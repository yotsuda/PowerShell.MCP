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

    /// <summary>Ends either mode. False when neither was active.</summary>
    public static bool Stop(string reason, bool fromConsoleThread) =>
        RemoteConsoleListener.Stop(reason, fromConsoleThread) | RemoteConsoleDialer.Stop(reason, fromConsoleThread);
}