using System.Text.Json;
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

    /// <summary>
    /// Category index assigned to this proxy instance
    /// </summary>
    private readonly int _categoryIndex;

    /// <summary>
    /// Shuffled queue of names for this category (refilled when exhausted)
    /// </summary>
    private readonly Queue<string> _shuffledNames = new();

    /// <summary>
    /// Path to the category lock file
    /// </summary>
    private static readonly string CategoryLockFile = Path.Combine(Path.GetTempPath(), "PowerShell.MCP.Categories.json");

    /// <summary>
    /// Console name categories - each proxy gets a unique category
    /// </summary>
    private static readonly string[][] Categories = new[]
    {
        // Animals
        new[] { "Cat", "Dog", "Fox", "Wolf", "Bear", "Lion", "Tiger", "Panda", "Koala", "Otter" },
        // Zodiac
        new[] { "Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo", "Libra", "Scorpio", "Aquarius", "Pisces" },
        // Gems
        new[] { "Sapphire", "Emerald", "Diamond", "Pearl", "Opal", "Topaz", "Amber", "Garnet", "Onyx", "Quartz" },
        // Planets & Moons
        new[] { "Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Neptune", "Pluto", "Titan", "Europa", "Luna" },
        // Colors
        new[] { "Red", "Blue", "Green", "Gold", "Silver", "Violet", "Coral", "Cyan", "Crimson", "Indigo" },
        // Elements
        new[] { "Fire", "Water", "Wind", "Earth", "Light", "Shadow", "Storm", "Frost", "Thunder", "Crystal" },
        // Birds
        new[] { "Robin", "Sparrow", "Falcon", "Raven", "Phoenix", "Crane", "Swan", "Finch", "Jay", "Dove" },
        // Flowers
        new[] { "Rose", "Lily", "Iris", "Daisy", "Lotus", "Orchid", "Tulip", "Jasmine", "Dahlia", "Peony" },
        // Trees
        new[] { "Oak", "Pine", "Maple", "Cedar", "Willow", "Birch", "Elm", "Ash", "Cherry", "Cypress" },
        // Mountains
        new[] { "Fuji", "Everest", "K2", "Kilimanjaro", "Olympus", "Alps", "Blanc", "Matterhorn", "Eiger", "Elbrus" },
        // Seas & Oceans
        new[] { "Pacific", "Atlantic", "Arctic", "Baltic", "Aegean", "Caspian", "Adriatic", "Nordic", "Arabian", "Tasman" },
        // Greek Mythology
        new[] { "Apollo", "Athena", "Hermes", "Artemis", "Hera", "Hades", "Poseidon", "Demeter", "Hestia", "Ares" },
        // Music Genres
        new[] { "Jazz", "Blues", "Rock", "Soul", "Funk", "Reggae", "Tango", "Waltz", "Swing", "Bossa" },
        // Weather
        new[] { "Sunny", "Cloudy", "Misty", "Breeze", "Rainbow", "Dusk", "Dawn", "Twilight", "Drizzle", "Haze" },
        // Fruits
        new[] { "Mango", "Apple", "Orange", "Grape", "Kiwi", "Papaya", "Melon", "Guava", "Fig", "Plum" },
        // Fish & Sea Creatures
        new[] { "Salmon", "Tuna", "Shark", "Trout", "Bass", "Goldfish", "Cod", "Carp", "Koi", "Mackerel" },
        // Vegetables
        new[] { "Carrot", "Potato", "Tomato", "Onion", "Garlic", "Pepper", "Spinach", "Lettuce", "Celery", "Cabbage" },
        // Dinosaurs
        new[] { "Rex", "Raptor", "Stego", "Diplo", "Tricera", "Ankylo", "Bronto", "Ptera", "Spino", "Pachy" },
        // Cheese
        new[] { "Cheddar", "Gouda", "Brie", "Feta", "Mozza", "Parma", "Ricotta", "Camembert", "Cream", "Swiss" },
        // Pasta
        new[] { "Penne", "Spaghetti", "Rigatoni", "Ravioli", "Lasagna", "Macaroni", "Fettuccine", "Tortellini", "Linguine", "Gnocchi" },
        // Spices & Herbs
        new[] { "Cumin", "Sage", "Thyme", "Basil", "Oregano", "Paprika", "Saffron", "Clove", "Nutmeg", "Cardamom" },
        // Instruments
        new[] { "Piano", "Guitar", "Violin", "Flute", "Drums", "Harp", "Cello", "Trumpet", "Banjo", "Oboe" },
        // Dance Styles
        new[] { "Salsa", "Mambo", "Rumba", "Ballet", "Tap", "Samba", "Polka", "Disco", "Flamenco", "Breakdance" },
        // Mythical Creatures
        new[] { "Dragon", "Griffin", "Unicorn", "Pegasus", "Sphinx", "Chimera", "Hydra", "Centaur", "Kraken", "Wyvern" },
        // Stars
        new[] { "Sirius", "Vega", "Polaris", "Rigel", "Altair", "Deneb", "Betelgeuse", "Proxima", "Alpha", "Centauri" },
        // Space Objects
        new[] { "Nova", "Pulsar", "Quasar", "Nebula", "Comet", "Meteor", "Orbit", "Galaxy", "Cosmos", "Aurora" },
        // Chemical Elements
        new[] { "Helium", "Neon", "Argon", "Xenon", "Cobalt", "Nickel", "Copper", "Zinc", "Titanium", "Carbon" },
        // Architecture
        new[] { "Tower", "Castle", "Palace", "Manor", "Villa", "Temple", "Fortress", "Chateau", "Bridge", "Abbey" },
        // Desserts
        new[] { "Waffle", "Crepe", "Mousse", "Sorbet", "Gelato", "Truffle", "Toffee", "Fudge", "Caramel", "Cookie" },
        // Fabrics
        new[] { "Silk", "Velvet", "Cotton", "Linen", "Denim", "Satin", "Cashmere", "Tweed", "Flannel", "Suede" },
    };

    private ConsoleSessionManager()
    {
        _categoryIndex = InitializeCategory();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupCategory();
    }

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

    /// <summary>
    /// Gets the next console name for this proxy's category
    /// </summary>
    /// <returns>Console name in format "#proxyPid name"</returns>
    public string GetNextConsoleName()
    {
        if (_shuffledNames.Count == 0)
        {
            // Refill with shuffled category names (Fisher-Yates shuffle)
            var names = Categories[_categoryIndex].ToArray();
            for (int i = names.Length - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (names[i], names[j]) = (names[j], names[i]);
            }
            foreach (var n in names) _shuffledNames.Enqueue(n);
        }
        return $"#{ProxyPid} {_shuffledNames.Dequeue()}";
    }

    /// <summary>
    /// Initializes the category for this proxy instance by reading the lock file
    /// and selecting an unused category
    /// </summary>
    private int InitializeCategory()
    {
        Dictionary<int, int> usedCategories = new();

        try
        {
            if (File.Exists(CategoryLockFile))
            {
                var json = File.ReadAllText(CategoryLockFile);
                var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (data != null)
                {
                    // Clean up entries for dead processes
                    foreach (var kvp in data)
                    {
                        if (int.TryParse(kvp.Key, out var pid))
                        {
                            try
                            {
                                Process.GetProcessById(pid);
                                usedCategories[pid] = kvp.Value;
                            }
                            catch (ArgumentException)
                            {
                                // Process no longer exists, skip
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore read errors, start fresh
        }

        // Find unused categories and pick one randomly
        var usedIndices = usedCategories.Values.ToHashSet();
        var availableIndices = Enumerable.Range(0, Categories.Length)
            .Where(i => !usedIndices.Contains(i))
            .ToList();
        
        int categoryIndex = availableIndices.Count > 0
            ? availableIndices[Random.Shared.Next(availableIndices.Count)]
            : Random.Shared.Next(Categories.Length); // All used, pick any randomly

        // Register this proxy's category
        usedCategories[ProxyPid] = categoryIndex;

        try
        {
            var newData = usedCategories.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);
            var json = JsonSerializer.Serialize(newData);
            File.WriteAllText(CategoryLockFile, json);
        }
        catch
        {
            // Ignore write errors
        }

        Console.Error.WriteLine($"[INFO] ConsoleSessionManager: Using category {categoryIndex} ({Categories[categoryIndex][0]}, {Categories[categoryIndex][1]}, ...)");
        return categoryIndex;
    }

    /// <summary>
    /// Removes this proxy's entry from the category lock file
    /// </summary>
    private void CleanupCategory()
    {
        try
        {
            if (!File.Exists(CategoryLockFile)) return;

            var json = File.ReadAllText(CategoryLockFile);
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (data != null)
            {
                data.Remove(ProxyPid.ToString());
                var newJson = JsonSerializer.Serialize(data);
                File.WriteAllText(CategoryLockFile, newJson);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
