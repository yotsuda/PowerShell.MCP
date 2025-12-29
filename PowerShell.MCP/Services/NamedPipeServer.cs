using System.IO.Pipes;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Diagnostics;

namespace PowerShell.MCP.Services;

/// <summary>
/// Static class for managing execution state
/// Status: standby / busy / completed
/// </summary>
public static class ExecutionState
{
    private static string _status = "standby";
    private static readonly Stopwatch _stopwatch = new();
    private static string _currentPipeline = "";
    
    // Unreported output (when result should be cached for later retrieval)
    private static string? _unreportedOutput = null;
    private static string? _unreportedPipeline = null;
    private static double _unreportedDuration = 0;
    
    // Flag: should cache output on completion (set when busy response is sent)
    private static bool _shouldCacheOutput = false;
    
    public static string Status => _status;
    public static string CurrentPipeline => _currentPipeline;
    
    /// <summary>
    /// Gets the elapsed time in seconds since execution started
    /// </summary>
    public static double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;
    
    /// <summary>
    /// Checks if there is unreported output
    /// </summary>
    public static bool HasUnreportedOutput => _unreportedOutput != null;
    
    /// <summary>
    /// Checks if output should be cached on completion
    /// </summary>
    public static bool ShouldCacheOutput => _shouldCacheOutput;
    
    public static void SetBusy(string pipeline)
    {
        _stopwatch.Restart();
        _currentPipeline = pipeline;
        _shouldCacheOutput = false;  // Reset flag
        _status = "busy";
    }
    
    /// <summary>
    /// Marks that the output should be cached on completion.
    /// Called when a busy response is sent to another request.
    /// </summary>
    public static void MarkForCaching()
    {
        _shouldCacheOutput = true;
    }
    
    /// <summary>
    /// Sets status to standby (command completed, result delivered successfully)
    /// </summary>
    public static void SetStandby()
    {
        _stopwatch.Stop();
        _currentPipeline = "";
        _shouldCacheOutput = false;
        _status = "standby";
    }
    
    /// <summary>
    /// Sets status to completed with unreported output
    /// </summary>
    public static void SetCompleted(string output)
    {
        _unreportedOutput = output;
        _unreportedPipeline = _currentPipeline;
        _unreportedDuration = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Stop();
        _currentPipeline = "";
        _shouldCacheOutput = false;
        _status = "completed";
    }
    
    /// <summary>
    /// Consumes unreported output (returns and clears it, transitions to standby)
    /// </summary>
    public static (string Output, string Pipeline, double Duration)? ConsumeUnreportedOutput()
    {
        if (_unreportedOutput == null)
            return null;
        
        var result = (_unreportedOutput, _unreportedPipeline ?? "", _unreportedDuration);
        _unreportedOutput = null;
        _unreportedPipeline = null;
        _unreportedDuration = 0;
        _status = "standby";
        return result;
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
                    // Consume unreported output (returns and clears it)
                    var unreported = ExecutionState.ConsumeUnreportedOutput();
                    if (unreported.HasValue)
                    {
                        statusResponse = JsonSerializer.Serialize(new
                        {
                            pid,
                            status = "completed",
                            pipeline = unreported.Value.Pipeline,
                            duration = Math.Round(unreported.Value.Duration, 2),
                            output = unreported.Value.Output
                        });
                    }
                    else
                    {
                        // Should not happen, but fallback to standby
                        statusResponse = JsonSerializer.Serialize(new { pid, status = "standby" });
                    }
                }
                else
                {
                    // standby
                    statusResponse = JsonSerializer.Serialize(new { pid, status = "standby" });
                }
                
                await SendMessageAsync(pipeServer, statusResponse, cancellationToken);
                return;
            }

            string? proxyVersion = requestRoot.TryGetProperty("proxy_version", out JsonElement proxyVersionElement)
                ? proxyVersionElement.GetString() : "Not detected";

            if (proxyVersion != MCPModuleInitializer.ServerVersion)
            {
                string output = McpServerHost.ExecuteSilentCommand("Get-MCPProxyPath");
                string proxyExePath = output[(output.LastIndexOfAny(['\r', '\n']) + 1)..];

                string versionErrorResponse;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    string escapedPath = McpServerHost.ExecuteSilentCommand("Get-MCPProxyPath -Escape");
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
                var busyResponse = $"⧗ Previous pipeline running | PID: {pid} | Status: Busy | Pipeline: {runningPipeline} | Duration: {elapsed:F2}s";

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
                    var result = await Task.Run(() => ExecuteTool(name!, requestRoot));
                    
                    // Check if output should be cached (busy response was sent to another request)
                    var shouldCache = ExecutionState.ShouldCacheOutput;
                    
                    // Try to send response
                    try
                    {
                        await SendMessageAsync(pipeServer, result, cancellationToken);
                        
                        if (shouldCache)
                        {
                            // Proxy sent another request while we were busy, cache the result
                            ExecutionState.SetCompleted(result);
                        }
                        else
                        {
                            ExecutionState.SetStandby();
                        }
                    }
                    catch
                    {
                        // Pipe error - save as unreported output
                        ExecutionState.SetCompleted(result);
                    }
                }
                catch (Exception ex)
                {
                    // Execution error - still need to return to standby
                    ExecutionState.SetStandby();
                    throw;
                }
            }
            else
            {
                // Other tools (get_current_location) - no state management needed
                var result = await Task.Run(() => ExecuteTool(name!, requestRoot));
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
    /// Executes a tool
    /// </summary>
    private static string ExecuteTool(string method, JsonElement parameters)
    {
        return method switch
        {
            "get_current_location" => MCPModuleInitializer.GetCurrentLocation(),
            "invoke_expression" => ExecuteInvokeExpression(parameters),
            _ => throw new ArgumentException($"Unknown method: {method}")
        };
    }

    /// <summary>
    /// Executes the invokeExpression tool
    /// </summary>
    private static string ExecuteInvokeExpression(JsonElement parameters)
    {
        var pipeline = parameters.GetProperty("pipeline").GetString() ?? "";
        var executeImmediately = parameters.TryGetProperty("execute_immediately", out var execElement) 
            ? execElement.GetBoolean() 
            : true;

        return McpServerHost.ExecuteCommand(pipeline, executeImmediately);
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
