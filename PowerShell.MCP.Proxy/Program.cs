using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PowerShell.MCP.Proxy.Services;
using System.Reflection;

namespace PowerShell.MCP.Proxy
{
    public class Program
    {
        public static readonly string ProxyVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

        public static async Task Main(string[] args)
        {
            // テスト用：日本語に強制（検証後はコメントアウトしてください）
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ja-JP");
            
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
                .WithLocalizedPromptsFromAssembly(Assembly.GetExecutingAssembly()); // Use localized prompts

            //Console.Error.WriteLine("[DEBUG] Building and running...");
            await builder.Build().RunAsync();
        }
    }
}
