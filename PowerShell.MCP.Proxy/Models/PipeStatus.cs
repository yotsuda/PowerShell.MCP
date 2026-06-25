namespace PowerShell.MCP.Proxy.Models;

/// <summary>
/// Pipe status constants
/// </summary>
public static class PipeStatus
{
    public const string Standby = "standby";
    public const string Busy = "busy";
    public const string Completed = "completed";

    /// <summary>
    /// A command is running but parked at an interactive prompt (Read-Host,
    /// a missing mandatory parameter, a credential/choice prompt). It is a
    /// sub-state of busy — NOT ready/routable — surfaced separately only so
    /// the AI is told to answer-or-close rather than to wait.
    /// </summary>
    public const string AwaitingInput = "awaiting_input";

    /// <summary>
    /// Returns true if a raw status string means the console is "ready" — i.e.
    /// not busy, so the proxy may route to it now. Standby is idle; Completed
    /// has undrained output but is still routable. This is the single
    /// definition of readiness used across discovery, the new-standby probe,
    /// and WaitForPipeReadyAsync. Null / unknown is treated as not-ready.
    /// </summary>
    public static bool IsReady(string? status)
        => status == Standby || status == Completed;
}

/// <summary>
/// Extension methods for GetStatusResponse
/// </summary>
public static class GetStatusResponseExtensions
{
    /// <summary>
    /// Returns true if the pipe is ready (standby or completed)
    /// </summary>
    public static bool IsReady(this GetStatusResponse status)
        => PipeStatus.IsReady(status.Status);
}
