using System.IO.Pipes;
using System.Text;

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
                var pipeName = ConsoleSessionManager.GetPipeNameForPid(pid);
                ConsoleSessionManager.Instance.RegisterConsole(pipeName, setAsActive: true);
                Console.Error.WriteLine($"[INFO] Registered console PID={pid}, pipe={pipeName}");
                
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