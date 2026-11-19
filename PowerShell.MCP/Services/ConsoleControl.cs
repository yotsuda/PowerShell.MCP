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
/// On Windows this raises a real CTRL_C_EVENT, and on Linux and macOS a
/// SIGINT to the running command's processes (see <see cref="SendCtrlC"/>);
/// either interrupts NATIVE console reads (git/npm/ssh/cmd waiting on stdin). It does NOT reliably interrupt a
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
    /// Sends a Ctrl+C to the native commands running under this console.
    /// On Windows that is a real CTRL_C_EVENT to the console group (id 0:
    /// every process attached to the caller's console) — pwsh's handler and
    /// any native child receive it, and pwsh survives. On Linux and macOS it
    /// is SIGINT to the processes the running command started — this
    /// process's descendants started since the command began, grandchildren
    /// included, as a terminal's Ctrl+C reaches the whole job; not to pwsh
    /// itself (its own handling raced the pipeline stop), and not to children
    /// that were already there (Start-Job workers, servers from Start-Process).
    /// The cancel handler lists them first (<see cref="CommandProcesses"/>),
    /// marks the pipeline stopping (<see cref="StopRunningPipeline"/>) so what
    /// follows the native command does not run, and then signals them
    /// (<see cref="Interrupt"/>), waiting up to 2 s for them to exit.
    /// (kernel32, called there before, threw — cancel failed outright.)
    /// </summary>
    public static bool SendCtrlC()
    {
        if (OperatingSystem.IsWindows())
            return GenerateConsoleCtrlEvent(CTRL_C_EVENT, 0);
        return ExecutionState.Status == "busy" && Interrupt(CommandProcesses());
    }

    /// <summary>
    /// The running command's processes: this process's descendants started
    /// since the command began (see <see cref="CommandDescendants"/>).
    /// Listed before the pipeline is stopped, which ends the direct child and
    /// would orphan the rest out of reach. Linux/macOS signal them
    /// (<see cref="Interrupt"/>); Windows ends those still running 2 s after
    /// the Ctrl+C (<see cref="EndSurvivors"/>). Empty when there are none or
    /// the process tree cannot be read.
    /// </summary>
    public static IReadOnlyList<int> CommandProcesses()
    {
        try
        {
            var since = DateTime.Now - TimeSpan.FromSeconds(ExecutionState.ElapsedSeconds + 1);
            return CommandDescendants(Environment.ProcessId, since);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Linux/macOS: SIGINT to each of <paramref name="targets"/>, then waits
    /// up to 2 s for them to exit (signal 0 only checks that one exists).
    /// </summary>
    public static bool Interrupt(IReadOnlyList<int> targets)
    {
        try
        {
            foreach (var pid in targets) kill(pid, SIGINT);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (targets.Any(pid => kill(pid, 0) == 0) && DateTime.UtcNow < deadline)
                System.Threading.Thread.Sleep(100);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Windows: waits up to 2 s for <paramref name="targets"/> to exit after the
    /// Ctrl+C, then ends the ones still running and returns them as
    /// "name (pid N)". A CTRL_C_EVENT does not end every native command: a
    /// program can handle or outlive it (lilypond 2.26.0 went on and finished
    /// its job), and PowerShell waits for it, so the runspace stays occupied —
    /// its heartbeat stops, the console reads as busy with "(user command)",
    /// and when the command finally ends the console may close itself as idle
    /// (docs/Issue-Console-Stuck-After-Cancel-2026-11-19.md). A cancel means
    /// stop, so what is left after the grace period is ended. Only processes
    /// the command started are listed (see <see cref="CommandProcesses"/>), so
    /// a Start-Process server or a Start-Job worker from before is untouched.
    /// </summary>
    public static IReadOnlyList<string> EndSurvivors(IReadOnlyList<int> targets, int graceMs = 2000)
    {
        var ended = new List<string>();
        if (targets.Count == 0) return ended;
        var deadline = DateTime.UtcNow.AddMilliseconds(graceMs);
        while (DateTime.UtcNow < deadline && targets.Any(IsRunning))
            System.Threading.Thread.Sleep(100);
        foreach (var pid in targets)
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                if (p.HasExited) continue;
                var name = p.ProcessName;
                p.Kill();
                ended.Add($"{name} (pid {pid})");
            }
            catch { /* exited meanwhile, or not ours to end */ }
        }
        return ended;
    }

    private static bool IsRunning(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }

    private const int SIGINT = 2;

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    /// <summary>
    /// This process's descendants started since <paramref name="since"/> —
    /// the running command's native processes and theirs, as a terminal's
    /// Ctrl+C would reach them. A child that was already there (a job worker,
    /// a server started earlier) is left out, and so is everything under it.
    /// </summary>
    private static List<int> CommandDescendants(int root, DateTime since)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (pid, ppid) in ProcessParents())
        {
            if (!children.TryGetValue(ppid, out var list)) children[ppid] = list = [];
            list.Add(pid);
        }
        var result = new List<int>();
        var pending = new Queue<int>();
        pending.Enqueue(root);
        while (pending.Count > 0 && result.Count < 256)
        {
            if (!children.TryGetValue(pending.Dequeue(), out var list)) continue;
            foreach (var pid in list)
            {
                DateTime started;
                try { using var p = System.Diagnostics.Process.GetProcessById(pid); started = p.StartTime; }
                catch { continue; } // gone already
                if (started < since) continue;
                result.Add(pid);
                pending.Enqueue(pid);
            }
        }
        return result;
    }

    /// <summary>(pid, parent pid) of every process: a Toolhelp snapshot on Windows, /proc on Linux, ps elsewhere.</summary>
    private static IEnumerable<(int pid, int ppid)> ProcessParents()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var pair in WindowsProcessParents()) yield return pair;
            yield break;
        }
        if (OperatingSystem.IsLinux())
        {
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
                string stat;
                try { stat = File.ReadAllText(Path.Combine(dir, "stat")); } catch { continue; }
                // "pid (comm) state ppid ...": comm may hold spaces and parentheses.
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                if (fields.Length > 1 && int.TryParse(fields[1], out var ppid)) yield return (pid, ppid);
            }
            yield break;
        }
        using var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ps", ["-A", "-o", "pid=", "-o", "ppid="])
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        if (ps == null) yield break;
        var output = ps.StandardOutput.ReadToEndAsync();
        if (!ps.WaitForExit(2000)) { try { ps.Kill(); } catch { } yield break; }
        foreach (var line in output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var ppid))
                yield return (pid, ppid);
        }
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Windows: (pid, parent pid) of every process, from one Toolhelp snapshot.</summary>
    private static List<(int pid, int ppid)> WindowsProcessParents()
    {
        var result = new List<(int, int)>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return result;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref entry)) return result;
            do { result.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID)); }
            while (Process32NextW(snap, ref entry));
        }
        finally { CloseHandle(snap); }
        return result;
    }

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
