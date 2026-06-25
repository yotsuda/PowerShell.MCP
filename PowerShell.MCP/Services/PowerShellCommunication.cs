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

    // Set by the host-UI decorator (TeePSHostUserInterface) the moment an
    // AI-dispatched command enters an interactive read/prompt. A blocked
    // prompt produces no result and no output, so without this signal
    // WaitForResult would sit idle until the full timeout. Instead the
    // signal wakes WaitForResult immediately so the proxy can hand control
    // back to the AI (treated like a timeout) while the command stays
    // blocked — the human can still answer at the terminal, or the AI can
    // abandon the console (close_console).
    private static bool _awaitingInput = false;
    private static string? _promptText = null;

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
    public static (bool isTimeout, bool shouldCache, bool awaitingInput, string? promptText) WaitForResult(int timeoutSeconds = 170)
    {
        lock (_lock)
        {
            _resultShouldCache = false;
            _awaitingInput = false;
            _promptText = null;
            var myGeneration = ++_submittedGeneration;
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (_completedGeneration < myGeneration)
            {
                // Prompt detected: the command is now blocked on an
                // interactive read. Hand control back to the AI right away
                // (same treatment as a timeout) rather than waiting out the
                // full timeout. The command stays running/blocked; its
                // eventual result is cached (MarkForCaching) for a later drain.
                if (_awaitingInput)
                {
                    ExecutionState.MarkForCaching();
                    return (false, false, true, _promptText);
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    // Timeout - mark for caching so NotifyResultReady will know
                    ExecutionState.MarkForCaching();
                    return (true, false, false, null);
                }
                Monitor.Wait(_lock, remaining);
            }

            return (false, _resultShouldCache, false, null);
        }
    }

    /// <summary>
    /// Called by the host-UI decorator when an AI-dispatched command enters
    /// an interactive read/prompt. Wakes WaitForResult so the proxy can
    /// return control to the AI immediately. The command itself stays
    /// blocked in the read until answered at the terminal or the console
    /// is closed.
    /// </summary>
    public static void SignalAwaitingInput(string? promptText)
    {
        lock (_lock)
        {
            _awaitingInput = true;
            _promptText = promptText;
            Monitor.PulseAll(_lock);
        }
    }

    /// <summary>Cleared by the decorator when the interactive read returns.</summary>
    public static void ClearAwaitingInput()
    {
        lock (_lock)
        {
            _awaitingInput = false;
            _promptText = null;
        }
    }

    /// <summary>True while a dispatched command is blocked at a prompt (for get_status).</summary>
    public static bool IsAwaitingInput { get { lock (_lock) { return _awaitingInput; } } }

    /// <summary>The prompt caption/field text the command is waiting on, if any.</summary>
    public static string? AwaitingPromptText { get { lock (_lock) { return _promptText; } } }

    /// <summary>
    /// Test hook: resets the submitted/completed generation counters to a known
    /// balanced state so unit tests of the wait/notify protocol are independent
    /// of execution order. Not used in production.
    /// </summary>
    internal static void ResetGenerationsForTests()
    {
        lock (_lock)
        {
            _submittedGeneration = 0;
            _completedGeneration = 0;
            _resultShouldCache = false;
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
    public static (bool isTimeout, bool shouldCache, bool awaitingInput, string? promptText) ExecuteCommand(string command, Dictionary<string, string>? variables = null, int timeoutSeconds = 170)
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
            return (false, false, false, null);
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
        // Engine not running (startup blocked, e.g. AMSI/AV false positive):
        // nothing would consume the stashed command, so WaitForResult would
        // block the full timeout. Fast-fail with guidance instead.
        if (!MCPModuleInitializer.EngineReady)
            return MCPModuleInitializer.GetEngineNotReadyMessage();

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