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
        // Don't cache here - HandleClientAsync will do it based on ShouldCacheOutput flag
        _currentResult = result;
        _resultReadyEvent.Set();
    }

    /// <summary>
    /// Waits for result (blocking method)
    /// </summary>
    public static string WaitForResult()
    {
        // Clear previous result
        _currentResult = null;
        _resultReadyEvent.Reset();

        // Wait for ~3 minutes (before MCP client timeout)
        const int timeoutMs = 170 * 1000; // 2 minutes 50 seconds
        bool signaled = _resultReadyEvent.WaitOne(timeoutMs);

        if (signaled)
        {
            return _currentResult ?? "No result available";
        }

        // Timeout - command still running
        // Mark for caching (NotifyResultReady will cache the actual result when done)
        ExecutionState.MarkForCaching();

        // Return immediately WITHOUT waiting for completion
        return "Command is still running. Use wait_for_completion tool to wait and retrieve the result. If you have other tasks to run in parallel, use invoke_expression - a new console will be started automatically if needed.";
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
