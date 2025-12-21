using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Services;

/// <summary>
/// Static class for managing execution state
/// </summary>
public static class ExecutionState
{
    private static volatile string _status = "idle";
    
    public static string Status => _status;
    public static void SetBusy() => _status = "busy";
    public static void SetIdle() => _status = "idle";
}

/// <summary>
/// Named Pipe server - handles communication with PowerShell.MCP.Proxy.exe
/// Returns "busy" response while command is executing
/// </summary>
public class NamedPipeServer : IDisposable
{
    public const string PipeName = "PowerShell.MCP.Communication";
    private const int MaxConcurrentConnections = 1; // Single pipe instance
    private readonly CancellationTokenSource _internalCancellation = new();
    private readonly List<Task> _serverTasks = new();
    private bool _disposed = false;

    /// <summary>
    /// Starts the Named Pipe server
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"[DEBUG] NamedPipeServer.StartAsync called, MaxConcurrentConnections={MaxConcurrentConnections}");
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _internalCancellation.Token);

        try
        {
            // Start multiple server instances
            for (int i = 0; i < MaxConcurrentConnections; i++)
            {
                Console.Error.WriteLine($"[DEBUG] Starting server instance {i + 1}/{MaxConcurrentConnections}");
                var task = RunServerInstanceAsync(combinedCts.Token);
                _serverTasks.Add(task);
            }

            Console.Error.WriteLine("[DEBUG] All server instances started, waiting for completion...");
            // Wait for all server instances to complete
            await Task.WhenAll(_serverTasks);
            Console.Error.WriteLine("[DEBUG] All server instances completed");
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[DEBUG] StartAsync cancelled");
            // Cancellation is normal termination
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Named Pipe Server error: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
        }
    }

    private async Task RunServerInstanceAsync(CancellationToken cancellationToken)
    {
        Console.Error.WriteLine($"[DEBUG] RunServerInstanceAsync started on thread {Environment.CurrentManagedThreadId}");
        Console.Error.Flush();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.Error.WriteLine("[DEBUG] Creating Named Pipe server...");
                Console.Error.Flush();
                using var pipeServer = CreateNamedPipeServer();
                Console.Error.WriteLine($"[DEBUG] Named Pipe server created, waiting for connection...");
                Console.Error.Flush();
                
                // Wait for client connection
                Console.Error.WriteLine("[DEBUG] Before WaitForConnectionAsync");
                Console.Error.Flush();
                await pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                Console.Error.WriteLine("[DEBUG] After WaitForConnectionAsync - Client connected to Named Pipe");
                Console.Error.Flush();
                
                // Handle communication with connected client
                await HandleClientAsync(pipeServer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[DEBUG] Named Pipe server cancelled");
                Console.Error.Flush();
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Named Pipe Server instance error: {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                Console.Error.Flush();
                
                // Wait a moment before retrying on error
                try
                {
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        Console.Error.WriteLine("[DEBUG] RunServerInstanceAsync exiting");
        Console.Error.Flush();
    }

    /// <summary>
    /// Creates a Named Pipe server
    /// </summary>
    private static NamedPipeServerStream CreateNamedPipeServer()
    {
        Console.Error.WriteLine($"[DEBUG] CreateNamedPipeServer: PipeName={PipeName}");
        try
        {
            var server = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            Console.Error.WriteLine($"[DEBUG] NamedPipeServerStream created successfully");
            return server;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to create NamedPipeServerStream: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Handles client communication
    /// </summary>
    private static async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        Console.Error.WriteLine("[DEBUG] HandleClientAsync started");
        try
        {
            // Receive request
            Console.Error.WriteLine("[DEBUG] Calling ReceiveMessageAsync...");
            var requestJson = await ReceiveMessageAsync(pipeServer, cancellationToken).ConfigureAwait(false);
            Console.Error.WriteLine($"[DEBUG] Received request: {requestJson.Substring(0, Math.Min(100, requestJson.Length))}...");
            
            // Parse JSON-RPC request
            using var requestDoc = JsonDocument.Parse(requestJson);
            var requestRoot = requestDoc.RootElement;
            
            var name = requestRoot.GetProperty("name").GetString();
            Console.Error.WriteLine($"[DEBUG] Tool name: {name}");

            string? proxyVersion = requestRoot.TryGetProperty("proxy_version", out JsonElement proxyVersionElement)
                ? proxyVersionElement.GetString() : "Not detected";

            if (proxyVersion != MCPModuleInitializer.ServerVersion)
            {
                string output = McpServerHost.ExecuteSilentCommand("Get-MCPProxyPath");
                string proxyExePath = output[(output.LastIndexOfAny(['\r', '\n']) + 1)..];

                var versionErrorResponse =
$@"PowerShell MCP Configuration Error

ISSUE: PowerShell.MCP.Proxy version is outdated.
- PowerShell.MCP module version: {MCPModuleInitializer.ServerVersion}
- Proxy executable version: {proxyVersion}

ACTION REQUIRED: Update your MCP client configuration
- Executable path: {proxyExePath}
- JSON config example: ""PowerShell"": {{ ""command"": ""{proxyExePath.Replace("\\", "\\\\")}"" }}

Please provide how to update the MCP client configuration to the user.";

                await SendMessageAsync(pipeServer, versionErrorResponse, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Check execution state
            if (ExecutionState.Status == "busy")
            {
                // Return busy response
                var busyResponse = @"Cannot execute new pipeline while previous pipeline is running (cannot be cancelled with Ctrl+C). Options:
1. Wait for completion
2. Manually terminate console (LLM will then restart automatically)

LLM should prompt user to choose.";

                await SendMessageAsync(pipeServer, busyResponse, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Execute tool
            var result = await Task.Run(() => ExecuteTool(name!, requestRoot)).ConfigureAwait(false);
            
            // Send response
            await SendMessageAsync(pipeServer, result, cancellationToken).ConfigureAwait(false);
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
                await SendMessageAsync(pipeServer, errorJson, cancellationToken).ConfigureAwait(false);
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
        Console.Error.WriteLine("[DEBUG] Server ReceiveMessageAsync: Reading length prefix...");
        
        // Receive message length (4 bytes)
        var lengthBytes = new byte[4];
        await ReadExactAsync(pipeServer, lengthBytes, cancellationToken).ConfigureAwait(false);
        var messageLength = BitConverter.ToInt32(lengthBytes, 0);
        Console.Error.WriteLine($"[DEBUG] Server ReceiveMessageAsync: Message length = {messageLength}");

        if (messageLength <= 0)
        {
            throw new InvalidOperationException($"Invalid message length: {messageLength}");
        }

        // Receive message body
        Console.Error.WriteLine("[DEBUG] Server ReceiveMessageAsync: Reading message body...");
        var messageBytes = new byte[messageLength];
        await ReadExactAsync(pipeServer, messageBytes, cancellationToken).ConfigureAwait(false);
        Console.Error.WriteLine("[DEBUG] Server ReceiveMessageAsync: Complete");

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
        await pipeServer.WriteAsync(lengthBytes, 0, 4, cancellationToken).ConfigureAwait(false);
        
        // Send message body
        await pipeServer.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken).ConfigureAwait(false);
        await pipeServer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads exactly the specified number of bytes
    /// </summary>
    private static async Task ReadExactAsync(NamedPipeServerStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading data");
            }
            totalBytesRead += bytesRead;
        }
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
