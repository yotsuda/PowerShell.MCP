using System.Runtime.InteropServices;

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
}
