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
                // Read proxy PID from global variable (set by PowerShell.MCP.Proxy before Import-Module)
                int? proxyPid = null;
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript("$global:PowerShellMCPProxyPid");
                    var result = ps.Invoke();
                    if (result.Count > 0 && result[0]?.BaseObject is int pid)
                    {
                        proxyPid = pid;
                    }
                }

                // Create Named Pipe server with proxy PID (if available) and pwsh PID
                _namedPipeServer = new NamedPipeServer(proxyPid);
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
                var result = McpServerHost.ExecuteSilentCommand(locationCommand);

                // Replace "Pipeline" with "get_current_location" in status line
                return result.Replace("Pipeline executed", "get_current_location executed");
            }
            catch (Exception ex)
            {
                return $"Error getting current location: {ex.Message}";
            }
        }
    /// <summary>
    /// Claims this console for a specific proxy by restarting the Named Pipe server with new name.
    /// Called when a proxy connects to an unowned console.
    /// </summary>
    /// <param name="proxyPid">The PID of the proxy claiming this console</param>
    /// <returns>The new pipe name after claiming</returns>
    public static string? ClaimConsole(int proxyPid)
    {
        if (_namedPipeServer == null || _tokenSource == null)
            return null;

        try
        {
            // Stop current server
            _tokenSource.Cancel();
            _namedPipeServer.Dispose();

            // Create new server with proxy PID
            _namedPipeServer = new NamedPipeServer(proxyPid);
            _tokenSource = new CancellationTokenSource();

            // Start new server
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

            return _namedPipeServer.PipeName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the current pipe name for this console.
    /// </summary>
    /// <returns>The pipe name, or null if not initialized</returns>
    public static string? GetPipeName()
    {
        return _namedPipeServer?.PipeName;
    }
    }
}
