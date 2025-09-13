namespace PowerShell.MCP.Services;

/// <summary>
/// PowerShellとの通信を管理するクラス
/// </summary>
public static class PowerShellCommunication
{
    /// <summary>
    /// 結果を待機します
    /// </summary>
    public static string WaitForResult()
    {
        var endTime = DateTime.UtcNow.AddSeconds(4 * 60 + 50);
        
        // 結果をクリアしてから待機
        McpServerHost.outputFromCommand = null;
        
        // 実行状態をBusyに設定
        ExecutionState.SetBusy();
        
        try
        {
            const int initialIntervalMs = 10;
            const int maxIntervalMs = 100;
            const int escalationTimeMs = 1000;
            
            var currentInterval = initialIntervalMs;
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow < endTime)
            {
                var currentResult = McpServerHost.outputFromCommand;
                if (currentResult is not null)
                {
                    return currentResult;
                }
                
                var elapsed = DateTime.UtcNow - startTime;
                if (elapsed.TotalMilliseconds > escalationTimeMs && currentInterval < maxIntervalMs)
                {
                    currentInterval = Math.Min(currentInterval * 2, maxIntervalMs);
                }
                
                Thread.Sleep(currentInterval);
            }
            
            return "Command execution timed out (4 minutes 50 seconds). Your command is still running. Consider restarting the PowerShell console.";
        }
        finally
        {
            // 実行完了後は必ずIdleに戻す
            ExecutionState.SetIdle();
        }
    }
}

/// <summary>
/// MCP通信のホストクラス
/// </summary>
public static class McpServerHost
{
    // 既存の通信プロパティ
    public static volatile string? executeCommand;
    public static volatile string? insertCommand;
    public static volatile string? executeCommandSilent;
    public static volatile string? outputFromCommand;
    //public static volatile string? pendingNotification;

    /// <summary>
    /// 位置変更通知を送信
    /// </summary>
    //public static void SendLocationChanged(string oldLocation, string newLocation)
    //{
    //    var notificationData = new
    //    {
    //        type = "location_changed",
    //        old_location = oldLocation,
    //        new_location = newLocation,
    //        timestamp = DateTime.UtcNow.ToString("O")
    //    };

    //    // 既存のpendingNotificationシステムを活用
    //    MCPProvider.SendNotification(notificationData);
    //}

    /// <summary>
    /// コマンド実行
    /// </summary>
    public static string ExecuteCommand(string command, bool executeImmediately = true)
    {
        try
        {
            // 結果をクリア
            outputFromCommand = null;

            if (executeImmediately)
            {
                executeCommand = command;
            }
            else
            {
                insertCommand = command;
            }

            // 状態管理付きで結果を待機
            return PowerShellCommunication.WaitForResult();
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }
    
    /// <summary>
    /// サイレント実行
    /// </summary>
    public static string ExecuteSilentCommand(string command)
    {
        try
        {
            // 結果をクリア
            outputFromCommand = null;
            executeCommandSilent = command;

            // WaitForResult()を使用して状態管理も含めて処理
            return PowerShellCommunication.WaitForResult();
        }
        catch (Exception ex)
        {
            return $"Error executing silent command: {ex.Message}";
        }
    }

    /// </summary>
    //public static void SendCommandExecuted(string command, string location, int? exitCode = null, long durationMs = 0)
    //{
    //    var notificationData = new
    //    {
    //        type = "command_executed",
    //        command = command,
    //        location = location,
    //        exit_code = exitCode,
    //        duration_ms = durationMs,
    //        timestamp = DateTime.UtcNow.ToString("O")
    //    };

    //    // 既存の pendingNotification システムを活用
    //    MCPProvider.SendNotification(notificationData);
    //}
}
