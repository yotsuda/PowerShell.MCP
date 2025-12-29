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
            try
            {
                // Create Named Pipe server with PID suffix (always use PID for uniqueness)
                _namedPipeServer = new NamedPipeServer(usePidSuffix: true);
                _tokenSource = new CancellationTokenSource();

                // Load and execute MCP polling engine script
                var pollingScript = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");

                // Execute as ScriptBlock for PowerShell execution
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript(pollingScript);
                    ps.Invoke();
                }

                // Start Named Pipe Server
                Task.Run(async () =>
                {
                    try
                    {
                        await _namedPipeServer.StartAsync(_tokenSource.Token);
                    }
                    catch (Exception)
                    {
                        // Silently ignore Named Pipe server errors
                    }
                }, _tokenSource.Token);
            }
            catch (Exception ex)
            {
                // Output warning message for errors
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
