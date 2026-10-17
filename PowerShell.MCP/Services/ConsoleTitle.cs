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
}
