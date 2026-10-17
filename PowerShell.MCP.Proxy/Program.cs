using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Tools;
using PowerShell.MCP.Proxy.Prompts;
using System.Reflection;

namespace PowerShell.MCP.Proxy
{
    public class Program
    {
        public static readonly string ProxyVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

        /// <summary>
        /// Sent in the MCP initialize response. Clients put it in the AI's
        /// context whether or not they load the tool schemas up front (Claude
        /// Code defers most of them, showing only names), so it is where the
        /// AI learns what this server can do that a name does not say —
        /// above all that it can drive a console on another machine, which an
        /// AI would otherwise never think to offer. Kept short: it is read on
        /// every turn.
        /// </summary>
        internal const string ServerInstructions =
@"PowerShell.MCP: persistent PowerShell 7 consoles the user can see and share. Use execute_command for shell work here.

It can also drive a console on ANOTHER machine (a VM, a server, a colleague's PC; elevated if that console is), with the user there watching. When the user wants something done on another machine, offer this. Two ways:
- Usually simplest, and the only way when that machine has nothing installed or cannot accept inbound connections (NAT, cloud NSG): call accept_remote_console here. It returns ONE line for the user to paste on that machine (as Administrator for admin work), into PowerShell 7 if it is there — remote control then starts in that window — else into Windows PowerShell; it fetches PowerShell 7 and PowerShell.MCP from this machine as needed and dials in. Before calling, ask the user whether PowerShell 7 and PowerShell.MCP may be installed there for later use, and where (its install parameter: none / user / machine); default none installs nothing. Inbound on this machine must be allowed (Windows asks once).
- If PowerShell 7 and PowerShell.MCP (same version as here) are installed there: the user runs Enable-MCPRemoteControl in PowerShell 7, which prints a pairing code and host/IPs; call connect_remote_console with them. Needs inbound TCP 42777 open there.
Then use execute_remote_command / wait_for_remote_completion / cancel_remote_command; disconnect_remote_console ends it. Intended for a LAN or VPN, not the open Internet.";

        public static async Task Main(string[] args)
        {
            // For testing: Force Japanese locale (comment out after verification)
            //System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ja-JP");
            //System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fr-FR");
            // Opt-in: `--no-profile` makes the interactive launchers (Windows/macOS/Linux)
            // start pwsh with -NoProfile. Default off — those are real human-facing shells,
            // so the user's $PROFILE loads unless the operator asks for lean consoles.
            // (The headless/CI launcher always uses -NoProfile, independent of this flag.)
            PwshLauncherShared.SuppressProfileOnInteractive =
                Array.Exists(args, a => string.Equals(a, "--no-profile", StringComparison.OrdinalIgnoreCase));

            var builder = Host.CreateApplicationBuilder(args);

            builder.Logging.AddConsole(consoleLogOptions =>
            {
                // Configure all logs to go to stderr
                consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            builder.Services
                .AddSingleton<NamedPipeClient>()
                .AddSingleton<IPowerShellService, PowerShellService>()
                .AddSingleton<IPipeDiscoveryService, PipeDiscoveryService>()
                .AddSingleton<RemoteConsoleClient>();

            builder.Services
                .AddMcpServer(options => options.ServerInstructions = ServerInstructions)
                .WithStdioServerTransport()
                .WithTools<PowerShellTools>()
                .WithTools<RemoteConsoleTools>()
                .WithLocalizedPrompts<PowerShellPrompts>()
                .WithRequestFilters(filters =>
                {
                    filters.AddCallToolFilter(next => async (request, cancellationToken) =>
                    {
                        try
                        {
                            return await next(request, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not McpException and not OperationCanceledException)
                        {
                            throw new McpException(ex.Message, ex);
                        }
                    });
                });

            var host = builder.Build();

            await host.RunAsync();
        }
    }
}
