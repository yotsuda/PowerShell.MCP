using System.IO.Pipes;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;

namespace PowerShell.MCP.Services;

/// <summary>
/// Static class for managing execution state
/// Status: standby / busy (status is derived from state, not stored)
/// </summary>
public static class ExecutionState
{
    private static bool _isBusy = false;
    private static readonly Stopwatch _stopwatch = new();
    private static string _currentPipeline = "";

    // Cached outputs (multiple outputs can accumulate)
    private static readonly List<string> _cachedOutputs = new();
    private static readonly object _cacheLock = new();

    // Flag: should cache output on completion (set when busy/timeout response is sent)
    private static bool _shouldCacheOutput = false;

    // Heartbeat tracking for detecting user-initiated commands
    private static DateTime _lastHeartbeat = DateTime.UtcNow;
    private const int HeartbeatTimeoutMs = 500; // If no heartbeat for 500ms, runspace is likely busy

    /// <summary>
    /// Gets the current status (derived from state)
    /// </summary>
    public static string Status
    {
        get
        {
            if (_isBusy) return "busy";
            lock (_cacheLock)
            {
                return _cachedOutputs.Count > 0 ? "completed" : "standby";
            }
        }
    }

    public static string CurrentPipeline => _currentPipeline;

    /// <summary>
    /// Gets the elapsed time in seconds since execution started
    /// </summary>
    public static double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

    /// <summary>
    /// Checks if there is cached output
    /// </summary>
    public static bool HasCachedOutput
    {
        get
        {
            lock (_cacheLock)
            {
                return _cachedOutputs.Count > 0;
            }
        }
    }

    /// <summary>
    /// Checks if output should be cached on completion
    /// </summary>
    public static bool ShouldCacheOutput => _shouldCacheOutput;

    /// <summary>
    /// Updates the heartbeat timestamp. Called from PowerShell timer event.
    /// </summary>
    public static void Heartbeat() => _lastHeartbeat = DateTime.UtcNow;

    /// <summary>
    /// Checks if the PowerShell runspace is available (heartbeat is recent)
    /// </summary>
    public static bool IsRunspaceAvailable =>
        (DateTime.UtcNow - _lastHeartbeat).TotalMilliseconds < HeartbeatTimeoutMs;

    public static void SetBusy(string pipeline)
    {
        _stopwatch.Restart();
        _currentPipeline = pipeline;
        _shouldCacheOutput = false;  // Reset flag
        _isBusy = true;
    }

    /// <summary>
    /// Marks that the output should be cached on completion.
    /// Called when a busy/timeout response is sent.
    /// </summary>
    public static void MarkForCaching()
    {
        _shouldCacheOutput = true;
    }

    /// <summary>
    /// Completes execution (called from finally block after command execution)
    /// </summary>
    public static void CompleteExecution()
    {
        _stopwatch.Stop();
        _currentPipeline = "";
        _isBusy = false;
    }

    /// <summary>
    /// Adds output to cache (called when MCP client won't receive the result directly)
    /// </summary>
    public static void AddToCache(string output)
    {
        lock (_cacheLock)
        {
            _cachedOutputs.Add(output);
        }
        _shouldCacheOutput = false;
    }

    /// <summary>
    /// Peeks cached outputs without consuming them
    /// </summary>
    public static IReadOnlyList<string> PeekCachedOutputs()
    {
        lock (_cacheLock)
        {
            return _cachedOutputs.ToList();
        }
    }

    /// <summary>
    /// Consumes all cached outputs (returns and clears them)
    /// </summary>
    public static IReadOnlyList<string> ConsumeCachedOutputs()
    {
        lock (_cacheLock)
        {
            var result = _cachedOutputs.ToList();
            _cachedOutputs.Clear();
            return result;
        }
    }
}

/// <summary>
/// Named Pipe server - handles communication with PowerShell.MCP.Proxy.exe
/// Returns "busy" response while command is executing
/// </summary>
public class NamedPipeServer : IDisposable
{
    public const string BasePipeName = "PowerShell.MCP.Communication";
    private const int MaxConcurrentConnections = 2; // Two pipe instances

    /// <summary>
    /// Gets the pipe name (with or without PID suffix depending on registration)
    /// </summary>
    public string PipeName { get; }

    private readonly CancellationTokenSource _internalCancellation = new();
    private readonly List<Task> _serverTasks = new();
    private bool _disposed = false;

    /// <summary>
    /// Creates a new Named Pipe server with the specified pipe name
    /// </summary>
    /// <param name="usePidSuffix">If true, append PID to pipe name</param>
    public NamedPipeServer(bool usePidSuffix)
    {
        PipeName = usePidSuffix
            ? $"{BasePipeName}.{Environment.ProcessId}"
            : BasePipeName;
    }

    /// <summary>
    /// Starts the Named Pipe server
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _internalCancellation.Token);

        try
        {
            // Start multiple server instances
            for (int i = 0; i < MaxConcurrentConnections; i++)
            {
                var task = RunServerInstanceAsync(combinedCts.Token);
                _serverTasks.Add(task);
            }

            // Wait for all server instances to complete
            await Task.WhenAll(_serverTasks);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is normal termination
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Named Pipe Server error: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs a server instance
    /// </summary>
    private async Task RunServerInstanceAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = CreateNamedPipeServer();

                // Wait for client connection
                await pipeServer.WaitForConnectionAsync(cancellationToken);

                // Handle communication with connected client
                await HandleClientAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Named Pipe Server instance error: {ex.Message}");

                // Wait a moment before retrying on error
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
    /// <summary>
    /// Creates a Named Pipe server
    /// </summary>
    private NamedPipeServerStream CreateNamedPipeServer()
    {
        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    /// <summary>
    /// Handles client communication
    /// </summary>
    private static async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        try
        {
            // Receive request
            var requestJson = await ReceiveMessageAsync(pipeServer, cancellationToken);

            // Parse JSON-RPC request
            using var requestDoc = JsonDocument.Parse(requestJson);
            var requestRoot = requestDoc.RootElement;

            var name = requestRoot.GetProperty("name").GetString();

            // Handle get_status request FIRST - returns immediately without using main runspace
            // Must be before version check because version check uses ExecuteSilentCommand which requires main runspace
            if (name == "get_status")
            {
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var status = ExecutionState.Status;

                string statusResponse;
                if (status == "busy")
                {
                    // Mark for caching - Proxy will use another pipe, so result won't be received
                    ExecutionState.MarkForCaching();

                    var elapsed = ExecutionState.ElapsedSeconds;
                    var runningPipeline = ExecutionState.CurrentPipeline;
                    statusResponse = JsonSerializer.Serialize(new
                    {
                        pid,
                        status = "busy",
                        pipeline = runningPipeline,
                        duration = Math.Round(elapsed, 2)
                    });
                }
                else if (status == "completed")
                {
                    // Has cached output(s) - report count (Proxy will call consume_output later)
                    var cachedOutputs = ExecutionState.PeekCachedOutputs();
                    statusResponse = JsonSerializer.Serialize(new
                    {
                        pid,
                        status = "completed",
                        cachedCount = cachedOutputs.Count
                    });
                }
                else
                {
                    // standby - but check if runspace is actually available (via heartbeat)
                    if (ExecutionState.IsRunspaceAvailable)
                    {
                        statusResponse = JsonSerializer.Serialize(new { pid, status = "standby" });
                    }
                    else
                    {
                        // Runspace is busy with user command (no heartbeat received recently)
                        statusResponse = JsonSerializer.Serialize(new
                        {
                            pid,
                            status = "busy",
                            pipeline = "(user command)",
                            duration = 0
                        });
                    }
                }

                await SendMessageAsync(pipeServer, statusResponse, cancellationToken);
                return;
            }

            // Handle consume_output request - consumes and returns all cached outputs
            if (name == "consume_output")
            {
                var cachedOutputs = ExecutionState.ConsumeCachedOutputs();
                // Combine all cached outputs with separators
                var combinedOutput = string.Join("\n\n", cachedOutputs);
                await SendMessageAsync(pipeServer, combinedOutput, cancellationToken);
                return;
            }

            string? proxyVersion = requestRoot.TryGetProperty("proxy_version", out JsonElement proxyVersionElement)
                ? proxyVersionElement.GetString() : "Not detected";

            if (proxyVersion != MCPModuleInitializer.ServerVersion)
            {
                var output = McpServerHost.ExecuteSilentCommand("Get-MCPProxyPath");
                string proxyExePath = output[(output.LastIndexOfAny(['\r', '\n']) + 1)..];

                string versionErrorResponse;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var escapedPath = McpServerHost.ExecuteSilentCommand("Get-MCPProxyPath -Escape");
                    escapedPath = escapedPath[(escapedPath.LastIndexOfAny(['\r', '\n']) + 1)..];

                    versionErrorResponse =
$@"PowerShell MCP Configuration Error

ISSUE: PowerShell.MCP.Proxy version is outdated.
- PowerShell.MCP module version: {MCPModuleInitializer.ServerVersion}
- Proxy executable version: {proxyVersion}

ACTION REQUIRED: Update your MCP client configuration
- Executable path: {proxyExePath}
- For JSON config, use escaped path: {escapedPath}

TIP: Run 'Get-MCPProxyPath -Escape' in PowerShell to get the properly escaped path for JSON configuration.

Please provide how to update the MCP client configuration to the user.";
                }
                else
                {
                    versionErrorResponse =
$@"PowerShell MCP Configuration Error

ISSUE: PowerShell.MCP.Proxy version is outdated.
- PowerShell.MCP module version: {MCPModuleInitializer.ServerVersion}
- Proxy executable version: {proxyVersion}

ACTION REQUIRED: Update your MCP client configuration
- Executable path: {proxyExePath}

TIP: Run 'Get-MCPProxyPath' in PowerShell to get the path for MCP client configuration.

Please provide how to update the MCP client configuration to the user.";
                }

                await SendMessageAsync(pipeServer, versionErrorResponse, cancellationToken);
                return;
            }

            // Check execution state
            if (ExecutionState.Status == "busy")
            {
                // Mark that the running command's output should be cached
                // (because Proxy won't receive it - it's sending a new request)
                ExecutionState.MarkForCaching();

                // Return busy response with status line including running pipeline
                var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var elapsed = ExecutionState.ElapsedSeconds;
                var runningPipeline = TruncatePipeline(ExecutionState.CurrentPipeline);
                var busyResponse = $"⧗ Previous pipeline running | pwsh PID: {pid} | Status: Busy | Pipeline: {runningPipeline} | Duration: {elapsed:F2}s";

                await SendMessageAsync(pipeServer, busyResponse, cancellationToken);
                return;
            }

            // Execute tool with state management for invoke_expression
            if (name == "invoke_expression")
            {
                var pipeline = requestRoot.TryGetProperty("pipeline", out var pipelineElement)
                    ? pipelineElement.GetString() ?? "" : "";

                ExecutionState.SetBusy(pipeline);

                try
                {
                    var (isTimeout, shouldCache) = await Task.Run(() => ExecuteInvokeExpression(requestRoot));

                    var pid = Process.GetCurrentProcess().Id;
                    var elapsed = ExecutionState.ElapsedSeconds;
                    var runningPipeline = TruncatePipeline(pipeline);

                    if (isTimeout)
                    {
                        // Timeout - send timeout response
                        var timeoutResponse = $"⧗ Pipeline is still running | pwsh PID: {pid} | Status: Busy | Pipeline: {runningPipeline} | Duration: {elapsed:F2}s\n\nUse wait_for_completion tool to wait and retrieve the result.";
                        try
                        {
                            await SendMessageAsync(pipeServer, timeoutResponse, cancellationToken);
                        }
                        catch
                        {
                            // Pipe error on timeout - nothing to do
                        }
                    }
                    else if (shouldCache)
                    {
                        // Result cached - MCP client disconnected, will be returned on next tool call
                        var cachedResponse = $"✓ Pipeline executed successfully | pwsh PID: {pid} | Status: Completed | Pipeline: {runningPipeline} | Duration: {elapsed:F2}s\n\nResult cached. Will be returned on next tool call.";
                        try
                        {
                            await SendMessageAsync(pipeServer, cachedResponse, cancellationToken);
                        }
                        catch
                        {
                            // Pipe error - result already cached, ignore
                        }
                    }
                    else
                    {
                        // Normal completion - consume all cached outputs and return
                        var outputs = ExecutionState.ConsumeCachedOutputs();
                        var result = string.Join("\n\n", outputs);
                        try
                        {
                            await SendMessageAsync(pipeServer, result, cancellationToken);
                        }
                        catch
                        {
                            // Pipe error - save result to cache for next request
                            ExecutionState.AddToCache(result);
                        }
                    }
                }
                catch (Exception)
                {
                    ExecutionState.CompleteExecution();
                    throw;
                }
            }
            else
            {
                // Other tools (get_current_location) - consume cached outputs and prepend to result
                var result = await Task.Run(() => ExecuteTool(name!, requestRoot));

                // Consume cached outputs and prepend (older first, then current result)
                var cachedOutputs = ExecutionState.ConsumeCachedOutputs();
                if (cachedOutputs.Count > 0)
                {
                    var combined = string.Join("\n\n", cachedOutputs);
                    result = combined + "\n\n" + result;
                }

                await SendMessageAsync(pipeServer, result, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Send error response
            var errorResponse = new
            {
                jsonrpc = "2.0",
                id = (string?)null,
                error = new
                {
                    code = -32603,
                    message = "Internal error",
                    data = ex.Message
                }
            };

            var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            try
            {
                await SendMessageAsync(pipeServer, errorJson, cancellationToken);
            }
            catch
            {
                // Give up if error response fails to send
            }
        }
    }

    /// <summary>
    /// Executes a tool (except invoke_expression which is handled separately)
    /// </summary>
    private static string ExecuteTool(string method, JsonElement parameters)
    {
        return method switch
        {
            "get_current_location" => MCPModuleInitializer.GetCurrentLocation(),
            _ => throw new ArgumentException($"Unknown method: {method}")
        };
    }

    /// <summary>
    /// Executes the invokeExpression tool
    /// </summary>
    /// <returns>Tuple of (isTimeout, shouldCache)</returns>
    private static (bool isTimeout, bool shouldCache) ExecuteInvokeExpression(JsonElement parameters)
    {
        var pipeline = parameters.GetProperty("pipeline").GetString() ?? "";
        return McpServerHost.ExecuteCommand(pipeline);
    }


    /// <summary>
    /// Receives a message from Named Pipe
    /// </summary>
    private static async Task<string> ReceiveMessageAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        // Receive message length (4 bytes)
        var lengthBytes = new byte[4];
        await ReadExactAsync(pipeServer, lengthBytes, cancellationToken);
        var messageLength = BitConverter.ToInt32(lengthBytes, 0);

        if (messageLength <= 0)
        {
            throw new InvalidOperationException($"Invalid message length: {messageLength}");
        }

        // Receive message body
        var messageBytes = new byte[messageLength];
        await ReadExactAsync(pipeServer, messageBytes, cancellationToken);

        return Encoding.UTF8.GetString(messageBytes);
    }

    /// <summary>
    /// Sends a message to Named Pipe
    /// </summary>
    public static async Task SendMessageAsync(NamedPipeServerStream pipeServer, string message, CancellationToken cancellationToken)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        // Send message length (4 bytes)
        await pipeServer.WriteAsync(lengthBytes, 0, 4, cancellationToken);

        // Send message body
        await pipeServer.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
        await pipeServer.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Reads exactly the specified number of bytes
    /// </summary>
    private static async Task ReadExactAsync(NamedPipeServerStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead, cancellationToken);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading data");
            }
            totalBytesRead += bytesRead;
        }
    }

    /// <summary>
    /// Truncates pipeline string for status display
    /// </summary>
    private static string TruncatePipeline(string pipeline)
    {
        if (string.IsNullOrEmpty(pipeline))
            return "";

        // Take first line
        var firstLine = pipeline.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? pipeline;

        // Take first pipe segment
        var firstSegment = firstLine.Split('|').FirstOrDefault()?.Trim() ?? firstLine;

        // Truncate if too long
        const int maxLength = 30;
        if (firstSegment.Length > maxLength)
            return firstSegment[..(maxLength - 3)] + "...";

        // Add "..." if truncated by newline or pipe
        if (firstSegment != pipeline)
            return firstSegment + "...";

        return firstSegment;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _internalCancellation.Cancel();
            _internalCancellation.Dispose();
            _disposed = true;
        }
    }
}
