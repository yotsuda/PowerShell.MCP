using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerShell.MCP.Proxy.Services;

public class PowerShellProcessManager
{
    private const string PowerShellExecutableName = "pwsh";

    /// <summary>
    /// Checks if a PowerShell process is running
    /// </summary>
    /// <returns>true if PowerShell process is found</returns>
    public static bool IsPowerShellProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(PowerShellExecutableName);
            var found = processes.Length > 0;
            
            // Release process object resources
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
    /// Starts PowerShell process with PowerShell.MCP module imported
    /// </summary>
    /// <param name="pipeClient">Named pipe client for health check</param>
    /// <returns>true if startup succeeded</returns>
    public static async Task<bool> StartPowerShellWithModuleAsync()
    {
        PwshNative.LaunchPwshStrict();
        return await NamedPipeClient.WaitForPipeReadyAsync();
    }
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

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

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
        public string? lpDesktop;   // Default is OK: "winsta0\\default" will be used
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
        IntPtr hToken = IntPtr.Zero;
        IntPtr env = IntPtr.Zero;
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            // 1) Get current user token
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out hToken))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            // 2) Create default environment block for user (same as Explorer)
            if (!CreateEnvironmentBlock(out env, hToken, false))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 3) Start pwsh.exe in new console
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
                env,
                userProfile,
                ref si,
                out pi);

            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());

            hProcess = pi.hProcess;
            hThread = pi.hThread;
        }
        finally
        {
            // Ensure resource cleanup
            if (env != IntPtr.Zero)
                DestroyEnvironmentBlock(env);
            
            if (hToken != IntPtr.Zero)
                CloseHandle(hToken);
            
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
            
            if (hThread != IntPtr.Zero)
                CloseHandle(hThread);
        }
    }
}
