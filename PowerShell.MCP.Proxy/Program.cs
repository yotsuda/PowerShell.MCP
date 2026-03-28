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

        public static async Task Main(string[] args)
        {
            // For testing: Force Japanese locale (comment out after verification)
            //System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ja-JP");
            //System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fr-FR");
            var builder = Host.CreateApplicationBuilder(args);

            builder.Logging.AddConsole(consoleLogOptions =>
            {
                // Configure all logs to go to stderr
                consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            builder.Services
                .AddSingleton<NamedPipeClient>()
                .AddSingleton<IPowerShellService, PowerShellService>()
                .AddSingleton<IPipeDiscoveryService, PipeDiscoveryService>();

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools<PowerShellTools>()
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
