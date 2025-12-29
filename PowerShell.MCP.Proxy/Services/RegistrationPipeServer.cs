using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PowerShell.MCP.Proxy.Models;
namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Named Pipe server that receives registration requests from PS modules.
/// When a PS module is imported, it sends its PID to this server.
/// </summary>
public class RegistrationPipeServer : IDisposable
{
    public const string PipeName = "PowerShell.MCP.Registration";
    
    private readonly CancellationTokenSource _cts = new();
    private Task? _serverTask;
    private bool _disposed;
    
    /// <summary>
    /// Starts the registration server in background
    /// </summary>
    public void Start()
    {
        _serverTask = Task.Run(() => RunServerAsync(_cts.Token));
        Console.Error.WriteLine("[INFO] RegistrationPipeServer started");
    }
    
    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                
                await pipeServer.WaitForConnectionAsync(cancellationToken);
                
                await HandleRegistrationAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] RegistrationPipeServer error: {ex.Message}");
                
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
    
    private async Task HandleRegistrationAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        try
        {
            // Read message length (4 bytes)
            var lengthBuffer = new byte[4];
            await ReadExactAsync(pipeServer, lengthBuffer, cancellationToken);
            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
            
            if (messageLength <= 0 || messageLength > 1024)
            {
                Console.Error.WriteLine($"[WARNING] Invalid registration message length: {messageLength}");
                return;
            }
            
            // Read message body
            var messageBuffer = new byte[messageLength];
            await ReadExactAsync(pipeServer, messageBuffer, cancellationToken);
            var message = Encoding.UTF8.GetString(messageBuffer);
            
            // Parse PID from message (format: "REGISTER:<PID>")
            if (message.StartsWith("REGISTER:") && int.TryParse(message.Substring(9), out var pid))
            {
                var sessionManager = ConsoleSessionManager.Instance;
                
                // Check if any existing console is Ready (not Busy)
                if (await HasReadyConsoleAsync(sessionManager, cancellationToken))
                {
                    // Send rejection
                    var rejectBytes = Encoding.UTF8.GetBytes("REJECT");
                    var rejectLengthBytes = BitConverter.GetBytes(rejectBytes.Length);
                    await pipeServer.WriteAsync(rejectLengthBytes, cancellationToken);
                    await pipeServer.WriteAsync(rejectBytes, cancellationToken);
                    await pipeServer.FlushAsync(cancellationToken);
                    return;
                }
                
                // No Ready console, register this one and set as active
                var pipeName = ConsoleSessionManager.GetPipeNameForPid(pid);
                // Since HasReadyConsoleAsync returned false, all existing consoles are busy or dead
                // So this new console should be the active one
                sessionManager.RegisterConsole(pipeName, setAsActive: true);
                
                // Send acknowledgment
                var ackBytes = Encoding.UTF8.GetBytes("OK");
                var ackLengthBytes = BitConverter.GetBytes(ackBytes.Length);
                await pipeServer.WriteAsync(ackLengthBytes, cancellationToken);
                await pipeServer.WriteAsync(ackBytes, cancellationToken);
                await pipeServer.FlushAsync(cancellationToken);
            }
            else
            {
                Console.Error.WriteLine($"[WARNING] Invalid registration message: {message}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] HandleRegistrationAsync error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Checks if any registered console is Ready (not Busy)
    /// </summary>
    private static async Task<bool> HasReadyConsoleAsync(ConsoleSessionManager sessionManager, CancellationToken cancellationToken)
    {
        var allPipes = sessionManager.GetAllPipeNames();
        
        foreach (var pipeName in allPipes)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                await client.ConnectAsync(500, cancellationToken); // 500ms timeout
                
                // Send get_status request with proxy_version (lightweight, doesn't use main runspace)
                var requestParams = new GetStatusParams();
                var request = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.GetStatusParams);
                var requestBytes = Encoding.UTF8.GetBytes(request);
                var lengthBytes = BitConverter.GetBytes(requestBytes.Length);
                
                await client.WriteAsync(lengthBytes, cancellationToken);
                await client.WriteAsync(requestBytes, cancellationToken);
                await client.FlushAsync(cancellationToken);
                
                // Read response length
                var responseLengthBuffer = new byte[4];
                await ReadExactAsync(client, responseLengthBuffer, cancellationToken);
                var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
                
                if (responseLength > 0 && responseLength < 1000)
                {
                    var responseBuffer = new byte[responseLength];
                    await ReadExactAsync(client, responseBuffer, cancellationToken);
                    var response = Encoding.UTF8.GetString(responseBuffer);
                    
                    // If NOT Busy, this is a Ready console
                    if (!response.Contains("| Status: Busy |"))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Console might be dead, continue checking others
            }
        }
        
        return false;
    }
    
    private static async Task ReadExactAsync(NamedPipeClientStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead, buffer.Length - totalBytesRead), cancellationToken);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading data");
            }
            totalBytesRead += bytesRead;
        }
    }
    
    private static async Task ReadExactAsync(NamedPipeServerStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int totalBytesRead = 0;
        while (totalBytesRead < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(totalBytesRead, buffer.Length - totalBytesRead), cancellationToken);
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
            _cts.Cancel();
            _cts.Dispose();
            _disposed = true;
        }
    }
}