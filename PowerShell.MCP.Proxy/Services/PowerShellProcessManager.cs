using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
    /// PowerShell.MCPモジュールをインポートした状態で PowerShell プロセスを起動します
    /// </summary>
    /// <param name="pipeClient">Named pipe 通信用のクライアント（ヘルスチェックに使用）</param>
    /// <returns>起動に成功した場合は true</returns>
    public static async Task<bool> StartPowerShellWithModuleAsync()
    {
        PwshNative.LaunchPwshStrict();
        return await NamedPipeClient.WaitForPipeReadyAsync();
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

public static class PwshNative
{
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFOW
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;   // 既定のままでOK: "winsta0\\default" が使われます
        public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    const uint TOKEN_QUERY = 0x0008;
    const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    const uint CREATE_NEW_CONSOLE = 0x00000010;

    public static void LaunchPwshStrict()
    {
        // 1) 現在ユーザーのトークンを取得
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out var hToken))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // 2) そのユーザーの“既定”環境ブロックを生成（Explorer 同等）
        if (!CreateEnvironmentBlock(out var env, hToken, false))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 3) 新しいコンソールで pwsh.exe を起動
            var si = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
            var pi = new PROCESS_INFORMATION();

            string commandLine = "pwsh.exe -NoExit -Command \"Import-Module PowerShell.MCP\"";

            bool ok = CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_UNICODE_ENVIRONMENT | CREATE_NEW_CONSOLE,
                env,                       // ← Explorer 相当の環境をそのまま渡す
                userProfile,               // 作業ディレクトリ
                ref si,
                out pi);

            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            // ハンドルは必要なら CloseHandle で明示的に閉じてください
        }
        finally
        {
            DestroyEnvironmentBlock(env);
        }
    }
}
