using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Prompts;
using System.Reflection;

namespace PowerShell.MCP.Proxy
{
    public class Program
    {
        public static readonly string ProxyVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

        public static async Task Main(string[] args)
        {
            //Console.Error.WriteLine("[DEBUG] Starting PowerShell.MCP.Proxy...");

            var builder = Host.CreateApplicationBuilder(args);

            builder.Logging.AddConsole(consoleLogOptions =>
            {
                // Configure all logs to go to stderr
                consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            //Console.Error.WriteLine("[DEBUG] Configuring services...");
            builder.Services
                .AddSingleton<NamedPipeClient>()
                .AddSingleton<IPowerShellService, PowerShellService>();
                //.AddSingleton<ILocationTracker, LocationTracker>();

            //Console.Error.WriteLine("[DEBUG] Adding MCP server...");
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly()
                .WithPromptsFromAssembly(); // プロンプトを明示的に追加

            //Console.Error.WriteLine("[DEBUG] Building and running...");
            await builder.Build().RunAsync();
        }
    }
}
