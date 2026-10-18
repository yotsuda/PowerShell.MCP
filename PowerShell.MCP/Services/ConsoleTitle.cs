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
}
