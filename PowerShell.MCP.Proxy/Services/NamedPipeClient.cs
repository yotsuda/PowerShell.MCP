using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

public class NamedPipeClient
{
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
    /// Waits until a specific Named Pipe is ready and returns standby status
    /// </summary>
    public static async Task<bool> WaitForPipeReadyAsync(string pipeName)
    {
        const int maxAttempts = 600; // Wait up to 60 seconds (100ms * 600)

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var request = "{\"name\":\"get_status\"}";
                var response = await new NamedPipeClient().SendRequestToAsync(pipeName, request);

                using var doc = JsonDocument.Parse(response);
                var status = doc.RootElement.GetProperty("status").GetString();

                if (status == "standby" || status == "completed")
                {
                    Console.Error.WriteLine($"[INFO] Named Pipe '{pipeName}' ready with status '{status}' after {attempt} attempts");
                    return true;
                }

                // busy - wait and retry
                await Task.Delay(100);
            }
            catch (TimeoutException)
            {
                // Expected during startup - pipe not yet available
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARNING] Named Pipe connection attempt {attempt} to '{pipeName}' failed: {ex.Message}");
                await Task.Delay(100);
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
