using System.Runtime.InteropServices;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Manages PowerShell console sessions
/// </summary>
public class ConsoleSessionManager
{
    private static readonly Lazy<ConsoleSessionManager> _instance = new(() => new ConsoleSessionManager());
    public static ConsoleSessionManager Instance => _instance.Value;

    private readonly object _lock = new();

    /// <summary>
    /// Base pipe name prefix for PowerShell.MCP
    /// </summary>
    public const string DefaultPipeName = "PowerShell.MCP.Communication";

    /// <summary>
    /// Error message when module is not imported
    /// </summary>
    public const string ErrorModuleNotImported = "PowerShell.MCP module is not imported in existing pwsh.";

    /// <summary>
    /// Currently active (ready) Named Pipe name
    /// </summary>
    public string? ActivePipeName { get; private set; }

    private ConsoleSessionManager() { }

    /// <summary>
    /// Generates a PID-based pipe name for proxy-started consoles
    /// </summary>
    public static string GetPipeNameForPid(int pid) => $"{DefaultPipeName}.{pid}";

    /// <summary>
    /// Sets the active pipe name
    /// </summary>
    public void SetActivePipeName(string? pipeName)
    {
        lock (_lock)
        {
            ActivePipeName = pipeName;
            if (pipeName != null)
            {
                Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Active pipe set to '{pipeName}'");
            }
        }
    }

    /// <summary>
    /// Clears a dead pipe from active pipe
    /// </summary>
    public void ClearDeadPipe(string pipeName)
    {
        lock (_lock)
        {
            if (ActivePipeName == pipeName)
            {
                ActivePipeName = null;
            }
            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Cleared dead pipe '{pipeName}'");
        }
    }

    /// <summary>
    /// Enumerates PowerShell.MCP Named Pipes lazily by scanning the file system.
    /// Uses yield return for lazy evaluation - stops scanning when caller stops iterating.
    /// Windows: \\.\pipe\PowerShell.MCP.Communication.*
    /// Linux/macOS: /tmp/CoreFxPipe_PowerShell.MCP.Communication.*
    /// </summary>
    public IEnumerable<string> EnumeratePipes()
    {
        IEnumerable<string> paths;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        try
        {
            if (isWindows)
            {
                // Windows: Named Pipes appear in \\.\pipe\
                paths = Directory.EnumerateFiles(@"\\.\pipe\", $"{DefaultPipeName}*");
            }
            else
            {
                // Linux/macOS: .NET Named Pipes use Unix Domain Sockets at CoreFxPipe_*
                // macOS may use $TMPDIR instead of /tmp
                var directories = new List<string> { "/tmp" };
                var tmpDir = Environment.GetEnvironmentVariable("TMPDIR");
                if (!string.IsNullOrEmpty(tmpDir) && tmpDir != "/tmp" && tmpDir != "/tmp/")
                {
                    directories.Add(tmpDir.TrimEnd('/'));
                }

                paths = directories
                    .Where(Directory.Exists)
                    .SelectMany(dir =>
                    {
                        try { return Directory.EnumerateFiles(dir, $"CoreFxPipe_{DefaultPipeName}*"); }
                        catch { return Enumerable.Empty<string>(); }
                    });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] EnumeratePipes: Failed to scan pipes: {ex.Message}");
            yield break;
        }

        foreach (var path in paths)
        {
            var fileName = Path.GetFileName(path);

            if (isWindows)
            {
                yield return fileName;
            }
            else
            {
                // Remove the CoreFxPipe_ prefix to get the pipe name
                yield return fileName["CoreFxPipe_".Length..];
            }
        }
    }
}
