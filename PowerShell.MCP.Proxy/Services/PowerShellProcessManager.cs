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
    /// <returns>true if startup succeeded</returns>
    public static async Task<bool> StartPowerShellWithModuleAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            PwshLauncherWindows.LaunchPwsh();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            PwshLauncherMacOS.LaunchPwsh();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            PwshLauncherLinux.LaunchPwsh();
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported operating system");
        }

        return await NamedPipeClient.WaitForPipeReadyAsync();
    }
}

/// <summary>
/// Windows-specific launcher using Win32 API to create a new console window
/// </summary>
public static class PwshLauncherWindows
{
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
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
    private struct STARTUPINFOW
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private const uint TOKEN_QUERY = 0x0008;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NEW_CONSOLE = 0x00000010;

    public static void LaunchPwsh()
    {
        IntPtr hToken = IntPtr.Zero;
        IntPtr env = IntPtr.Zero;
        IntPtr hProcess = IntPtr.Zero;
        IntPtr hThread = IntPtr.Zero;

        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, out hToken))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            // false = do not inherit current process environment
            // This uses only system/user default environment variables (Control Panel settings)
            if (!CreateEnvironmentBlock(out env, hToken, false))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var si = new STARTUPINFOW { cb = (uint)Marshal.SizeOf<STARTUPINFOW>() };
            var pi = new PROCESS_INFORMATION();

            string commandLine = "pwsh.exe -NoExit -Command \"Import-Module PSReadLine,PowerShell.MCP\"";

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

/// <summary>
/// macOS-specific launcher using AppleScript to open Terminal.app
/// </summary>
public static class PwshLauncherMacOS
{
    // Command to initialize PowerShell.MCP and remove PSReadLine (not supported on Linux/macOS)
    private const string PwshInitCommand = "Import-Module PowerShell.MCP; Remove-Module PSReadLine -ErrorAction SilentlyContinue";

    public static void LaunchPwsh()
    {
        // Use osascript with stdin to avoid shell quoting issues
        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process != null)
        {
            // Terminal.app opens a new window with a login shell (typically zsh)
            // which reads ~/.zprofile and sets up the user's environment including PATH
            // This ensures pwsh is found regardless of installation method (Homebrew, pkg, etc.)
            process.StandardInput.WriteLine("tell application \"Terminal\"");
            process.StandardInput.WriteLine("    activate");
            process.StandardInput.WriteLine($"    do script \"pwsh -NoExit -WorkingDirectory ~ -Command '{PwshInitCommand}'\"");
            process.StandardInput.WriteLine("end tell");
            process.StandardInput.Close();
            process.WaitForExit(5000);
        }
    }
}

/// <summary>
/// Linux-specific launcher that tries multiple terminal emulators
/// </summary>
public static class PwshLauncherLinux
{
    // Terminal emulator configurations: (name, useShellWrapper, args...)
    // useShellWrapper: true = use "sh -c" to wrap the command (for terminals that need a single command string)
    private static readonly string[] SupportedTerminals =
    [
        "gnome-terminal",
        "konsole",
        "xfce4-terminal",
        "xterm",
        "lxterminal",
        "mate-terminal",
        "terminator",
        "tilix",
        "alacritty",
        "kitty",
    ];

    // Command to initialize PowerShell.MCP and remove PSReadLine (not supported on Linux/macOS)
    private const string PwshInitCommand = "Import-Module PowerShell.MCP; Remove-Module PSReadLine -ErrorAction SilentlyContinue";


    public static void LaunchPwsh()
    {
        foreach (var terminal in SupportedTerminals)
        {
            if (TryLaunchTerminal(terminal))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "No supported terminal emulator found. Please install one of: " +
            string.Join(", ", SupportedTerminals));
    }

    private static bool TryLaunchTerminal(string terminal)
    {
        try
        {
            // Check if terminal exists using 'which'
            var whichPsi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = terminal,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var whichProcess = Process.Start(whichPsi);
            whichProcess?.WaitForExit(2000);
            
            if (whichProcess?.ExitCode != 0)
            {
                return false;
            }

            // Get user's default shell
            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";

            // Launch terminal via setsid to detach from parent process
            // We use the user's login shell to ensure ~/.bash_profile or ~/.zprofile is loaded,
            // which sets up PATH and other environment variables correctly.
            // This mimics what happens when a user manually opens a terminal and types 'pwsh'.
            var psi = new ProcessStartInfo
            {
                FileName = "setsid",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            // Command to launch pwsh with proper initialization via login shell
            // exec replaces the shell with pwsh to keep the process tree clean
            var pwshCommand = $"exec pwsh -NoExit -WorkingDirectory ~ -Command '{PwshInitCommand}'";

            // setsid <terminal> ... <shell> -l -c '<pwshCommand>'
            psi.ArgumentList.Add(terminal);

            // Configure arguments based on terminal type
            switch (terminal)
            {
                case "gnome-terminal":
                    psi.ArgumentList.Add("--");
                    psi.ArgumentList.Add(shell);
                    psi.ArgumentList.Add("-l");
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(pwshCommand);
                    break;

                case "konsole":
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(shell);
                    psi.ArgumentList.Add("-l");
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(pwshCommand);
                    break;

                case "xterm":
                case "lxterminal":
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(shell);
                    psi.ArgumentList.Add("-l");
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(pwshCommand);
                    break;

                case "xfce4-terminal":
                case "mate-terminal":
                case "terminator":
                case "tilix":
                    // These terminals expect -e with a single command string
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add($"{shell} -l -c '{pwshCommand}'");
                    break;

                case "alacritty":
                    psi.ArgumentList.Add("-e");
                    psi.ArgumentList.Add(shell);
                    psi.ArgumentList.Add("-l");
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(pwshCommand);
                    break;

                case "kitty":
                    psi.ArgumentList.Add(shell);
                    psi.ArgumentList.Add("-l");
                    psi.ArgumentList.Add("-c");
                    psi.ArgumentList.Add(pwshCommand);
                    break;

                default:
                    return false;
            }

            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
