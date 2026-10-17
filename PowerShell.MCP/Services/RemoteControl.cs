namespace PowerShell.MCP.Services;

/// <summary>
/// Remote control of this console, whichever way it was set up: listening
/// for a proxy (RemoteConsoleListener) or dialing one (RemoteConsoleDialer).
/// What the local pipe needs to know is only that the console is spoken for.
/// </summary>
public static class RemoteControl
{
    public static bool IsActive => RemoteConsoleListener.IsListening || RemoteConsoleDialer.IsActive;

    /// <summary>Why a local proxy may not claim this console, for its status line.</summary>
    public static string Describe()
    {
        if (RemoteConsoleListener.IsListening)
            return RemoteConsoleListener.IsConnected
                ? $"console is driven by a remote MCP server ({RemoteConsoleListener.ConnectedFrom})"
                : $"console is listening for a remote MCP server on port {RemoteConsoleListener.Port}";
        if (RemoteConsoleDialer.IsActive)
            return RemoteConsoleDialer.IsConnected
                ? $"console is driven by the remote MCP server at {RemoteConsoleDialer.Target}"
                : $"console is dialing the remote MCP server at {RemoteConsoleDialer.Target}";
        return "console is not under remote control";
    }

    /// <summary>
    /// What every prompt starts with while remote control is on, or "".
    /// It names who drives the console, not where the path is: a [...]
    /// prefix such as Enter-PSSession's [server] means "this path is on
    /// that machine", and here the path is this machine's own. In listen
    /// mode the peer is named by its address, the only thing the pairing
    /// vouches for; in dial mode by the host this console dialed.
    /// </summary>
    public static string PromptTag
    {
        get
        {
            if (RemoteConsoleListener.IsListening)
                return $"[{(RemoteConsoleListener.ConnectedFrom is { } from ? ConnectedTitle(from) : WaitingTitle)}] ";
            if (RemoteConsoleDialer.IsActive)
                return $"[{(RemoteConsoleDialer.IsConnected ? ConnectedTitle(RemoteConsoleDialer.Host) : WaitingTitle)}] ";
            return "";
        }
    }

    // The window title says the same as the prompt tag, and as briefly: a
    // tab or taskbar button shows only its start, so who drives the console
    // comes first ("Administrator: " goes in front, see ConsoleTitle).
    public const string WaitingTitle = "AI remote control: waiting";
    public static string ConnectedTitle(string who) => $"AI from {who}";

    /// <summary>Ends either mode. False when neither was active.</summary>
    public static bool Stop(string reason, bool fromConsoleThread) =>
        RemoteConsoleListener.Stop(reason, fromConsoleThread) | RemoteConsoleDialer.Stop(reason, fromConsoleThread);
}