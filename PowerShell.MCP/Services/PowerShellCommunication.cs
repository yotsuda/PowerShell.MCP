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
    }

    /// <summary>
    /// Waits for result (blocking method)
    /// </summary>
    public static string WaitForResult()
    {
        var endTime = DateTime.UtcNow.AddSeconds(4 * 60 + 50);
        
        // Clear previous result
        _currentResult = null;
        _resultReadyEvent.Reset();
        
        // Set execution state to Busy
        ExecutionState.SetBusy();
        
        try
        {
            var remainingTime = endTime - DateTime.UtcNow;
            var timeoutMs = (int)Math.Max(0, remainingTime.TotalMilliseconds);
            
            // Block waiting with ManualResetEvent
            bool signaled = _resultReadyEvent.WaitOne(timeoutMs);
            
            if (signaled)
            {
                return _currentResult ?? "No result available";
            }
            else
            {
                return "Command execution timed out (4 minutes 50 seconds). Your command is still running. Consider restarting the PowerShell console.";
            }
        }
        finally
        {
            // Always return to Idle after execution completes
            ExecutionState.SetIdle();
        }
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
    /// Command execution
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

            // Wait for result with state management
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
