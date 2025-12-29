using System.IO.Pipes;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Manages multiple PowerShell console sessions and their Named Pipes
/// </summary>
public class ConsoleSessionManager
{
    private static readonly Lazy<ConsoleSessionManager> _instance = new(() => new ConsoleSessionManager());
    public static ConsoleSessionManager Instance => _instance.Value;

    private readonly object _lock = new();
    
    /// <summary>
    /// Default pipe name for user-imported module
    /// </summary>
    public const string DefaultPipeName = "PowerShell.MCP.Communication";
    
    /// <summary>
    /// Registered Named Pipe names
    /// </summary>
    private readonly List<string> _pipeNames = new();
    
    /// <summary>
    /// Currently active Named Pipe name
    /// </summary>
    public string? ActivePipeName { get; private set; }
    
    /// <summary>
    /// Unreported outputs from sessions (pipeName -> output)
    /// </summary>
    private readonly Dictionary<string, string> _unreportedOutputs = new();

    /// <summary>
    /// Busy consoles (pipeName -> last busy status string)
    /// </summary>
    private readonly Dictionary<string, string> _busyConsoles = new();

    private ConsoleSessionManager() { }

    /// <summary>
    /// Generates a PID-based pipe name for proxy-started consoles
    /// </summary>
    public static string GetPipeNameForPid(int pid) => $"{DefaultPipeName}.{pid}";

    /// <summary>
    /// Registers a new console session
    /// </summary>
    public void RegisterConsole(string pipeName, bool setAsActive = true)
    {
        lock (_lock)
        {
            if (!_pipeNames.Contains(pipeName))
            {
                _pipeNames.Add(pipeName);
                Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Registered pipe '{pipeName}'");
            }
            
            if (setAsActive)
            {
                ActivePipeName = pipeName;
                Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Active pipe set to '{pipeName}'");
            }
        }
    }

    /// <summary>
    /// Stores unreported output for a session
    /// </summary>
    public void SetUnreportedOutput(string pipeName, string output)
    {
        lock (_lock)
        {
            _unreportedOutputs[pipeName] = output;
            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Stored unreported output for pipe '{pipeName}'");
        }
    }

    /// <summary>
    /// Consumes all unreported outputs and returns them
    /// Also transitions those sessions to Standby
    /// </summary>
    public List<(string PipeName, string Output)> ConsumeUnreportedOutputs()
    {
        lock (_lock)
        {
            var outputs = _unreportedOutputs.Select(kv => (kv.Key, kv.Value)).ToList();
            _unreportedOutputs.Clear();
            return outputs;
        }
    }

    /// <summary>
    /// Unregisters a console session (when closed or connection lost)
    /// </summary>
    public void UnregisterConsole(string pipeName)
    {
        lock (_lock)
        {
            _pipeNames.Remove(pipeName);
            _unreportedOutputs.Remove(pipeName);
            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Unregistered pipe '{pipeName}'");
            
            if (ActivePipeName == pipeName)
            {
                ActivePipeName = _pipeNames.LastOrDefault();
                Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Active pipe changed to '{ActivePipeName ?? "none"}'");
            }
        }
    }

    /// <summary>
    /// Checks if a Named Pipe is connectable
    /// </summary>
    public bool CanConnect(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(500); // 500ms timeout
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cleans up dead consoles (called on MCP request)
    /// </summary>
    public void CleanupDeadConsoles()
    {
        lock (_lock)
        {
            foreach (var pipeName in _pipeNames.ToList())
            {
                if (!CanConnect(pipeName))
                {
                    Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Pipe '{pipeName}' is not connectable, removing");
                    UnregisterConsole(pipeName);
                }
            }
        }
    }

    /// <summary>
    /// Discovers and registers existing console with default pipe name (user-imported)
    /// </summary>
    public void DiscoverExistingConsole()
    {
        lock (_lock)
        {
            if (!_pipeNames.Contains(DefaultPipeName) && CanConnect(DefaultPipeName))
            {
                RegisterConsole(DefaultPipeName, setAsActive: true);
            }
        }
    }

    /// <summary>
    /// Gets an available (non-busy) console pipe name, or null if none available
    /// </summary>
    public string? GetAvailableConsolePipeName()
    {
        lock (_lock)
        {
            // First try active pipe
            if (ActivePipeName != null && CanConnect(ActivePipeName))
            {
                return ActivePipeName;
            }
            
            // Try other pipes
            foreach (var pipeName in _pipeNames.ToList())
            {
                if (CanConnect(pipeName))
                {
                    return pipeName;
                }
                else
                {
                    UnregisterConsole(pipeName);
                }
            }
            
            return null;
        }
    }

    /// <summary>
    /// Gets all registered pipe names
    /// </summary>
    public List<string> GetAllPipeNames()
    {
        lock (_lock)
        {
            return _pipeNames.ToList();
        }
    }

    /// <summary>
    /// Checks if there are any registered consoles
    /// </summary>
    public bool HasRegisteredConsoles()
    {
        lock (_lock)
        {
            return _pipeNames.Count > 0;
        }
    }

    /// <summary>
    /// Marks a console as busy with its status string
    /// </summary>
    public void SetConsoleBusy(string pipeName, string busyStatus)
    {
        lock (_lock)
        {
            _busyConsoles[pipeName] = busyStatus;
            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Marked pipe '{pipeName}' as busy");
        }
    }

    /// <summary>
    /// Clears the busy status for a console
    /// </summary>
    public void ClearConsoleBusy(string pipeName)
    {
        lock (_lock)
        {
            if (_busyConsoles.Remove(pipeName))
            {
                Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Cleared busy status for pipe '{pipeName}'");
            }
        }
    }

    /// <summary>
    /// Gets all busy consoles (pipeName -> status)
    /// </summary>
    public Dictionary<string, string> GetBusyConsoles()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_busyConsoles);
        }
    }
}