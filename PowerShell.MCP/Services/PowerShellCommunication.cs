namespace PowerShell.MCP.Services;

/// <summary>
/// Class for managing communication with PowerShell
/// </summary>
public static class PowerShellCommunication
{
    private static readonly ManualResetEvent _resultReadyEvent = new(false);
    private static string? _currentResult = null;

    /// <summary>
    /// Method to notify that the result is ready
    /// Explicitly called from PowerShell script
    /// </summary>
    public static void NotifyResultReady(string result)
    {
        _currentResult = result;
        _resultReadyEvent.Set();

        // If marked for caching (e.g., WaitForResult timed out), cache the actual result
        if (ExecutionState.ShouldCacheOutput)
        {
            ExecutionState.SetCompleted(result);
        }
    }

    /// <summary>
    /// Waits for result (blocking method)
    /// </summary>
    public static string WaitForResult()
    {
        // Clear previous result
        _currentResult = null;
        _resultReadyEvent.Reset();

        // First, wait for 4 minutes (before MCP 5-minute timeout)
        const int markForCachingMs = 4 * 60 * 1000; // 4 minutes
        bool signaled = _resultReadyEvent.WaitOne(markForCachingMs);

        if (signaled)
        {
            return _currentResult ?? "No result available";
        }

        // After 4 minutes, mark for caching (MCP will timeout at 5m, result will be cached)
        ExecutionState.MarkForCaching();

        // Continue waiting for the actual result (will be cached via NotifyResultReady)
        _resultReadyEvent.WaitOne();

        // Return info message instead of actual result (to avoid duplicate reporting)
        return "Command completed. Output cached and will be included in next get_current_location or invoke_expression response.";
    }
}

/// <summary>
/// Host class for MCP communication
/// </summary>
public static class McpServerHost
{
    // Existing communication properties
    public static volatile string? executeCommand;
    public static volatile string? insertCommand;
    public static volatile string? executeCommandSilent;
    /// <summary>
    /// Command execution (state management is handled by NamedPipeServer)
    /// </summary>
    public static string ExecuteCommand(string command, bool executeImmediately = true)
    {
        try
        {
            if (executeImmediately)
            {
                executeCommand = command;
            }
            else
            {
                insertCommand = command;
            }

            // Wait for result
            return PowerShellCommunication.WaitForResult();
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    /// <summary>
    /// Silent execution
    /// </summary>
    public static string ExecuteSilentCommand(string command)
    {
        // Note: Busy check is done at Named Pipe level (HandleClientAsync)
        // No need to check here - would block ourselves
        try
        {
            executeCommandSilent = command;

            // Process including state management using WaitForResult()
            return PowerShellCommunication.WaitForResult();
        }
        catch (Exception ex)
        {
            return $"Error executing silent command: {ex.Message}";
        }
    }
}
