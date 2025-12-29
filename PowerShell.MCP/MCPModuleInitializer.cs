using System.Management.Automation;
using System.Reflection;
using System.IO.Pipes;
using System.Text;
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
        private const string RegistrationPipeName = "PowerShell.MCP.Registration";
        
        private static CancellationTokenSource? _tokenSource;
        private static NamedPipeServer? _namedPipeServer;
        public static readonly string ServerVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

        public void OnImport()
        {
            try
            {
                // First, try to register with Proxy to determine pipe name
                bool registeredWithProxy = TryRegisterWithProxy();
                
                // Determine pipe name based on registration result
                // If registered with proxy, use PID suffix; otherwise use default name
                _namedPipeServer = new NamedPipeServer(usePidSuffix: registeredWithProxy);
                
                // Check if another PowerShell.MCP server is already running with the same pipe name
                bool pipeExists = false;
                try
                {
                    using var testClient = new NamedPipeClientStream(".", _namedPipeServer.PipeName, PipeDirection.InOut);
                    testClient.Connect(100); // 100ms timeout
                    pipeExists = true;
                }
                catch (TimeoutException)
                {
                    // No server listening - OK to start
                }
                catch (IOException)
                {
                    // Pipe/socket doesn't exist or connection failed - OK to start
                }

                if (pipeExists)
                {
                    throw new InvalidOperationException("Another PowerShell.MCP server is already running. Only one instance is allowed per machine.");
                }

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
                // Output warning message
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript($"Write-Warning '[PowerShell.MCP] Failed to start: {ex.Message}'");
                    ps.Invoke();
                }
            }
        }

        /// <summary>
        /// Tries to register this PS module instance with the Proxy
        /// </summary>
        /// <returns>True if registration succeeded, false if proxy is not available</returns>
        private static bool TryRegisterWithProxy()
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", RegistrationPipeName, PipeDirection.InOut);
                
                // Try to connect with short timeout - proxy may not be running
                try
                {
                    pipeClient.Connect(1000); // 1 second timeout
                }
                catch (TimeoutException)
                {
                    // Proxy registration server not available
                    // User may have imported module manually without proxy
                    return false;
                }
                
                // Send registration message: "REGISTER:<PID>"
                var pid = Environment.ProcessId;
                var message = $"REGISTER:{pid}";
                var messageBytes = Encoding.UTF8.GetBytes(message);
                var lengthBytes = BitConverter.GetBytes(messageBytes.Length);
                
                pipeClient.Write(lengthBytes, 0, lengthBytes.Length);
                pipeClient.Write(messageBytes, 0, messageBytes.Length);
                pipeClient.Flush();
                
                // Wait for acknowledgment
                var ackLengthBuffer = new byte[4];
                if (pipeClient.Read(ackLengthBuffer, 0, 4) == 4)
                {
                    var ackLength = BitConverter.ToInt32(ackLengthBuffer, 0);
                    if (ackLength > 0 && ackLength < 100)
                    {
                        var ackBuffer = new byte[ackLength];
                        pipeClient.Read(ackBuffer, 0, ackLength);
                        var ack = Encoding.UTF8.GetString(ackBuffer);
                        return ack == "OK";
                    }
                }
                
                return false;
            }
            catch (Exception)
            {
                // Silently ignore registration errors
                return false;
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
