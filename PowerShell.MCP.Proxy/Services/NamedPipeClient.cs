using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

public class NamedPipeClient
{
    private const string PipeName = "PowerShell.MCP.Communication";
    //private const string PipeName = "PowerShell.MCP.Communication-debug";
    private const int TimeoutMs = 5000; // 接続タイムアウト（5秒）

    /// <summary>
    /// Named Pipe経由でPowerShellモジュールにリクエストを送信します
    /// </summary>
    /// <param name="toolName">実行するツール名</param>
    /// <param name="arguments">ツールの引数</param>
    /// <param name="originalId">元のMCPクライアントからのID</param>
    /// <returns>PowerShellモジュールからのレスポンス</returns>
    public static async Task<string> SendRequestAsync(string toolName, JsonElement arguments, object originalId)
    {
        using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);

        try
        {
            // Named Pipeに接続
            await pipeClient.ConnectAsync(TimeoutMs);
            
            // JSON-RPCリクエストを構築（元のIDを保持）
            var request = new
            {
                jsonrpc = "2.0",
                id = originalId, // オリジナルIDを使用
                method = toolName,
                parameters = arguments
            };

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // リクエストを送信
            await SendMessageAsync(pipeClient, requestJson);
            
            // レスポンスを受信
            var responseJson = await ReceiveMessageAsync(pipeClient);
            
            // JSON-RPCレスポンスを解析
            using var responseDoc = JsonDocument.Parse(responseJson);
            var responseRoot = responseDoc.RootElement;

            if (responseRoot.TryGetProperty("error", out var errorElement))
            {
                var errorMessage = errorElement.TryGetProperty("message", out var msgElement) 
                    ? msgElement.GetString() 
                    : "Unknown error";
                throw new InvalidOperationException($"PowerShell module error: {errorMessage}");
            }

            if (responseRoot.TryGetProperty("result", out var resultElement))
            {
                return resultElement.GetString() ?? string.Empty;
            }

            throw new InvalidOperationException("Invalid response format from PowerShell module");
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Failed to connect to PowerShell module via Named Pipe '{PipeName}' within {TimeoutMs}ms. Please ensure PowerShell.MCP module is imported.");
        }
        catch (IOException ex)
        {
            throw new IOException($"Named Pipe communication error: {ex.Message}. Please verify PowerShell.MCP module is running.", ex);
        }
    }

    /// <summary>
    /// Named Pipeにメッセージを送信します
    /// </summary>
    private static async Task SendMessageAsync(NamedPipeClientStream pipeClient, string message)
    {
        var messageBytes = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(messageBytes.Length);

        // メッセージの長さを先に送信（4バイト）
        await pipeClient.WriteAsync(lengthBytes, 0, 4);
        
        // メッセージ本体を送信
        await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length);
        await pipeClient.FlushAsync();
    }

    /// <summary>
    /// Named Pipeからメッセージを受信します
    /// </summary>
    private static async Task<string> ReceiveMessageAsync(NamedPipeClientStream pipeClient)
    {
        // メッセージの長さを受信（4バイト）
        var lengthBytes = new byte[4];
        var totalBytesRead = 0;
        
        while (totalBytesRead < 4)
        {
            var bytesRead = await pipeClient.ReadAsync(lengthBytes, totalBytesRead, 4 - totalBytesRead);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading message length");
            }
            totalBytesRead += bytesRead;
        }

        var messageLength = BitConverter.ToInt32(lengthBytes, 0);
        
        if (messageLength <= 0 || messageLength > 10 * 1024 * 1024) // 10MB上限
        {
            throw new InvalidOperationException($"Invalid message length: {messageLength}");
        }

        // メッセージ本体を受信
        var messageBytes = new byte[messageLength];
        totalBytesRead = 0;
        
        while (totalBytesRead < messageLength)
        {
            var bytesRead = await pipeClient.ReadAsync(messageBytes, totalBytesRead, messageLength - totalBytesRead);
            if (bytesRead == 0)
            {
                throw new IOException("Connection closed while reading message body");
            }
            totalBytesRead += bytesRead;
        }

        return Encoding.UTF8.GetString(messageBytes);
    }

    /// <summary>
    /// Named Pipeの接続テストを行います
    /// </summary>
    /// <returns>接続可能な場合はtrue</returns>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            await pipeClient.ConnectAsync(1000); // 1秒でテスト
            return pipeClient.IsConnected;
        }
        catch
        {
            return false;
        }
    }
}
