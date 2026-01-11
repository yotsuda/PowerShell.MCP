namespace PowerShell.MCP.Services;

/// <summary>
/// Class for managing communication with PowerShell
/// </summary>
public static class PowerShellCommunication
{
    private static readonly ManualResetEvent _resultReadyEvent = new(false);
    private static bool _resultShouldCache = false;

    /// <summary>
    /// Method to notify that the result is ready
    /// Explicitly called from PowerShell script
    /// </summary>
    public static void NotifyResultReady(string result)
    {
        // Capture cache flag before AddToCache resets it
        _resultShouldCache = ExecutionState.ShouldCacheOutput;
        
        // Always add to cache
        ExecutionState.AddToCache(result);
        ExecutionState.CompleteExecution();
        _resultReadyEvent.Set();
    }

    /// <summary>
    /// Waits for result (blocking method)
    /// </summary>
    /// <returns>Tuple of (isTimeout, shouldCache)</returns>
    public static (bool isTimeout, bool shouldCache) WaitForResult()
    {
        _resultShouldCache = false;
        _resultReadyEvent.Reset();

        bool signaled = _resultReadyEvent.WaitOne(170 * 1000);

        if (signaled)
        {
            return (false, _resultShouldCache);
        }

        // Timeout - mark for caching so NotifyResultReady will know
        ExecutionState.MarkForCaching();
        return (true, false);
    }
}

/// <summary>
/// Host class for MCP communication
/// </summary>
public static class McpServerHost
{
    // Communication properties
    public static volatile string? executeCommand;
    public static volatile string? executeCommandSilent;

    /// <summary>
    /// Command execution (state management is handled by NamedPipeServer)
    /// </summary>
    public static (bool isTimeout, bool shouldCache) ExecuteCommand(string command)
    {
        try
        {
            executeCommand = command;
            return PowerShellCommunication.WaitForResult();
        }
        catch (Exception)
        {
            return (false, false);
        }
    }

    /// <summary>
    /// Silent execution - returns result directly (for internal use)
    /// </summary>
    public static string ExecuteSilentCommand(string command)
    {
        try
        {
            executeCommandSilent = command;
            PowerShellCommunication.WaitForResult();
            // Silent command always consumes and returns the result
            var outputs = ExecutionState.ConsumeCachedOutputs();
            return string.Join("\n\n", outputs);
        }
        catch (Exception ex)
        {
            return $"Error executing silent command: {ex.Message}";
        }
    }
}