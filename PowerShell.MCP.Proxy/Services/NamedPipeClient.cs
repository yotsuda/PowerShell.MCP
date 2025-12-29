using System.IO.Pipes;
using System.Text;

namespace PowerShell.MCP.Proxy.Services;

public class NamedPipeClient
{
    private readonly ConsoleSessionManager _sessionManager = ConsoleSessionManager.Instance;

    public async Task<string> SendRequestAsync(string arguments)
    {
        try
        {
            // Cleanup dead consoles and discover existing ones
            _sessionManager.CleanupDeadConsoles();
            _sessionManager.DiscoverExistingConsole();

            // Check if any PowerShell process is running
            if (!PowerShellProcessManager.IsPowerShellProcessRunning())
            {
                return "The PowerShell 7 console is not running. Use start_powershell_console tool to start it first.";
            }

            // Get active pipe name
            var pipeName = _sessionManager.ActivePipeName;
            
            if (pipeName == null)
            {
                // No registered console, try default pipe (user-imported module)
                if (_sessionManager.CanConnect(ConsoleSessionManager.DefaultPipeName))
                {
                    _sessionManager.RegisterConsole(ConsoleSessionManager.DefaultPipeName, setAsActive: true);
                    pipeName = ConsoleSessionManager.DefaultPipeName;
                }
                else
                {
                    return "PowerShell.MCP module is not imported in existing pwsh.";
                }
            }
            
            using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);

            // Attempt Named Pipe connection
            try
            {
                await pipeClient.ConnectAsync(1000 * 3); // 3 second timeout
            }
            catch (TimeoutException)
            {
                // Pipe was registered but now unavailable
                _sessionManager.UnregisterConsole(pipeName);
                return "PowerShell.MCP module is not imported in existing pwsh.";
            }
 
            // Convert JSON message to UTF-8 bytes
            var messageBytes = Encoding.UTF8.GetBytes(arguments);
            
            // Create 4-byte Little Endian message length
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);
            
            // Send message length prefix + JSON message body
            await pipeClient.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length);

            // Receive response: proper length prefix handling
            var response = await ReceiveMessageAsync(pipeClient);
            return response;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("[WARNING] PowerShell.MCP module connection timeout - module may not be running");
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Named Pipe communication failed: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Sends request to a specific Named Pipe
    /// </summary>
    public async Task<string> SendRequestToAsync(string pipeName, string arguments)
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);

            try
            {
                await pipeClient.ConnectAsync(1000 * 3);
            }
            catch (TimeoutException)
            {
                _sessionManager.UnregisterConsole(pipeName);
                return string.Empty;
            }

            var messageBytes = Encoding.UTF8.GetBytes(arguments);
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);
            
            await pipeClient.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length);

            return await ReceiveMessageAsync(pipeClient);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Named Pipe communication to '{pipeName}' failed: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<string> ReceiveMessageAsync(NamedPipeClientStream pipeClient)
    {
        // 1. Read message length (4 bytes) reliably
        var lengthBuffer = new byte[4];
        await ReadExactAsync(pipeClient, lengthBuffer, 4);
        
        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        
        // 2. Validate message length
        if (messageLength < 0)
        {
            throw new InvalidOperationException($"Invalid message length received: {messageLength}");
        }
        
        // 3. Read message body reliably
        var messageBuffer = new byte[messageLength];
        await ReadExactAsync(pipeClient, messageBuffer, messageLength);
        
        // 4. UTF-8 decode
        return Encoding.UTF8.GetString(messageBuffer);
    }

    private async Task ReadExactAsync(NamedPipeClientStream pipeClient, byte[] buffer, int count)
    {
        int totalBytesRead = 0;
        
        while (totalBytesRead < count)
        {
            var bytesRead = await pipeClient.ReadAsync(buffer, totalBytesRead, count - totalBytesRead);
            
            if (bytesRead == 0)
            {
                throw new InvalidOperationException($"Connection closed unexpectedly. Expected {count} bytes, got {totalBytesRead}");
            }
            
            totalBytesRead += bytesRead;
        }
    }

    /// <summary>
    /// Waits until a specific Named Pipe is ready
    /// </summary>
    public static async Task<bool> WaitForPipeReadyAsync(string pipeName)
    {
        const int maxAttempts = 80; // Wait up to 40 seconds
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var testClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                await testClient.ConnectAsync(500); // 500ms timeout
                Console.Error.WriteLine($"[INFO] Named Pipe '{pipeName}' ready after {attempt} attempts");
                // Give the server time to prepare for the next connection
                await Task.Delay(500);
                return true;
            }
            catch (TimeoutException)
            {
                // Expected during startup - waiting for Named Pipe
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARNING] Named Pipe connection attempt {attempt} to '{pipeName}' failed: {ex.Message}");
            }
        }
        
        Console.Error.WriteLine($"[WARNING] Named Pipe '{pipeName}' not ready after maximum attempts");
        return false;
    }

    /// <summary>
    /// Waits until default Named Pipe is ready (for backward compatibility)
    /// </summary>
    public static Task<bool> WaitForPipeReadyAsync() => WaitForPipeReadyAsync(ConsoleSessionManager.DefaultPipeName);
}
