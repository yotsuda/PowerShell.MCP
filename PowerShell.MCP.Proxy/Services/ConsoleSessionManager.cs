using System.Diagnostics;
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
    /// The PID of this proxy process (used for pipe name filtering)
    /// </summary>
    public int ProxyPid { get; } = Process.GetCurrentProcess().Id;

    /// <summary>
    /// Currently active (ready) Named Pipe name
    /// </summary>
    public string? ActivePipeName { get; private set; }

    /// <summary>
    /// Known busy pipe PIDs (tracked across tool calls to detect externally closed consoles)
    /// </summary>
    private readonly HashSet<int> _knownBusyPids = new();

    private ConsoleSessionManager() { }

    /// <summary>
    /// Generates a pipe name for proxy-started consoles (format: {name}.{proxyPid}.{pwshPid})
    /// </summary>
    public static string GetPipeNameForPids(int proxyPid, int pwshPid) => $"{DefaultPipeName}.{proxyPid}.{pwshPid}";

    /// <summary>
    /// Extracts pwsh PID from pipe name (last segment: {name}.{pwshPid} or {name}.{proxyPid}.{pwshPid})
    /// </summary>
    public static int? GetPidFromPipeName(string pipeName)
    {
        var parts = pipeName.Split('.');
        if (parts.Length > 0 && int.TryParse(parts[^1], out var pid))
        {
            return pid;
        }
        return null;
    }

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
    /// Marks a pipe as busy
    /// </summary>
    public void MarkPipeBusy(int pid)
    {
        lock (_lock)
        {
            _knownBusyPids.Add(pid);
        }
    }

    /// <summary>
    /// Removes a pipe from busy tracking
    /// </summary>
    public void UnmarkPipeBusy(int pid)
    {
        lock (_lock)
        {
            _knownBusyPids.Remove(pid);
        }
    }

    /// <summary>
    /// Gets known busy PIDs and clears the set. Returns PIDs that were tracked as busy.
    /// </summary>
    public HashSet<int> ConsumeKnownBusyPids()
    {
        lock (_lock)
        {
            var result = new HashSet<int>(_knownBusyPids);
            _knownBusyPids.Clear();
            return result;
        }
    }

    /// <summary>
    /// Clears a dead pipe from active pipe and busy tracking
    /// </summary>
    public void ClearDeadPipe(string pipeName)
    {
        lock (_lock)
        {
            if (ActivePipeName == pipeName)
            {
                ActivePipeName = null;
            }
            var pid = GetPidFromPipeName(pipeName);
            if (pid.HasValue)
            {
                _knownBusyPids.Remove(pid.Value);
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
    /// <param name="proxyPid">If specified, only returns pipes owned by this proxy (format: {name}.{proxyPid}.*)</param>
    public IEnumerable<string> EnumeratePipes(int? proxyPid = null)
    {
        IEnumerable<string> paths;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Build filter pattern based on proxyPid
        string filterPattern = proxyPid.HasValue
            ? $"{DefaultPipeName}.{proxyPid.Value}.*"
            : $"{DefaultPipeName}*";

        try
        {
            if (isWindows)
            {
                // Windows: Named Pipes appear in \\.\pipe\
                paths = Directory.EnumerateFiles(@"\\.\pipe\", filterPattern);
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
                        try { return Directory.EnumerateFiles(dir, $"CoreFxPipe_{filterPattern}"); }
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

    /// <summary>
    /// Enumerates unowned PowerShell.MCP Named Pipes (user-started consoles not yet claimed by any proxy).
    /// Unowned pipes have 4 segments: {name}.{pwshPid}
    /// Owned pipes have 5 segments: {name}.{proxyPid}.{pwshPid}
    /// </summary>
    public IEnumerable<string> EnumerateUnownedPipes()
    {
        foreach (var pipe in EnumeratePipes(proxyPid: null))
        {
            // Get just the pipe name without directory path
            var baseName = Path.GetFileName(pipe);

            // Remove CoreFxPipe_ prefix (Linux/macOS)
            if (baseName.StartsWith("CoreFxPipe_"))
                baseName = baseName.Substring("CoreFxPipe_".Length);

            // Count segments after DefaultPipeName
            // Unowned: PowerShell.MCP.Communication.{pwshPid} = 4 segments
            // Owned:   PowerShell.MCP.Communication.{proxyPid}.{pwshPid} = 5 segments
            var segments = baseName.Split('.');
            if (segments.Length == 4 && int.TryParse(segments[^1], out _))
            {
                yield return pipe;
            }
        }
    }
}
