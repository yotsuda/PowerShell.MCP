namespace PowerShell.MCP.Services;

/// <summary>
/// PowerShellとの通信を管理するクラス（適応的ポーリング版）
/// </summary>
public static class PowerShellCommunication
{
    /// <summary>
    /// 結果を待機します（適応的ポーリング方式）
    /// </summary>
    public static string WaitForResult(TimeSpan timeout)
    {
        var endTime = DateTime.UtcNow.Add(timeout);
        
        // 結果をクリアしてから待機
        McpServerHost.outputFromCommand = null;
        
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
        
        return "Command execution timed out";
    }
}

/// <summary>
/// MCP通信のホストクラス（簡潔版 - 既存Named Pipe活用）
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
    /// コマンド実行通知を送信
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
