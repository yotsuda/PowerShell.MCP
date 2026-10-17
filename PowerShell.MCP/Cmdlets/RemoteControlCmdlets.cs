using System.Management.Automation;
using System.Management.Automation.Host;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using PowerShell.MCP.Services;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Enable-MCPRemoteControl: let an MCP proxy on another machine drive this
/// console. Prints the pairing code the AI must be given. See
/// RemoteConsoleListener for the protocol and the security model.
/// </summary>
/// <remarks>
/// Remote control is something a person at this machine grants, in a console
/// they can see: the cmdlet refuses to run where nobody could be watching (a
/// service, redirected input, -NonInteractive, a host that is not a console)
/// and asks for a yes before it listens or dials. There is deliberately no
/// switch to skip the question — that would make it a background agent.
/// </remarks>
[Cmdlet(VerbsLifecycle.Enable, "MCPRemoteControl", DefaultParameterSetName = "Listen")]
public class EnableMCPRemoteControlCmdlet : PSCmdlet
{
    [Parameter(ParameterSetName = "Listen", Position = 0)]
    [ValidateRange(1, 65535)]
    public int Port { get; set; } = RemoteConsoleListener.DefaultPort;

    /// <summary>"Any" (IPv4 and IPv6, the default) or one address to bind, such as 127.0.0.1.</summary>
    [Parameter(ParameterSetName = "Listen")]
    public string ListenAddress { get; set; } = "Any";

    /// <summary>
    /// Accept connections only from these addresses (the machine the AI runs
    /// on); others are closed before TLS. Recommended on any network you do
    /// not fully trust.
    /// </summary>
    [Parameter(ParameterSetName = "Listen")]
    public string[]? AllowFrom { get; set; }

    /// <summary>
    /// Dial the proxy instead of listening: host:port of the machine where the
    /// AI called accept_remote_console. For a console behind NAT or a firewall
    /// that cannot be opened — only an outbound connection is needed here.
    /// </summary>
    [Parameter(ParameterSetName = "Dial", Mandatory = true)]
    public string ConnectTo { get; set; } = null!;

    /// <summary>The pairing code accept_remote_console gave the AI, which the AI passes on.</summary>
    [Parameter(ParameterSetName = "Dial", Mandatory = true)]
    public string Code { get; set; } = null!;

    protected override void ProcessRecord()
    {
        if (ConsoleLiveness.IsOwned)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException("This console was started by a local MCP server and belongs to it. Enable remote control in a console you opened yourself, after Import-Module PowerShell.MCP."),
                "ConsoleOwnedLocally", ErrorCategory.InvalidOperation, null));
        }
        if (RemoteControl.IsActive)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException($"Remote control is already on ({RemoteControl.Describe()}). Disable-MCPRemoteControl first."),
                "AlreadyActive", ErrorCategory.InvalidOperation, null));
        }
        if (WhyNobodyCanWatch() is { } reason)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException($"Remote control needs a console a person can see and answer, and {reason}. Run Enable-MCPRemoteControl in an interactive PowerShell 7 window."),
                "NotInteractive", ErrorCategory.InvalidOperation, null));
        }

        if (ParameterSetName == "Dial")
        {
            Dial();
            return;
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

        IPAddress[]? allowFrom = null;
        if (AllowFrom is { Length: > 0 })
        {
            var parsed = new List<IPAddress>();
            foreach (var item in AllowFrom)
            {
                // IPAddress.TryParse takes shorthand ("127.0.0" is 127.0.0.0):
                // a typo would silently allow something else. Dotted IPv4
                // needs all four numbers.
                var text = item.Trim();
                if (!IPAddress.TryParse(text, out var ip) ||
                    (ip.AddressFamily == AddressFamily.InterNetwork && text.Split('.').Length != 4))
                    ThrowTerminatingError(new ErrorRecord(
                        new ArgumentException($"'{item}' is not a full IP address. -AllowFrom takes the IP addresses of the machine the AI runs on, such as 192.168.0.10."),
                        "BadAllowFrom", ErrorCategory.InvalidArgument, item));
                parsed.Add(ip!);
            }
            allowFrom = parsed.ToArray();
        }

        var publicNetworks = PublicNetworks();
        if (!Confirm(
                $"An AI on another machine will be able to connect to this console on TCP port {Port}" +
                (allowFrom != null ? $", from {string.Join(", ", allowFrom.Select(a => a.ToString()))} only," : "") +
                " with the pairing code shown next (valid for one connection, for 10 minutes)." +
                (publicNetworks.Count > 0 && allowFrom == null ? $" This machine is on a Public network ({string.Join(", ", publicNetworks)}): anyone on it can reach the port." : "")))
        {
            WriteWarning("Remote control was not enabled.");
            return;
        }

        string code;
        try
        {
            code = RemoteConsoleListener.Start(Port, address, allowFrom);
        }
        catch (InvalidOperationException ex)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "ListenerNotStarted", ErrorCategory.ResourceUnavailable, Port));
            return;
        }

        try { Host.UI.RawUI.WindowTitle = ConsoleTitle.Decorate(RemoteControl.WaitingTitle); } catch { }

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
        ui.WriteLine("  It connects with connect_remote_console and then runs commands here with execute_remote_command; every prompt starts with [AI from <its address>] meanwhile.");
        ui.WriteLine(ConsoleColor.DarkGray, bg, "  The code works once and expires in 10 minutes; when a session ends a new one is shown, and remote control stops if nobody uses it.");
        ui.WriteLine(ConsoleColor.DarkGray, bg, $"  Remote control also ends after {RemoteConsoleListener.MaxFailures} failed attempts, or with Disable-MCPRemoteControl.");
        if (publicNetworks.Count > 0 && allowFrom == null)
            ui.WriteLine(ConsoleColor.Yellow, bg, $"  Public network ({string.Join(", ", publicNetworks)}): consider Enable-MCPRemoteControl -AllowFrom <IP of the AI's machine>.");
        ui.WriteLine(ConsoleColor.DarkGray, bg, $"  Inbound TCP {Port} must be allowed through the firewall. On Windows, as administrator, for the AI's machine only:");
        var remoteAddress = allowFrom != null ? string.Join(",", allowFrom.Select(a => a.ToString())) : "<IP of the AI's machine>";
        ui.WriteLine(ConsoleColor.DarkGray, bg, $"    New-NetFirewallRule -DisplayName 'PowerShell.MCP remote console' -Direction Inbound -Protocol TCP -LocalPort {Port} -RemoteAddress {remoteAddress} -Action Allow");
        ui.WriteLine(ConsoleColor.DarkGray, bg, "  and when you are done:  Remove-NetFirewallRule -DisplayName 'PowerShell.MCP remote console'");
        ui.WriteLine();
    }

    private void Dial()
    {
        var target = ConnectTo.Trim();
        var colon = target.LastIndexOf(':');
        string host;
        int port;
        if (target.StartsWith('[') && target.IndexOf(']') is var end and > 0 && colon > end)
        {
            host = target[1..end];
        }
        else if (colon > 0 && target.IndexOf(':') == colon)
        {
            host = target[..colon];
        }
        else
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("-ConnectTo needs host:port — the port accept_remote_console reported on the other machine, e.g. my-pc:51234."),
                "BadConnectTo", ErrorCategory.InvalidArgument, ConnectTo));
            return;
        }
        if (!int.TryParse(target[(colon + 1)..], out port) || port is < 1 or > 65535)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException($"'{target}' has an invalid port."), "BadConnectTo", ErrorCategory.InvalidArgument, ConnectTo));
            return;
        }

        if (!Confirm($"This console will connect to the MCP server at {host}:{port}, and the AI there will be able to run commands here."))
        {
            WriteWarning("Remote control was not enabled.");
            return;
        }

        try
        {
            RemoteConsoleDialer.Start(host, port, Code);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            ThrowTerminatingError(new ErrorRecord(ex, "DialerNotStarted", ErrorCategory.InvalidOperation, ConnectTo));
            return;
        }

        try { Host.UI.RawUI.WindowTitle = ConsoleTitle.Decorate(RemoteControl.WaitingTitle); } catch { }

        var ui = Host.UI;
        var bg = ui.RawUI.BackgroundColor;
        ui.WriteLine();
        ui.WriteLine(ConsoleColor.Cyan, bg, $"  PowerShell.MCP is connecting to the remote MCP server at {host}:{port} (this console, PID {Environment.ProcessId}).");
        ui.WriteLine("  Once it answers and the pairing code matches, the AI there controls this console; you keep the prompt, see every command, and every prompt starts with [AI from <host>].");
        ui.WriteLine(ConsoleColor.DarkGray, bg, "  A dropped connection is redialed by itself. Remote control ends with Disable-MCPRemoteControl, when the AI disconnects, or when the other side refuses.");
        ui.WriteLine();
    }

    /// <summary>
    /// Null when a person can see and answer this console; otherwise why not.
    /// The point is that remote control cannot be switched on quietly from a
    /// script, a scheduled task or a hidden window.
    /// </summary>
    private string? WhyNobodyCanWatch()
    {
        if (!Environment.UserInteractive) return "this process is not interactive (a service or a scheduled task)";
        if (Console.IsInputRedirected) return "its input is redirected";
        if (StartedNonInteractive()) return "PowerShell was started with -NonInteractive";
        if (Host.Name != "ConsoleHost") return $"the host is '{Host.Name}', not a console";
        return null;
    }

    /// <summary>-NonInteractive (or an abbreviation) before the command or file argument.</summary>
    private static bool StartedNonInteractive()
    {
        foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
        {
            if (Regex.IsMatch(arg, @"^[-/](c|command|f|file|e|ec|encodedcommand)$", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(arg, @"^[-/]noni", RegexOptions.IgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// Asks the person at this console. Says who the AI will act as and
    /// whether that is elevated; the answer defaults to No.
    /// </summary>
    private bool Confirm(string what)
    {
        var user = OperatingSystem.IsWindows() ? $"{Environment.UserDomainName}\\{Environment.UserName}" : Environment.UserName;
        var elevated = Environment.IsPrivilegedProcess;
        var message = $"{what}{Environment.NewLine}" +
                      $"Commands will run in this console as {user}{(elevated ? ", ELEVATED (administrator)" : "")}, with everything this session can reach. " +
                      "You see every command here; Disable-MCPRemoteControl ends it at any time.";
        var choices = new System.Collections.ObjectModel.Collection<ChoiceDescription>
        {
            new("&Yes", "Allow an AI on another machine to control this console."),
            new("&No", "Do not enable remote control."),
        };
        try
        {
            return Host.UI.PromptForChoice("Allow remote control of this console?", message, choices, 1) == 0;
        }
        catch (Exception ex) when (ex is PSInvalidOperationException or HostException or NotImplementedException)
        {
            return false;
        }
    }

    /// <summary>Names of the networks this machine is on that Windows treats as Public; empty elsewhere.</summary>
    private List<string> PublicNetworks()
    {
        var names = new List<string>();
        if (!OperatingSystem.IsWindows()) return names;
        try
        {
            foreach (var item in InvokeCommand.InvokeScript(
                         "Get-NetConnectionProfile -ErrorAction SilentlyContinue | Where-Object { \"$($_.NetworkCategory)\" -eq 'Public' } | ForEach-Object { \"$($_.Name)\" }"))
            {
                if (item?.BaseObject is string name && name.Length > 0) names.Add(name);
            }
        }
        catch { }
        return names;
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
/// Disable-MCPRemoteControl: end remote control — stop listening or dialing,
/// and drop the remote session, if any.
/// </summary>
[Cmdlet(VerbsLifecycle.Disable, "MCPRemoteControl")]
public class DisableMCPRemoteControlCmdlet : PSCmdlet
{
    protected override void ProcessRecord()
    {
        var stopped = RemoteConsoleDialer.IsActive ? $"Stopped dialing {RemoteConsoleDialer.Target}" : "Remote listener stopped";
        // Blank lines around what it prints, like the other remote control notices.
        Host.UI.WriteLine();
        if (!RemoteControl.Stop("Disable-MCPRemoteControl", fromConsoleThread: true))
        {
            WriteWarning("This console is not under remote control.");
            Host.UI.WriteLine();
            return;
        }
        try { Host.UI.RawUI.WindowTitle = ConsoleTitle.Decorate($"#{Environment.ProcessId} ____"); } catch { }
        Host.UI.WriteLine(ConsoleColor.DarkGray, Host.UI.RawUI.BackgroundColor, $"{stopped}. This console is a plain MCP console again.");
        Host.UI.WriteLine();
    }
}
