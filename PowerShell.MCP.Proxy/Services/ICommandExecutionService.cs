namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Type of command execution result
/// </summary>
public enum ExecutionResultType
{
    Success,
    Timeout,
    Busy,
    Completed,
    Error
}

/// <summary>
/// Result of command execution
/// </summary>
public record ExecutionResult(
    ExecutionResultType Type,
    string? Output,
    string? StatusLine,
    int Pid,
    double Duration,
    string? Pipeline,
    string? BusyReason
);

/// <summary>
/// Service for executing PowerShell commands
/// </summary>
public interface ICommandExecutionService
{
    /// <summary>
    /// Execute a command and parse the result
    /// </summary>
    Task<ExecutionResult> ExecuteAsync(
        string pipeName,
        string pipeline,
        int timeoutSeconds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Check for local variable assignments without scope prefix
    /// </summary>
    string? CheckVariableScopeWarning(string pipeline);
}