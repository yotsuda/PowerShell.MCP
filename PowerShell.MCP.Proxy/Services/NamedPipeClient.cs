using System.IO.Pipes;
using System.Text;

namespace PowerShell.MCP.Proxy.Services;

public class NamedPipeClient
{
    private const string PipeName = "PowerShell.MCP.Communication";

    public async Task<string> SendRequestAsync(string arguments)
    {
        Console.Error.WriteLine($"[DEBUG] NamedPipeClient.SendRequestAsync called");
        Console.Error.WriteLine($"[DEBUG] Arguments length: {arguments.Length}");
        
        try
        {
            if (!PowerShellProcessManager.IsPowerShellProcessRunning())
            {
                Console.Error.WriteLine("[DEBUG] PowerShell process not running");
                return $"The PowerShell 7 console is not running. Use start_powershell_console tool to start it first.";
            }
            
            Console.Error.WriteLine($"[DEBUG] Creating NamedPipeClientStream for pipe: {PipeName}");
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);

            // Attempt Named Pipe connection
            try
            {
                Console.Error.WriteLine("[DEBUG] Connecting to Named Pipe (3s timeout)...");
                await pipeClient.ConnectAsync(1000 * 3).ConfigureAwait(false); // 3 second timeout
                Console.Error.WriteLine("[DEBUG] Connected to Named Pipe successfully");
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine("[DEBUG] Named Pipe connection timeout");
                return $"PowerShell 7 (pwsh) is running but PowerShell.MCP module is not imported.\r\n\r\nMANDATORY STEPS - DO NOT SKIP ANY:\r\n1. Explain the situation to the user\r\n2. Present exactly these two options:\r\n   - Option 1: User imports PowerShell.MCP module manually in current console\r\n   - Option 2: I will automatically start fresh console with PowerShell.MCP imported\r\n3. STOP and wait for explicit user response - DO NOT make any choice for the user\r\n4. ONLY if user explicitly chooses option 2, execute start_powershell_console\r\n5. If user chooses option 1, provide the command: Import-Module PowerShell.MCP\r\n6. DO NOT execute any PowerShell commands until user makes their choice\r\n\r\nCRITICAL: Never assume user preference or execute start_powershell_console without explicit user consent.";
            }
 
            // Convert JSON message to UTF-8 bytes
            var messageBytes = Encoding.UTF8.GetBytes(arguments);
            Console.Error.WriteLine($"[DEBUG] Message bytes length: {messageBytes.Length}");
            
            // Create 4-byte Little Endian message length
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);
            Console.Error.WriteLine($"[DEBUG] Sending length prefix: {messageBytes.Length}");
            
            // Send message length prefix + JSON message body
            await pipeClient.WriteAsync(lengthBytes, 0, lengthBytes.Length).ConfigureAwait(false);
            await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length).ConfigureAwait(false);
            await pipeClient.FlushAsync().ConfigureAwait(false);
            Console.Error.WriteLine("[DEBUG] Message sent, waiting for response...");

            // Receive response: proper length prefix handling
            var response = await ReceiveMessageAsync(pipeClient).ConfigureAwait(false);
            Console.Error.WriteLine($"[DEBUG] Response received, length: {response.Length}");
            return response;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("[WARNING] PowerShell.MCP module connection timeout - module may not be running");
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Named Pipe communication failed: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
            return string.Empty;
        }
    }

    private async Task<string> ReceiveMessageAsync(NamedPipeClientStream pipeClient)
    {
        Console.Error.WriteLine("[DEBUG] ReceiveMessageAsync: Reading length prefix...");
        
        // 1. Read message length (4 bytes) reliably
        var lengthBuffer = new byte[4];
        await ReadExactAsync(pipeClient, lengthBuffer, 4).ConfigureAwait(false);
        
        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        Console.Error.WriteLine($"[DEBUG] ReceiveMessageAsync: Message length = {messageLength}");
        
        // 2. Validate message length
        if (messageLength < 0)
        {
            throw new InvalidOperationException($"Invalid message length received: {messageLength}");
        }
        
        // 3. Read message body reliably
        Console.Error.WriteLine($"[DEBUG] ReceiveMessageAsync: Reading message body...");
        var messageBuffer = new byte[messageLength];
        await ReadExactAsync(pipeClient, messageBuffer, messageLength).ConfigureAwait(false);
        
        // 4. UTF-8 decode
        var result = Encoding.UTF8.GetString(messageBuffer);
        Console.Error.WriteLine($"[DEBUG] ReceiveMessageAsync: Complete");
        return result;
    }

    private async Task ReadExactAsync(NamedPipeClientStream pipeClient, byte[] buffer, int count)
    {
        int totalBytesRead = 0;
        
        while (totalBytesRead < count)
        {
            var bytesRead = await pipeClient.ReadAsync(buffer, totalBytesRead, count - totalBytesRead).ConfigureAwait(false);
            
            if (bytesRead == 0)
            {
                throw new InvalidOperationException($"Connection closed unexpectedly. Expected {count} bytes, got {totalBytesRead}");
            }
            
            totalBytesRead += bytesRead;
        }
    }
    /// <summary>
    /// Waits until Named Pipe is ready
    /// </summary>
    /// <returns>true if pipe is ready</returns>
    public static async Task<bool> WaitForPipeReadyAsync()
    {
        const int maxAttempts = 80; // Wait up to 40 seconds
        //const int delayMs = 1000;
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var testClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                await testClient.ConnectAsync(500).ConfigureAwait(false); // 500ms timeout
                Console.Error.WriteLine($"[INFO] Named Pipe ready after {attempt} attempts");
                // Give the server time to prepare for the next connection
                await Task.Delay(500).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                // Expected during startup - waiting for Named Pipe
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARNING] Named Pipe connection attempt {attempt} failed: {ex.Message}");
            }
        }
        
        Console.Error.WriteLine("[WARNING] Named Pipe not ready after maximum attempts");
        return false;
    }
}
