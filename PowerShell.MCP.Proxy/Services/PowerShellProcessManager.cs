using System.Diagnostics;

namespace PowerShell.MCP.Proxy.Services;

public class PowerShellProcessManager
{
    private const string PowerShellExecutableName = "pwsh";
    private const int ProcessCheckTimeoutMs = 5000;

    /// <summary>
    /// PowerShellプロセスが実行中かどうかをチェックします
    /// </summary>
    /// <returns>PowerShellプロセスが見つかった場合はtrue</returns>
    public bool IsPowerShellProcessRunning()
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
    /// PowerShell.MCPモジュールをインポートした状態でPowerShellプロセスを起動します
    /// </summary>
    /// <returns>起動に成功した場合はtrue</returns>
    public bool StartPowerShellWithModule()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh.exe",
                Arguments = "-NoExit -Command \"Import-Module PowerShell.MCP\"",
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            using var process = Process.Start(startInfo);
            
            if (process == null)
            {
                Console.Error.WriteLine("Failed to start PowerShell process");
                return false;
            }

            // プロセスが正常に起動したかを短時間チェック
            if (process.HasExited)
            {
                Console.Error.WriteLine($"PowerShell process exited immediately with code: {process.ExitCode}");
                return false;
            }

            Console.Error.WriteLine($"PowerShell process started successfully (PID: {process.Id})");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error starting PowerShell process: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 指定したPIDのプロセスが実行中かどうかをチェックします（将来の最適化用）
    /// </summary>
    /// <param name="processId">チェックするプロセスID</param>
    /// <returns>プロセスが実行中の場合はtrue</returns>
    public bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // プロセスが見つからない
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error checking process {processId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// PowerShellプロセスのリストを取得します（デバッグ用）
    /// </summary>
    /// <returns>実行中のPowerShellプロセスの情報</returns>
    public List<ProcessInfo> GetPowerShellProcesses()
    {
        var result = new List<ProcessInfo>();
        
        try
        {
            var processes = Process.GetProcessesByName(PowerShellExecutableName);
            
            foreach (var process in processes)
            {
                try
                {
                    result.Add(new ProcessInfo
                    {
                        Id = process.Id,
                        ProcessName = process.ProcessName,
                        StartTime = process.StartTime,
                        HasExited = process.HasExited
                    });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error getting info for process {process.Id}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error getting PowerShell processes: {ex.Message}");
        }
        
        return result;
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
