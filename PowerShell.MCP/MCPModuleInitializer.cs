using System.Management.Automation;
using System.Reflection;
using PowerShell.MCP.Services;

namespace PowerShell.MCP
{
    /// <summary>
    /// Helper class for loading embedded resources from the Resources/ folder
    /// </summary>
    public static class EmbeddedResourceLoader
    {
        public static string LoadScript(string scriptFileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyName = assembly.GetName().Name;
            var resourceName = $"{assemblyName}.Resources.{scriptFileName}";

            using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// Initialization class for PowerShell.MCP module
    /// Automatically executed when the module is imported
    /// </summary>
    public class MCPModuleInitializer : IModuleAssemblyInitializer
    {
        private static CancellationTokenSource? _tokenSource;
        private static NamedPipeServer? _namedPipeServer;
        public static readonly string ServerVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

        public void OnImport()
        {
            // TODO: Fail module import if named pipe already exists
            // Throw exception?

            try
            {
                _tokenSource = new CancellationTokenSource();
                _namedPipeServer = new NamedPipeServer();

                // Load and execute MCP polling engine script
                var pollingScript = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
                
                // Execute as ScriptBlock for PowerShell execution
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript(pollingScript);
                    ps.Invoke();
                }

                // Start Named Pipe Server with error handling
                Console.Error.WriteLine("[DEBUG] Starting Named Pipe server task...");
                Task.Run(async () =>
                {
                    try
                    {
                        Console.Error.WriteLine($"[DEBUG] Named Pipe server task started on thread {Environment.CurrentManagedThreadId}");
                        Console.Error.WriteLine($"[DEBUG] Pipe name: {NamedPipeServer.PipeName}");
                        await _namedPipeServer.StartAsync(_tokenSource.Token);
                        Console.Error.WriteLine("[DEBUG] Named Pipe server StartAsync completed");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[ERROR] Named Pipe server failed: {ex.GetType().Name}: {ex.Message}");
                        Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                    }
                }, _tokenSource.Token);

                // Output information message
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript("Write-Information '[PowerShell.MCP] MCP server started' -Tags 'PowerShell.MCP','ServerStart'");
                    ps.Invoke();
                }
            }
            catch (Exception ex)
            {
                // Output warning message
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript($"Write-Warning '[PowerShell.MCP] Failed to start: {ex.Message}'");
                    ps.Invoke();
                }
            }
        }

        /// <summary>
        /// Gets current location and drive information
        /// </summary>
        public static string GetCurrentLocation()
        {
            try
            {
                // Use existing MCPLocationProvider.ps1 script
                var locationCommand = EmbeddedResourceLoader.LoadScript("MCPLocationProvider.ps1");

                // Execute silently with state management
                return McpServerHost.ExecuteSilentCommand(locationCommand);
            }
            catch (Exception ex)
            {
                return $"Error getting current location: {ex.Message}";
            }
        }
    }
}
