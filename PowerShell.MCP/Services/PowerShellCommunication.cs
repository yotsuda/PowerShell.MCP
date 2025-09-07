namespace PowerShell.MCP.Services;

/// <summary>
/// PowerShellとの通信を管理するクラス（シンプル版）
/// 元の MCPPollingEngine.ps1 と完全互換
/// </summary>
public static class PowerShellCommunication
{
    /// <summary>
    /// 結果を待機します（ポーリング方式）
    /// </summary>
    /// <param name="timeout">タイムアウト時間</param>
    /// <returns>実行結果</returns>
    public static string WaitForResult(TimeSpan timeout)
    {
        var endTime = DateTime.UtcNow.Add(timeout);
        
        // 結果をクリアしてから待機
        McpServerHost.outputFromCommand = null;
        
        while (DateTime.UtcNow < endTime)
        {
            var currentResult = McpServerHost.outputFromCommand;
            if (!string.IsNullOrEmpty(currentResult))
            {
                return currentResult;
            }
            
            Thread.Sleep(100); // 100ms待機
        }
        
        return "Command execution timed out";
    }
}

/// <summary>
/// 元のMCPPollingEngine.ps1との完全互換クラス
/// </summary>
public static class McpServerHost
{
    public static volatile string? executeCommand;
    public static volatile string? insertCommand;
    public static volatile string? executeCommandSilent;
    public static volatile string? outputFromCommand;
}
