using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// 通知受信用のNamedPipeServer
/// </summary>
public class NotificationPipeServer //: IDisposable
{
    private const string NotificationPipeName = "PowerShell.MCP.Notifications";
    private readonly CancellationTokenSource _cancellationSource = new();
    //private Task? _serverTask;
    //private static string? _lastKnownLocation;

    // 今のところ、MCP notification をサポートしている MCP client はほとんどないようだ。
    // いったんコメントアウトしておく。
    //public async Task StartAsync()
    //{
    //    _serverTask = Task.Run(async () => await RunNotificationServerAsync(_cancellationSource.Token));
    //    await Task.Delay(100); // 起動待機
    //}

    //private async Task RunNotificationServerAsync(CancellationToken cancellationToken)
    //{
    //    while (!cancellationToken.IsCancellationRequested)
    //    {
    //        try
    //        {
    //            using var pipeServer = new NamedPipeServerStream(
    //                NotificationPipeName,
    //                PipeDirection.In,
    //                1,
    //                PipeTransmissionMode.Byte,
    //                PipeOptions.Asynchronous);

    //            await pipeServer.WaitForConnectionAsync(cancellationToken);
    //            await HandleNotificationAsync(pipeServer, cancellationToken);
    //        }
    //        catch (OperationCanceledException)
    //        {
    //            break;
    //        }
    //        catch (Exception ex)
    //        {
    //            Console.Error.WriteLine($"Notification pipe error: {ex.Message}");
    //            await Task.Delay(1000, cancellationToken);
    //        }
    //    }
    //}

    //private async Task HandleNotificationAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    //{
    //    try
    //    {
    //        var notificationJson = await ReceiveMessageAsync(pipeServer, cancellationToken);
    //        var notification = JsonSerializer.Deserialize<JsonElement>(notificationJson);
            
    //        if (notification.TryGetProperty("type", out var typeElement))
    //        {
    //            var notificationType = typeElement.GetString();
                
    //            if (notificationType == "location_changed" && 
    //                notification.TryGetProperty("new_location", out var locationElement))
    //            {
    //                var newLocation = locationElement.GetString();
    //                if (_lastKnownLocation != newLocation)
    //                {
    //                    _lastKnownLocation = newLocation;
    //                    SendMcpNotification(notification);
    //                }
    //            }
    //            else
    //            {
    //                // その他の通知（コマンド実行など）は常に送信
    //                SendMcpNotification(notification);
    //            }
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.Error.WriteLine($"Notification handling error: {ex.Message}");
    //    }
    //}

    //private void SendMcpNotification(JsonElement notificationData)
    //{
    //    var mcpNotification = new
    //    {
    //        jsonrpc = "2.0",
    //        method = "notifications/message",
    //        @params = new
    //        {
    //            level = "info",
    //            logger = "PowerShell.MCP",
    //            data = notificationData
    //        }
    //    };
        
    //    var mcpJson = JsonSerializer.Serialize(mcpNotification);
    //    Console.WriteLine(mcpJson); // MCPクライアントへpush送信
    //}

    //private async Task<string> ReceiveMessageAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    //{
    //    // 簡潔な形式: 長さプレフィックスなしでJSONを直接受信
    //    using var reader = new StreamReader(pipeServer);
    //    return await reader.ReadToEndAsync();
    //}

    //public void Dispose()
    //{
    //    _cancellationSource.Cancel();
    //    _serverTask?.Wait(1000);
    //    _cancellationSource.Dispose();
    //}
}

public class NamedPipeClient
{
    private const string PipeName = "PowerShell.MCP.Communication";
    //private const string PipeName = "PowerShell.MCP.Communication-debug";
    private const int TimeoutMs = 1000 * 10; // 接続タイムアウト

    /// <summary>
    /// Named Pipe経由でPowerShellモジュールにリクエストを送信します
    /// </summary>
    /// <param name="toolName">実行するツール名</param>
    /// <param name="arguments">ツールの引数</param>
    /// <param name="originalId">元のMCPクライアントからのID</param>
    /// <returns>PowerShellモジュールからのレスポンス</returns>
    public static async Task<string> SendRequestAsync(string toolName, JsonElement arguments, object originalId)
    {
        if (!PowerShellProcessManager.IsPowerShellProcessRunning())
        {
            throw new McpException($"The PowerShell 7 console is not running. Please guide the user to start it and import the PowerShell.MCP module.");
        }

        using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);

        // Named Pipeに接続
        try
        {
            await pipeClient.ConnectAsync(TimeoutMs);
        }
        catch (TimeoutException e)
        {
            throw new McpException($"PowerShell 7 (pwsh.exe) is running but PowerShell.MCP module is not imported.\r\n\r\nMANDATORY STEPS - DO NOT SKIP ANY:\r\n1. Explain the situation to the user\r\n2. Present exactly these two options:\r\n   - Option 1: User imports PowerShell.MCP module manually in current console\r\n   - Option 2: I will automatically start fresh console with PowerShell.MCP imported\r\n3. STOP and wait for explicit user response - DO NOT make any choice for the user\r\n4. ONLY if user explicitly chooses option 2, execute start_powershell_console\r\n5. If user chooses option 1, provide the command: Import-Module PowerShell.MCP\r\n6. DO NOT execute any PowerShell commands until user makes their choice\r\n\r\nCRITICAL: Never assume user preference or execute start_powershell_console without explicit user consent.", e);
        }

        try
        {
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
            throw new InvalidOperationException($"Invalid message length (over 10MB): {messageLength}");
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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WaitNamedPipe(string name, uint timeout);

    /// <summary>
    /// サーバ側で Named Pipe が作成されることを待機します
    /// </summary>
    /// <returns>接続可能な場合は true</returns>
    public static async Task<bool> WaitForPipeReadyAsync()
    {
        for (int i = 0; i < 30; i++)
        {
            try
            {
                // パイプの存在確認
                if (WaitNamedPipe($@"\\.\pipe\{PipeName}", 100))
                {
                    // 実際に通信テストを実行
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                    await client.ConnectAsync(1000);

                    // 簡単なテストコマンド（例：$true）を送信して応答を確認
                    // 実際の通信が可能かテスト

                    return true;
                }
            }
            catch
            {
                // 通信失敗、再試行
            }
            await Task.Delay(1000);
        }
        return false;
    }
}
