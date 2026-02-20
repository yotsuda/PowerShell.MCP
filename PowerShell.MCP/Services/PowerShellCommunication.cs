namespace PowerShell.MCP.Services;

/// <summary>
/// Class for managing communication with PowerShell.
/// Uses Monitor.Wait/PulseAll with a generation counter to prevent
/// lost wakeups from late-completing timed-out commands.
/// </summary>
public static class PowerShellCommunication
{
    private static readonly object _lock = new();
    private static bool _resultShouldCache = false;
    private static long _submittedGeneration = 0;
    private static long _completedGeneration = 0;

    /// <summary>
    /// Method to notify that the result is ready.
    /// Explicitly called from PowerShell script.
    /// </summary>
    public static void NotifyResultReady(string result)
    {
        // Capture cache flag before AddToCache resets it
        var shouldCache = ExecutionState.ShouldCacheOutput;

        // Truncate large output before caching to reduce pipe transfer and memory overhead
        var output = OutputTruncationHelper.TruncateIfNeeded(result);
        ExecutionState.AddToCache(output);
        ExecutionState.CompleteExecution();

        lock (_lock)
        {
            _resultShouldCache = shouldCache;
            _completedGeneration++;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    /// Silent command completed — no truncation (internal use, small known outputs)
    /// </summary>
    public static void NotifySilentResultReady(string result)
    {
        ExecutionState.AddToCache(result);
        ExecutionState.CompleteExecution();

        lock (_lock)
        {
            _completedGeneration++;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>
    /// Waits for result (blocking method).
    /// Uses a submitted/completed generation counter pair so that a late
    /// completion from a previously timed-out command cannot falsely
    /// satisfy the current wait.
    /// </summary>
    /// <param name="timeoutSeconds">Timeout in seconds (1-170)</param>
    /// <returns>Tuple of (isTimeout, shouldCache)</returns>
    public static (bool isTimeout, bool shouldCache) WaitForResult(int timeoutSeconds = 170)
    {
        lock (_lock)
        {
            _resultShouldCache = false;
            var myGeneration = ++_submittedGeneration;
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (_completedGeneration < myGeneration)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    // Timeout - mark for caching so NotifyResultReady will know
                    ExecutionState.MarkForCaching();
                    return (true, false);
                }
                Monitor.Wait(_lock, remaining);
            }

            return (false, _resultShouldCache);
        }
    }
}

/// <summary>
/// Host class for MCP communication.
/// Command fields are protected by _commandLock and exposed via atomic
/// Consume methods to prevent partial reads across fields.
/// </summary>
public static class McpServerHost
{
    /// <summary>
    /// Immutable snapshot of a pending command and its variables.
    /// </summary>
    public record CommandSlot(string? Command, Dictionary<string, string>? Variables);

    private static readonly object _commandLock = new();

    // Command fields - accessed from both pipe server thread and PowerShell timer thread
    private static string? _executeCommand;
    private static string? _executeCommandSilent;
    private static Dictionary<string, string>? _executeCommandVariables;

    /// <summary>
    /// Atomically reads and clears the pending command and its variables.
    /// Called from the PowerShell polling engine.
    /// </summary>
    public static CommandSlot ConsumeCommand()
    {
        lock (_commandLock)
        {
            var result = new CommandSlot(_executeCommand, _executeCommandVariables);
            _executeCommand = null;
            _executeCommandVariables = null;
            return result;
        }
    }

    /// <summary>
    /// Atomically reads and clears the pending silent command.
    /// Called from the PowerShell polling engine.
    /// </summary>
    public static string? ConsumeSilentCommand()
    {
        lock (_commandLock)
        {
            var cmd = _executeCommandSilent;
            _executeCommandSilent = null;
            return cmd;
        }
    }

    // Lock to prevent concurrent ExecuteCommand/ExecuteSilentCommand
    private static readonly SemaphoreSlim _executionLock = new(1, 1);

    /// <summary>
    /// Command execution (state management is handled by NamedPipeServer)
    /// </summary>
    /// <param name="command">PowerShell command to execute</param>
    /// <param name="timeoutSeconds">Timeout in seconds (1-170)</param>
    public static (bool isTimeout, bool shouldCache) ExecuteCommand(string command, Dictionary<string, string>? variables = null, int timeoutSeconds = 170)
    {
        _executionLock.Wait();
        try
        {
            lock (_commandLock)
            {
                _executeCommandVariables = variables;
                _executeCommand = command;
            }
            return PowerShellCommunication.WaitForResult(timeoutSeconds);
        }
        catch (Exception)
        {
            return (false, false);
        }
        finally
        {
            _executionLock.Release();
        }
    }

    /// <summary>
    /// Silent execution - returns result directly (for internal use)
    /// </summary>
    public static string ExecuteSilentCommand(string command)
    {
        _executionLock.Wait();
        try
        {
            lock (_commandLock)
            {
                _executeCommandSilent = command;
            }
            PowerShellCommunication.WaitForResult();
            // Silent command always consumes and returns the result
            var outputs = ExecutionState.ConsumeCachedOutputs();
            return string.Join("\n\n", outputs);
        }
        catch (Exception ex)
        {
            return $"Error executing silent command: {ex.Message}";
        }
        finally
        {
            _executionLock.Release();
        }
    }
}