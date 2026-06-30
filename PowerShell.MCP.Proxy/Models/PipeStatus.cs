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

    /// <summary>
    /// Returns true if a raw status string means the console is "busy" — a
    /// command is still running, INCLUDING the <see cref="AwaitingInput"/>
    /// sub-state (a command parked at a host prompt). Centralized (mirroring
    /// <see cref="IsReady"/>) so every caller that waits on or tracks busy
    /// consoles — wait_for_completion's poll set and the busy-status collector
    /// — treats awaiting_input uniformly, and a future status value is handled
    /// in one place instead of being silently dropped by a switch that forgot
    /// the new case. Null / unknown is treated as not-busy.
    /// </summary>
    public static bool IsBusy(string? status)
        => status == Busy || status == AwaitingInput;
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
