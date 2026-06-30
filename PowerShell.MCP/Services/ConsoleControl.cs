using System.Reflection;
using System.Runtime.InteropServices;
using System.Management.Automation.Runspaces;

namespace PowerShell.MCP.Services;

/// <summary>
/// Console-level control primitive used by the pipe server to interrupt an
/// AI-dispatched command that is running in this console. Runs on the pipe
/// server's background thread, so it fires even while the runspace's home
/// thread is busy. Mirrors a human pressing Ctrl+C.
/// </summary>
/// <remarks>
/// This raises a real CTRL_C_EVENT, which interrupts NATIVE console reads
/// (git/npm/ssh/cmd waiting on stdin). It does NOT reliably interrupt a
/// PowerShell HOST prompt (Read-Host / a missing mandatory parameter):
/// those run via the polling-engine event and the signal does not unwind
/// the host read. For a PowerShell prompt the recovery path is to answer it
/// (at the terminal) or close the console (see the close_console tool and
/// the awaiting-input notice the proxy surfaces).
/// </remarks>
public static class ConsoleControl
{
    private const uint CTRL_C_EVENT = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    /// <summary>
    /// Sends a real Ctrl+C (CTRL_C_EVENT) to this process's console group —
    /// the same signal a human Ctrl+C raises. pwsh's handler stops the
    /// running pipeline and any native child receives the break too; pwsh
    /// itself survives. Group id 0 targets every process attached to the
    /// caller's console.
    /// </summary>
    public static bool SendCtrlC() => GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);

    // ===== Pipeline-level cancel (for PowerShell-side commands) =====
    //
    // SendCtrlC only reaches NATIVE children (git/npm/ssh waiting on stdin);
    // it does NOT stop a running PowerShell pipeline (Start-Sleep, a runaway
    // loop) because the polling-engine action isn't pwsh's "current pipeline"
    // from the console-ctrl handler's point of view. To stop those we stop
    // the home runspace's currently-running pipeline directly — the same
    // primitive PowerShell's own engine uses for Ctrl+C. The home runspace
    // and the poll timer are captured once at engine setup (on the home
    // thread, where Runspace.DefaultRunspace is the module's runspace);
    // the pipe-server background thread can't read them itself.

    private static Runspace? _homeRunspace;
    private static System.Timers.Timer? _pollTimer;

    /// <summary>
    /// Called once from MCPPollingEngine.ps1 on the home thread at setup,
    /// handing C# the references it needs to interrupt and re-arm the engine
    /// from the pipe-server background thread.
    /// </summary>
    public static void RegisterEngine(Runspace homeRunspace, System.Timers.Timer pollTimer)
    {
        _homeRunspace = homeRunspace;
        _pollTimer = pollTimer;
    }

    /// <summary>
    /// Stops the command currently running in the home runspace — interrupts
    /// PowerShell pipelines/cmdlets/loops cooperatively (Start-Sleep, while
    /// loops). A command stuck in a non-cooperative blocking call
    /// (Thread.Sleep, blocking I/O) is NOT interruptible — Pipeline.Stop()
    /// blocks until that call returns — so the Stop is issued on a background
    /// thread and this method returns immediately without waiting for it.
    /// <para>
    /// GetCurrentlyRunningPipeline is internal on LocalRunspace, so it is
    /// reached by reflection (stable since PS 1.0). On reflection failure the
    /// method is a no-op and the caller falls back to SendCtrlC alone.
    /// </para>
    /// Returns true if a running pipeline was found and a stop was issued.
    /// </summary>
    public static bool StopRunningPipeline()
    {
        var rs = _homeRunspace;
        if (rs == null) return false;
        try
        {
            var method = rs.GetType().GetMethod(
                "GetCurrentlyRunningPipeline",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null) return false;

            if (method.Invoke(rs, null) is not Pipeline pipe) return false;

            // Stop() blocks until the pipeline actually stops; for a
            // non-cooperative blocking call that is until the call returns.
            // Fire-and-forget so a stuck command can't wedge the cancel ack.
            System.Threading.Tasks.Task.Run(() =>
            {
                try { pipe.Stop(); } catch { }
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Belt-and-suspenders: re-arm the engine's one-shot poll timer from the
    /// pipe-server thread so the engine keeps ticking even if a stopped
    /// action's <c>finally { $McpTimer.Start() }</c> somehow did not run.
    /// System.Timers.Timer is thread-safe; .Start() only arms the timer
    /// (Elapsed still fires on a thread-pool thread and the action is queued
    /// to the home thread), and a one-shot timer keeps exactly one pending
    /// fire no matter how many times Start() is called — verified — so this
    /// cannot accumulate ticks. The Enabled guard avoids a redundant arm on
    /// the normal path where the engine's finally already re-armed it.
    /// </summary>
    public static void EnsurePollTimerArmed()
    {
        var t = _pollTimer;
        if (t == null) return;
        try { if (!t.Enabled) t.Start(); } catch { }
    }
}
