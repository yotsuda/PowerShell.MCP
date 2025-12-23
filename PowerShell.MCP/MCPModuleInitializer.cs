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
            // Check if another PowerShell.MCP server is already running
            // Try to connect to existing server - if successful, another instance is running
            bool pipeExists = false;
            try
            {
                using var testClient = new System.IO.Pipes.NamedPipeClientStream(".", NamedPipeServer.PipeName, System.IO.Pipes.PipeDirection.InOut);
                testClient.Connect(100); // 100ms timeout
                pipeExists = true;
            }
            catch (TimeoutException)
            {
                // No server listening - OK to start
            }
            catch (System.IO.IOException)
            {
                // Pipe/socket doesn't exist or connection failed - OK to start
            }

            if (pipeExists)
            {
                throw new InvalidOperationException("Another PowerShell.MCP server is already running. Only one instance is allowed per machine.");
            }

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
