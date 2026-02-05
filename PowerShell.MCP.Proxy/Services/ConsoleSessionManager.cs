using System.IO.MemoryMappedFiles;
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
    /// Per-agent session state (ActivePipeName and KnownBusyPids)
    /// </summary>
    private class AgentSessionState
    {
        public string? ActivePipeName { get; set; }
        public readonly HashSet<int> KnownBusyPids = new();
    }

    private readonly Dictionary<string, AgentSessionState> _agentSessions = new();

    /// <summary>
    /// Category index assigned to this proxy instance
    /// </summary>
    private readonly int _categoryIndex;

    /// <summary>
    /// Shuffled queue of names for this category (refilled when exhausted)
    /// </summary>
    private readonly Queue<string> _shuffledNames = new();

    /// <summary>
    /// Set of pwsh PIDs that have been assigned a console title
    /// </summary>
    private readonly HashSet<int> _titledPids = new();

    /// <summary>
    /// File path for shared memory backing (cross-platform)
    /// </summary>
    private static readonly string SharedMemoryFile = Path.Combine(Path.GetTempPath(), "PowerShell.MCP.AllocatedConsoleCategories.dat");

    /// <summary>
    /// Mutex name for synchronizing access
    /// </summary>
    private const string MutexName = "PowerShell.MCP.AllocatedConsoleCategories";

    /// <summary>
    /// Shared memory layout constants
    /// </summary>
    private const int MaxEntries = 64;
    private const int EntrySize = 8;        // 4 bytes PID + 4 bytes category index
    private const int HeaderSize = 8;       // 4 bytes magic + 4 bytes count
    private const int SharedMemorySize = HeaderSize + (MaxEntries * EntrySize);
    private const int MagicNumber = 0x4D435043; // "MCPC"

    /// <summary>
    /// Console name categories - each proxy gets a unique category
    /// </summary>
    private static readonly string[][] Categories = new[]
    {
        // Animals
        new[] { "Cat", "Dog", "Fox", "Wolf", "Bear", "Lion", "Tiger", "Panda", "Koala", "Rabbit" },
        // Zodiac
        new[] { "Aries", "Taurus", "Gemini", "Capricorn", "Leo", "Virgo", "Libra", "Scorpio", "Aquarius", "Pisces" },
        // Gems
        new[] { "Sapphire", "Emerald", "Diamond", "Pearl", "Opal", "Topaz", "Amber", "Ruby", "Amethyst", "Quartz" },
        // Planets & Moons
        new[] { "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Neptune", "Pluto", "Titan", "Europa", "Luna" },
        // Colors
        new[] { "Red", "Blue", "Green", "Yellow", "Cyan", "Pink", "Purple", "Brown", "Gray", "White" },
        // Birds
        new[] { "Robin", "Sparrow", "Falcon", "Raven", "Phoenix", "Crane", "Swan", "Finch", "Jay", "Dove" },
        // Flowers
        new[] { "Rose", "Lily", "Iris", "Daisy", "Lotus", "Orchid", "Tulip", "Jasmine", "Dahlia", "Peony" },
        // Trees
        new[] { "Oak", "Pine", "Maple", "Cedar", "Willow", "Birch", "Elm", "Ash", "Cherry", "Cypress" },
        // Mountains
        new[] { "Mt.Fuji", "Everest", "K2", "Kilimanjaro", "Mt.Olympus", "Denali", "Mt.Blanc", "Matterhorn", "Eiger", "Elbrus" },
        // Seas & Oceans
        new[] { "Pacific", "Atlantic", "Arctic", "Baltic", "Aegean", "Caspian", "Adriatic", "Nordic", "Arabian", "Tasman" },
        // Greek Mythology
        new[] { "Zeus", "Athena", "Hermes", "Artemis", "Hera", "Hades", "Poseidon", "Demeter", "Hestia", "Ares" },
        // Music Genres
        new[] { "Jazz", "Blues", "Rock", "Soul", "Funk", "Reggae", "Pop", "Metal", "Punk", "Classical" },
        // Weather
        new[] { "Sunny", "Cloudy", "Misty", "Breeze", "Rainbow", "Dusk", "Dawn", "Twilight", "Drizzle", "Haze" },
        // Fruits
        new[] { "Mango", "Apple", "Peach", "Grape", "Kiwi", "Papaya", "Melon", "Guava", "Banana", "Plum" },
        // Fish & Sea Creatures
        new[] { "Salmon", "Tuna", "Shark", "Swordfish", "Catfish", "Trout", "Piranha", "Angelfish", "Koi", "Sardine" },
        // Vegetables
        new[] { "Carrot", "Potato", "Tomato", "Onion", "Garlic", "Pumpkin", "Bean", "Corn", "Broccoli", "Radish" },
        // Dinosaurs
        new[] { "Rex", "Raptor", "Stego", "Diplo", "Tricera", "Ankylo", "Bronto", "Ptera", "Spino", "Pachy" },
        // Cheese
        new[] { "Cheddar", "Gouda", "Brie", "Feta", "Mozza", "Parma", "Ricotta", "Camembert", "Edam", "Mascarpone" },
        // Sports
        new[] { "Soccer", "Tennis", "Golf", "Baseball", "Basketball", "Rugby", "Hockey", "Volleyball", "Bowling", "Softball" },
        // Spices & Herbs
        new[] { "Cumin", "Sage", "Thyme", "Basil", "Oregano", "Paprika", "Saffron", "Clove", "Nutmeg", "Cardamom" },
        // Instruments
        new[] { "Piano", "Guitar", "Violin", "Flute", "Drums", "Harp", "Cello", "Trumpet", "Banjo", "Oboe" },
        // Dance Styles
        new[] { "Ballet", "Tap", "Polka", "Moonwalk", "Flamenco", "Breakdance", "Waltz", "Twist", "Hiphop", "Tango" },
        // Mythical Creatures
        new[] { "Dragon", "Griffin", "Unicorn", "Pegasus", "Sphinx", "Chimera", "Hydra", "Centaur", "Kraken", "Wyvern" },
        // Stars
        new[] { "Sirius", "Vega", "Polaris", "Rigel", "Altair", "Deneb", "Betelgeuse", "Antares", "Spica", "Proxima" },
        // Space Objects
        new[] { "Nova", "Pulsar", "Quasar", "Nebula", "Comet", "Meteor", "Orbit", "Galaxy", "Cosmos", "Asteroid" },
        // Chemical Elements
        new[] { "Helium", "Neon", "Argon", "Xenon", "Cobalt", "Nickel", "Copper", "Zinc", "Titanium", "Carbon" },
        // Architecture
        new[] { "Tower", "Castle", "Palace", "Manor", "Villa", "Temple", "Fortress", "Chateau", "Bridge", "Abbey" },
        // Desserts
        new[] { "Waffle", "Crepe", "Pie", "Donut", "Cupcake", "Pancake", "Cake", "Pudding", "Caramel", "Cookie" },
        // Fabrics
        new[] { "Silk", "Velvet", "Cotton", "Linen", "Denim", "Satin", "Cashmere", "Tweed", "Flannel", "Suede" },
        // Drinks
        new[] { "Coffee", "Tea", "Juice", "Milk", "Cocoa", "Soda", "Water", "Cola", "Lemonade", "Smoothie" },
        // Vehicles
        new[] { "Car", "Bike", "Train", "Plane", "Boat", "Bus", "Taxi", "Truck", "Ship", "Subway" },
    };

    private ConsoleSessionManager()
    {
        _categoryIndex = InitializeCategory();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupCategory();
    }

    /// <summary>
    /// Gets the session state for the specified agent, creating it if necessary.
    /// Must be called within lock(_lock).
    /// </summary>
    private AgentSessionState GetOrCreateAgentState(string agentId)
    {
        if (!_agentSessions.TryGetValue(agentId, out var state))
        {
            state = new AgentSessionState();
            _agentSessions[agentId] = state;
        }
        return state;
    }

    /// <summary>
    /// Generates a pipe name for proxy-started consoles (format: {name}.{proxyPid}.{agentId}.{pwshPid})
    /// </summary>
    public static string GetPipeNameForPids(int proxyPid, string agentId, int pwshPid) => $"{DefaultPipeName}.{proxyPid}.{agentId}.{pwshPid}";

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
    /// Gets the active pipe name for the specified agent
    /// </summary>
    public string? GetActivePipeName(string agentId)
    {
        lock (_lock)
        {
            return GetOrCreateAgentState(agentId).ActivePipeName;
        }
    }

    /// <summary>
    /// Sets the active pipe name for the specified agent
    /// </summary>
    public void SetActivePipeName(string agentId, string? pipeName)
    {
        lock (_lock)
        {
            GetOrCreateAgentState(agentId).ActivePipeName = pipeName;
            if (pipeName != null)
            {
                Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Active pipe set to '{pipeName}' (agent={agentId})");
            }
        }
    }

    /// <summary>
    /// Marks a pipe as busy for the specified agent
    /// </summary>
    public void MarkPipeBusy(string agentId, int pid)
    {
        lock (_lock)
        {
            GetOrCreateAgentState(agentId).KnownBusyPids.Add(pid);
        }
    }

    /// <summary>
    /// Removes a pipe from busy tracking for the specified agent
    /// </summary>
    public void UnmarkPipeBusy(string agentId, int pid)
    {
        lock (_lock)
        {
            GetOrCreateAgentState(agentId).KnownBusyPids.Remove(pid);
        }
    }

    /// <summary>
    /// Gets known busy PIDs and clears the set for the specified agent
    /// </summary>
    public HashSet<int> ConsumeKnownBusyPids(string agentId)
    {
        lock (_lock)
        {
            var state = GetOrCreateAgentState(agentId);
            var result = new HashSet<int>(state.KnownBusyPids);
            state.KnownBusyPids.Clear();
            return result;
        }
    }

    /// <summary>
    /// Clears a dead pipe from active pipe and busy tracking for the specified agent
    /// </summary>
    public void ClearDeadPipe(string agentId, string pipeName)
    {
        lock (_lock)
        {
            var state = GetOrCreateAgentState(agentId);
            if (state.ActivePipeName == pipeName)
            {
                state.ActivePipeName = null;
            }
            var pid = GetPidFromPipeName(pipeName);
            if (pid.HasValue)
            {
                state.KnownBusyPids.Remove(pid.Value);
                _titledPids.Remove(pid.Value);
            }
            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Cleared dead pipe '{pipeName}' (agent={agentId})");

            // Remove empty agent session to prevent memory accumulation
            if (agentId != "default" && state.ActivePipeName == null && state.KnownBusyPids.Count == 0)
            {
                _agentSessions.Remove(agentId);
            }
        }
    }

    /// <summary>
    /// Enumerates PowerShell.MCP Named Pipes lazily by scanning the file system.
    /// Uses yield return for lazy evaluation - stops scanning when caller stops iterating.
    /// Windows: \\.\pipe\PowerShell.MCP.Communication.*
    /// Linux/macOS: /tmp/CoreFxPipe_PowerShell.MCP.Communication.*
    /// </summary>
    /// <param name="proxyPid">If specified, only returns pipes owned by this proxy</param>
    /// <param name="agentId">If specified with proxyPid, only returns pipes for this agent (format: {name}.{proxyPid}.{agentId}.*)</param>
    public IEnumerable<string> EnumeratePipes(int? proxyPid = null, string? agentId = null)
    {
        IEnumerable<string> paths;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Build filter pattern based on proxyPid and agentId
        string filterPattern;
        if (proxyPid.HasValue && agentId != null)
            filterPattern = $"{DefaultPipeName}.{proxyPid.Value}.{agentId}.*";
        else if (proxyPid.HasValue)
            filterPattern = $"{DefaultPipeName}.{proxyPid.Value}.*";
        else
            filterPattern = $"{DefaultPipeName}*";

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
    /// Owned pipes have 6 segments: {name}.{proxyPid}.{agentId}.{pwshPid}
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
            // Owned:   PowerShell.MCP.Communication.{proxyPid}.{agentId}.{pwshPid} = 6 segments
            var segments = baseName.Split('.');
            if (segments.Length == 4 && int.TryParse(segments[^1], out _))
            {
                yield return pipe;
            }
        }
    }

    /// <summary>
    /// Assigns a console name to the specified pwsh PID. Returns null if already assigned.
    /// </summary>
    /// <param name="pwshPid">The PID of the PowerShell process</param>
    /// <returns>Console name in format "#pwshPid name", or null if already titled</returns>
    public string? TryAssignNameToPid(int pwshPid)
    {
        lock (_lock)
        {
            if (!_titledPids.Add(pwshPid))
                return null;

            if (_shuffledNames.Count == 0)
                RefillShuffledNames();

            var name = _shuffledNames.Dequeue();
            return $"#{pwshPid} {name}";
        }
    }

    /// <summary>
    /// Refills the shuffled names queue with category names in random order (Fisher-Yates shuffle)
    /// </summary>
    private void RefillShuffledNames()
    {
        var names = Categories[_categoryIndex].ToArray();
        for (int i = names.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (names[i], names[j]) = (names[j], names[i]);
        }
        foreach (var n in names) _shuffledNames.Enqueue(n);
    }

    /// <summary>
    /// Initializes the category for this proxy instance using shared memory
    /// </summary>
    private int InitializeCategory()
    {
        using var mutex = new Mutex(false, MutexName, out _);

        try
        {
            mutex.WaitOne();

            using var mmf = MemoryMappedFile.CreateFromFile(SharedMemoryFile, FileMode.OpenOrCreate, null, SharedMemorySize);
            using var accessor = mmf.CreateViewAccessor();

            // Read and validate header
            int magic = accessor.ReadInt32(0);
            int count = accessor.ReadInt32(4);

            if (magic != MagicNumber)
            {
                // Initialize fresh
                accessor.Write(0, MagicNumber);
                count = 0;
            }

            // Read existing entries and clean up dead processes
            var usedCategories = new Dictionary<int, int>();
            var validEntries = new List<(int pid, int category)>();

            for (int i = 0; i < count && i < MaxEntries; i++)
            {
                int offset = HeaderSize + (i * EntrySize);
                int pid = accessor.ReadInt32(offset);
                int category = accessor.ReadInt32(offset + 4);

                if (IsProcessAlive(pid))
                {
                    usedCategories[pid] = category;
                    validEntries.Add((pid, category));
                }
            }

            // Find unused category
            var usedIndices = usedCategories.Values.ToHashSet();
            var availableIndices = Enumerable.Range(0, Categories.Length)
                .Where(i => !usedIndices.Contains(i))
                .ToList();

            int categoryIndex = availableIndices.Count > 0
                ? availableIndices[Random.Shared.Next(availableIndices.Count)]
                : Random.Shared.Next(Categories.Length); // All used, pick any randomly

            // Add this proxy's entry
            validEntries.Add((ProxyPid, categoryIndex));

            // Write back all valid entries
            accessor.Write(4, validEntries.Count);
            for (int i = 0; i < validEntries.Count; i++)
            {
                int offset = HeaderSize + (i * EntrySize);
                accessor.Write(offset, validEntries[i].pid);
                accessor.Write(offset + 4, validEntries[i].category);
            }

            Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Using category {categoryIndex} ({Categories[categoryIndex][0]}, {Categories[categoryIndex][1]}, ...)");
            return categoryIndex;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Checks if a process with the given PID is still running
    /// </summary>
    private static bool IsProcessAlive(int pid)
    {
        try
        {
            Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes this proxy's entry from the shared memory
    /// </summary>
    private void CleanupCategory()
    {
        try
        {
            using var mutex = new Mutex(false, MutexName, out _);
            mutex.WaitOne();

            try
            {
                using var mmf = MemoryMappedFile.CreateFromFile(SharedMemoryFile, FileMode.OpenOrCreate, null, SharedMemorySize);
                using var accessor = mmf.CreateViewAccessor();

                int magic = accessor.ReadInt32(0);
                int count = accessor.ReadInt32(4);

                if (magic != MagicNumber) return;

                // Read entries, excluding this proxy
                var validEntries = new List<(int pid, int category)>();
                for (int i = 0; i < count && i < MaxEntries; i++)
                {
                    int offset = HeaderSize + (i * EntrySize);
                    int pid = accessor.ReadInt32(offset);
                    int category = accessor.ReadInt32(offset + 4);

                    if (pid != ProxyPid && IsProcessAlive(pid))
                    {
                        validEntries.Add((pid, category));
                    }
                }

                // Write back
                accessor.Write(4, validEntries.Count);
                for (int i = 0; i < validEntries.Count; i++)
                {
                    int offset = HeaderSize + (i * EntrySize);
                    accessor.Write(offset, validEntries[i].pid);
                    accessor.Write(offset + 4, validEntries[i].category);
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
        catch
        {
            // Ignore cleanup errors (shared memory may not exist)
        }
    }
}
