using System.Management.Automation;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PowerShell.MCP.Services;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Start-MCPRemoteListener: let an MCP proxy on another machine drive this
/// console. Prints the pairing code the AI must be given. See
/// RemoteConsoleListener for the protocol and the security model.
/// </summary>
[Cmdlet(VerbsLifecycle.Start, "MCPRemoteListener")]
public class StartMCPRemoteListenerCmdlet : PSCmdlet
{
    [Parameter(Position = 0)]
    [ValidateRange(1, 65535)]
    public int Port { get; set; } = RemoteConsoleListener.DefaultPort;

    /// <summary>"Any" (IPv4 and IPv6, the default) or one address to bind, such as 127.0.0.1.</summary>
    [Parameter]
    public string ListenAddress { get; set; } = "Any";

    protected override void ProcessRecord()
    {
        if (ConsoleLiveness.IsOwned)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException("This console was started by a local MCP server and belongs to it. Start the listener in a console you opened yourself, after Import-Module PowerShell.MCP."),
                "ConsoleOwnedLocally", ErrorCategory.InvalidOperation, null));
        }

        IPAddress address;
        if (ListenAddress.Equals("Any", StringComparison.OrdinalIgnoreCase))
            address = IPAddress.IPv6Any;
        else if (!IPAddress.TryParse(ListenAddress, out address!))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException($"'{ListenAddress}' is not an IP address. Use 'Any' or an address of this machine."),
                "BadListenAddress", ErrorCategory.InvalidArgument, ListenAddress));
        }

        string code;
        try
        {
            code = RemoteConsoleListener.Start(Port, address);
        }
        catch (InvalidOperationException ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "ListenerNotStarted", ErrorCategory.ResourceUnavailable, Port));
            return;
        }

        try { Host.UI.RawUI.WindowTitle = $"PowerShell.MCP [listening :{Port}]"; } catch { }

        var ui = Host.UI;
        var bg = ui.RawUI.BackgroundColor;
        ui.WriteLine();
        ui.WriteLine(ConsoleColor.Cyan, bg, $"  PowerShell.MCP is listening for a remote MCP server on TCP port {Port} (this console, PID {Environment.ProcessId}).");
        ui.WriteLine();
        ui.WriteLine(ConsoleColor.Yellow, bg, $"      Pairing code:   {RemoteConsoleListener.FormatCode(code)}");
        ui.WriteLine();
        // The host name alone often does not resolve from the other machine
        // (another subnet, a VPN, no shared DNS); the addresses spare the user
        // an ipconfig. IPv4 only: link-local IPv6 would be noise.
        var addresses = LocalAddresses(address);
        var hostInfo = addresses.Count > 0
            ? $"{Dns.GetHostName()}  —  IP: {string.Join(", ", addresses)}"
            : Dns.GetHostName();
        ui.WriteLine($"  Give the AI on the other machine the host and the code. Host: {hostInfo}");
        ui.WriteLine("  It connects with connect_remote_console and then runs commands here with execute_remote_command.");
        ui.WriteLine(ConsoleColor.DarkGray, bg, $"  The listener stops after {RemoteConsoleListener.MaxFailures} failed attempts, or with Stop-MCPRemoteListener.");
        ui.WriteLine(ConsoleColor.DarkGray, bg, $"  Inbound TCP {Port} must be allowed through the firewall. On Windows, as administrator:");
        ui.WriteLine(ConsoleColor.DarkGray, bg, $"    New-NetFirewallRule -DisplayName 'PowerShell.MCP remote console' -Direction Inbound -Protocol TCP -LocalPort {Port} -Action Allow");
        ui.WriteLine();
    }

    /// <summary>
    /// The IPv4 addresses a remote machine could reach this one at: every
    /// address of an interface that is up and not loopback, or just the one
    /// the listener was bound to.
    /// </summary>
    private static List<string> LocalAddresses(IPAddress bound)
    {
        if (!bound.Equals(IPAddress.IPv6Any) && !bound.Equals(IPAddress.Any) && !IPAddress.IsLoopback(bound))
            return [bound.ToString()];
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    result.Add(unicast.Address.ToString());
                }
            }
        }
        catch { }
        return result;
    }
}

/// <summary>
/// Stop-MCPRemoteListener: stop listening and drop the remote session, if any.
/// </summary>
[Cmdlet(VerbsLifecycle.Stop, "MCPRemoteListener")]
public class StopMCPRemoteListenerCmdlet : PSCmdlet
{
    protected override void ProcessRecord()
    {
        if (!RemoteConsoleListener.Stop("Stop-MCPRemoteListener", fromConsoleThread: true))
        {
            WriteWarning("This console is not listening for a remote MCP server.");
            return;
        }
        try { Host.UI.RawUI.WindowTitle = $"#{Environment.ProcessId} ____"; } catch { }
        Host.UI.WriteLine(ConsoleColor.DarkGray, Host.UI.RawUI.BackgroundColor, "Remote listener stopped. This console is a plain MCP console again.");
    }
}