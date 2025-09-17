using System.IO.Pipes;
using System.Text;

namespace PowerShell.MCP.Proxy.Services;

public class NamedPipeClient
{
    private const string PipeName = "PowerShell.MCP.Communication";

    public async Task<string> SendRequestAsync(string arguments)
    {
        try
        {
            if (!PowerShellProcessManager.IsPowerShellProcessRunning())
            {
                return $"The PowerShell 7 console is not running. Use start_powershell_console tool to start it first.";
            }
            
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);

            // Named Pipe への接続を試行
            try
            {
                await pipeClient.ConnectAsync(1000 * 3); // 3秒でタイムアウト
            }
            catch (TimeoutException)
            {
                return $"PowerShell 7 (pwsh.exe) is running but PowerShell.MCP module is not imported.\r\n\r\nMANDATORY STEPS - DO NOT SKIP ANY:\r\n1. Explain the situation to the user\r\n2. Present exactly these two options:\r\n   - Option 1: User imports PowerShell.MCP module manually in current console\r\n   - Option 2: I will automatically start fresh console with PowerShell.MCP imported\r\n3. STOP and wait for explicit user response - DO NOT make any choice for the user\r\n4. ONLY if user explicitly chooses option 2, execute start_powershell_console\r\n5. If user chooses option 1, provide the command: Import-Module PowerShell.MCP\r\n6. DO NOT execute any PowerShell commands until user makes their choice\r\n\r\nCRITICAL: Never assume user preference or execute start_powershell_console without explicit user consent.";
            }
 
            // JSONメッセージをUTF-8バイトに変換
            var messageBytes = Encoding.UTF8.GetBytes(arguments);
            
            // メッセージ長をLittle Endianで4バイト作成
            var lengthBytes = BitConverter.GetBytes(messageBytes.Length);
            
            // メッセージ長プレフィックス + JSONメッセージ本体を送信
            await pipeClient.WriteAsync(lengthBytes, 0, lengthBytes.Length);
            await pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length);

            // レスポンス受信: 正しい長さプレフィックス処理
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

    private async Task<string> ReceiveMessageAsync(NamedPipeClientStream pipeClient)
    {
        // 1. メッセージ長（4バイト）を確実に読み取り
        var lengthBuffer = new byte[4];
        await ReadExactAsync(pipeClient, lengthBuffer, 4);
        
        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
        
        // 2. メッセージ長の妥当性チェック
        if (messageLength < 0)
        {
            throw new InvalidOperationException($"Invalid message length received: {messageLength}");
        }
        
        // 3. メッセージ本体を確実に読み取り
        var messageBuffer = new byte[messageLength];
        await ReadExactAsync(pipeClient, messageBuffer, messageLength);
        
        // 4. UTF-8デコード
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
    /// Named Pipe が準備完了になるまで待機します
    /// </summary>
    /// <returns>パイプが準備できた場合は true</returns>
    public static async Task<bool> WaitForPipeReadyAsync()
    {
        const int maxAttempts = 80; // 最大で40秒間待機する
        const int delayMs = 1000;
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var testClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                await testClient.ConnectAsync(500); // 500ms timeout
                Console.Error.WriteLine($"[INFO] Named Pipe ready after {attempt} attempts");
                return true;
            }
            catch (TimeoutException)
            {
                Console.Error.WriteLine($"[DEBUG] Waiting for Named Pipe... attempt {attempt}/{maxAttempts}");
                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DEBUG] Named Pipe test failed (attempt {attempt}): {ex.Message}");
                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs);
                }
            }
        }
        
        Console.Error.WriteLine("[WARNING] Named Pipe not ready after maximum attempts");
        return false;
    }
}
