namespace PowerShell.MCP.Services;

/// <summary>
/// Every window title this module sets goes through here. Windows marks an
/// elevated console by starting its title with "Administrator: "; replacing
/// the title would drop that mark, and a user (or a remote session's viewer)
/// would lose sight of the one fact that matters most about the window.
/// </summary>
public static class ConsoleTitle
{
    private const string AdminPrefix = "Administrator: ";

    public static bool Elevated { get; } = OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess;

    public static string Decorate(string title) =>
        Elevated && !title.StartsWith(AdminPrefix, StringComparison.Ordinal) ? AdminPrefix + title : title;

    private static volatile string? _known;

    /// <summary>
    /// The title as the engine last read it on the runspace thread, for the
    /// status line on Linux and macOS, where the pipe server cannot read the
    /// title itself. Asking the runspace instead queued behind a running
    /// command or a prompt — and, once the user answered, took that
    /// command's result as its own.
    /// </summary>
    public static string? Known => _known;

    /// <summary>Called by the engine's idle tick with $Host.UI.RawUI.WindowTitle.</summary>
    public static void Remember(string? title)
    {
        if (!string.IsNullOrEmpty(title)) _known = title;
    }

    private static volatile string? _name;

    /// <summary>
    /// The name the owning proxy gave this console ("Lithium" of
    /// "#43036 Lithium"), for Get-MCPOwner; null while unowned or not yet
    /// named. Kept from the proxy's set_window_title rather than read back
    /// from the window title, which the user, a remote session or a program
    /// run in the console may have changed since.
    /// </summary>
    public static string? Name => _name;

    /// <summary>Called with the title the proxy names this console with.</summary>
    public static void RememberName(string? title) => _name = ParseName(title, Environment.ProcessId);

    /// <summary>Called when the console changes owner: the next one names it anew.</summary>
    public static void ForgetName() => _name = null;

    /// <summary>
    /// The name in a title of the form "#&lt;pid&gt; &lt;name&gt;" (with or without
    /// the "Administrator: " mark), or null for any other title and for the
    /// unowned placeholder "#&lt;pid&gt; ____".
    /// </summary>
    internal static string? ParseName(string? title, int pid)
    {
        if (string.IsNullOrEmpty(title)) return null;
        if (title.StartsWith(AdminPrefix, StringComparison.Ordinal)) title = title[AdminPrefix.Length..];
        var prefix = $"#{pid} ";
        if (!title.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var name = title[prefix.Length..].Trim();
        return name.Length == 0 || name == "____" ? null : name;
    }
}
