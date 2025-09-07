using System.Diagnostics;

namespace PowerShell.MCP.Proxy.Services;

public class PowerShellProcessManager
{
    private const string PowerShellExecutableName = "pwsh";

    /// <summary>
    /// PowerShellプロセスが実行中かどうかをチェックします
    /// </summary>
    /// <returns>PowerShellプロセスが見つかった場合は true</returns>
    public static bool IsPowerShellProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(PowerShellExecutableName);
            var found = processes.Length > 0;
            
            // プロセスオブジェクトのリソースを解放
            foreach (var process in processes)
            {
                process.Dispose();
            }
            
            return found;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error checking PowerShell process: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// PowerShell.MCPモジュールをインポートした状態で PowerShell プロセスを起動します（非同期版）
    /// </summary>
    /// <param name="pipeClient">Named pipe 通信用のクライアント（ヘルスチェックに使用）</param>
    /// <returns>起動に成功した場合は true</returns>
    public static async Task<bool> StartPowerShellWithModuleAsync(NamedPipeClient? pipeClient = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = "-NoExit -Command \"Import-Module PowerShell.MCP\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            using var process = Process.Start(startInfo);
            
            if (process == null)
            {
                Console.Error.WriteLine("Failed to start PowerShell process");
                return false;
            }

            Console.Error.WriteLine($"PowerShell process started (PID: {process.Id})");
            
            // Named pipeでの接続確認が最も確実な方法
            if (pipeClient != null)
            {
                // モジュールが完全に読み込まれてNamed Pipeがリスンするまで待機
                return await WaitForModuleReadyAsync(pipeClient);
            }
            
            // pipeClientがない場合は基本的な待機のみ
            await Task.Delay(3000);
            return !process.HasExited;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error starting PowerShell process: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 既存の同期版メソッド（後方互換性のため残す）
    /// </summary>
    /// <returns>起動に成功した場合はtrue</returns>
    //public bool StartPowerShellWithModule()
    //{
    //    return StartPowerShellWithModuleAsync().GetAwaiter().GetResult();
    //}

    /// <summary>
    /// PowerShell.MCPモジュールがnamed pipeでリッスンするまで待機します
    /// </summary>
    /// <param name="pipeClient">Named pipe通信用のクライアント</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>モジュールが準備完了した場合はtrue</returns>
    private static async Task<bool> WaitForModuleReadyAsync(NamedPipeClient pipeClient, int timeoutMs = 15000)
    {
        const int checkIntervalMs = 500;
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        
        Console.Error.WriteLine("Waiting for PowerShell.MCP module to be ready...");
        
        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var isReady = await pipeClient.TestConnectionAsync();
                if (isReady)
                {
                    var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    Console.Error.WriteLine($"PowerShell.MCP module is ready (took {elapsedMs:F0}ms)");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // ログに記録するが、再試行を続ける
                Console.Error.WriteLine($"Module health check failed (retrying): {ex.Message}");
            }
            
            await Task.Delay(checkIntervalMs);
        }
        
        Console.Error.WriteLine($"Timeout waiting for PowerShell.MCP module (timeout: {timeoutMs}ms)");
        return false;
    }
}

/// <summary>
/// プロセス情報を格納するデータクラス
/// </summary>
public class ProcessInfo
{
    public int Id { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public bool HasExited { get; set; }
}
