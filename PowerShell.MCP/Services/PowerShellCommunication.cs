namespace PowerShell.MCP.Services;

/// <summary>
/// PowerShellとの通信を管理するクラス
/// </summary>
public static class PowerShellCommunication
{
    private static readonly ManualResetEvent _resultReadyEvent = new(false);
    private static string? _currentResult = null;

    /// <summary>
    /// 結果が準備完了したことを通知するメソッド
    /// PowerShellスクリプトから明示的に呼び出される
    /// </summary>
    public static void NotifyResultReady(string result)
    {
        _currentResult = result;
        _resultReadyEvent.Set();
    }

    /// <summary>
    /// 結果を待機します（ブロッキング方式）
    /// </summary>
    public static string WaitForResult()
    {
        var endTime = DateTime.UtcNow.AddSeconds(4 * 60 + 50);
        
        // 前回の結果をクリア
        _currentResult = null;
        _resultReadyEvent.Reset();
        
        // 実行状態をBusyに設定
        ExecutionState.SetBusy();
        
        try
        {
            var remainingTime = endTime - DateTime.UtcNow;
            var timeoutMs = (int)Math.Max(0, remainingTime.TotalMilliseconds);
            
            // ManualResetEventでブロッキング待機
            bool signaled = _resultReadyEvent.WaitOne(timeoutMs);
            
            if (signaled)
            {
                return _currentResult ?? "No result available";
            }
            else
            {
                return "Command execution timed out (4 minutes 50 seconds). Your command is still running. Consider restarting the PowerShell console.";
            }
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
