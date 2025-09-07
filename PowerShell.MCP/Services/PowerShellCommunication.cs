namespace PowerShell.MCP.Services;

/// <summary>
/// PowerShellとの通信を管理するクラス（適応的ポーリング版）
/// PowerShell側はメインスレッドでTimerベース、C#側は効率的なポーリング
/// </summary>
public static class PowerShellCommunication
{
    /// <summary>
    /// 結果を待機します（適応的ポーリング方式）
    /// </summary>
    /// <param name="timeout">タイムアウト時間</param>
    /// <returns>実行結果</returns>
    public static string WaitForResult(TimeSpan timeout)
    {
        var endTime = DateTime.UtcNow.Add(timeout);
        
        // 結果をクリアしてから待機
        McpServerHost.outputFromCommand = null;
        
        // 適応的ポーリング間隔
        const int initialIntervalMs = 10;    // 最初は高頻度
        const int maxIntervalMs = 100;       // 最大間隔
        const int escalationTimeMs = 1000;   // 1秒後に間隔を増加
        
        var currentInterval = initialIntervalMs;
        var startTime = DateTime.UtcNow;
        
        while (DateTime.UtcNow < endTime)
        {
            var currentResult = McpServerHost.outputFromCommand;
            if (!string.IsNullOrEmpty(currentResult))
            {
                return currentResult;
            }
            
            // 適応的間隔調整：最初は高頻度、時間経過で低頻度に
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
/// 元のMCPPollingEngine.ps1との完全互換クラス
/// PowerShell側の制約により、ここではイベント機構は使用不可
/// </summary>
public static class McpServerHost
{
    public static volatile string? executeCommand;
    public static volatile string? insertCommand;
    public static volatile string? executeCommandSilent;
    public static volatile string? outputFromCommand;
}
