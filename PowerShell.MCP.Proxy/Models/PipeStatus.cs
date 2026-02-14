namespace PowerShell.MCP.Proxy.Models;

/// <summary>
/// Pipe status constants
/// </summary>
public static class PipeStatus
{
    public const string Standby = "standby";
    public const string Busy = "busy";
    public const string Completed = "completed";
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
        => status.Status == PipeStatus.Standby || status.Status == PipeStatus.Completed;
}
