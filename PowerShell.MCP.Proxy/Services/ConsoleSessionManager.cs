using System.Runtime.InteropServices;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Manages PowerShell console sessions with caching for efficiency
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
    /// Currently active (ready) Named Pipe name
    /// </summary>
    public string? ActivePipeName { get; private set; }
    
    /// <summary>
    /// Known busy pipes (pipe name -> last known status info)
    /// </summary>
    private readonly Dictionary<string, string> _busyPipes = new();

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
                // Remove from busy list if it was there
                _busyPipes.Remove(pipeName);
                Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Active pipe set to '{pipeName}'");
            }
        }
    }

    /// <summary>
    /// Marks a pipe as busy with its status info
    /// </summary>
    public void MarkPipeBusy(string pipeName, string statusInfo)
    {
        lock (_lock)
        {
            _busyPipes[pipeName] = statusInfo;
            
            // If this was the active pipe, clear it
            if (ActivePipeName == pipeName)
            {
                ActivePipeName = null;
            }
            
            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Marked pipe '{pipeName}' as busy");
        }
    }

    /// <summary>
    /// Gets all known busy pipes and their status info
    /// </summary>
    public Dictionary<string, string> GetBusyPipes()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_busyPipes);
        }
    }

    /// <summary>
    /// Removes a pipe from the busy list (when it becomes standby or dead)
    /// </summary>
    public void RemoveFromBusy(string pipeName)
    {
        lock (_lock)
        {
            _busyPipes.Remove(pipeName);
        }
    }

    /// <summary>
    /// Clears a dead pipe from all caches
    /// </summary>
    public void ClearDeadPipe(string pipeName)
    {
        lock (_lock)
        {
            _busyPipes.Remove(pipeName);
            if (ActivePipeName == pipeName)
            {
                ActivePipeName = null;
            }
            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Cleared dead pipe '{pipeName}'");
        }
    }

    /// <summary>
    /// Discovers all PowerShell.MCP Named Pipes by scanning the file system
    /// Windows: \\.\pipe\PowerShell.MCP.Communication.*
    /// Linux/macOS: /tmp/CoreFxPipe_PowerShell.MCP.Communication.*
    /// </summary>
    public List<string> DiscoverAllPipes()
    {
        var discoveredPipes = new List<string>();
        
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows: Named Pipes appear in \\.\pipe\
                var pipeDirectory = @"\\.\pipe\";
                var pipePrefix = DefaultPipeName;
                
                foreach (var pipePath in Directory.GetFiles(pipeDirectory, $"{pipePrefix}*"))
                {
                    var pipeName = Path.GetFileName(pipePath);
                    discoveredPipes.Add(pipeName);
                }
            }
            else
            {
                // Linux/macOS: .NET Named Pipes use Unix Domain Sockets at /tmp/CoreFxPipe_*
                var socketDirectory = "/tmp";
                var socketPrefix = $"CoreFxPipe_{DefaultPipeName}";
                
                foreach (var socketPath in Directory.GetFiles(socketDirectory, $"{socketPrefix}*"))
                {
                    var socketName = Path.GetFileName(socketPath);
                    // Remove the CoreFxPipe_ prefix to get the pipe name
                    var pipeName = socketName["CoreFxPipe_".Length..];
                    discoveredPipes.Add(pipeName);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] DiscoverAllPipes: Failed to scan pipes: {ex.Message}");
        }
        
        return discoveredPipes;
    }
}
