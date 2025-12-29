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

        private enum RegistrationResult
        {
            Registered,         // Proxy registered this console
            Rejected,           // Ready console already exists
            ProxyNotAvailable   // Proxy is not running
        }

        public void OnImport()
        {
            try
            {
                // First, try to register with Proxy
                var registrationResult = TryRegisterWithProxy();
                
                if (registrationResult == RegistrationResult.Rejected)
                {
                    throw new InvalidOperationException("Another Ready PowerShell.MCP console is already running. Import cancelled to avoid confusion.");
                }
                
                bool usePidSuffix;
                if (registrationResult == RegistrationResult.Registered)
                {
                    // Proxy registered this console with PID-based pipe name
                    usePidSuffix = true;
                }
                else
                {
                    // Proxy not available - check for existing Ready console ourselves
                    if (IsExistingConsoleReady())
                    {
                        throw new InvalidOperationException("Another Ready PowerShell.MCP console is already running. Import cancelled to avoid confusion.");
                    }
                    usePidSuffix = false;
                }
                
                // Create Named Pipe server
                _namedPipeServer = new NamedPipeServer(usePidSuffix: usePidSuffix);
                
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
                    throw new InvalidOperationException("Another PowerShell.MCP server is already running with the same pipe name.");
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
            catch (InvalidOperationException ex) when (ex.Message.Contains("Ready PowerShell.MCP console"))
            {
                // Re-throw to fail the import - user should not have two Ready consoles
                throw;
            }
            catch (Exception ex)
            {
                // Output warning message for other errors
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
        private static RegistrationResult TryRegisterWithProxy()
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
                    return RegistrationResult.ProxyNotAvailable;
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
                        
                        if (ack == "OK")
                            return RegistrationResult.Registered;
                        if (ack == "REJECT")
                            return RegistrationResult.Rejected;
                    }
                }
                
                return RegistrationResult.ProxyNotAvailable;
            }
            catch (Exception)
            {
                return RegistrationResult.ProxyNotAvailable;
            }
        }

        /// <summary>
        /// Checks if an existing console is Ready (not Busy)
        /// Used when Proxy is not available
        /// </summary>
        private static bool IsExistingConsoleReady()
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", NamedPipeServer.BasePipeName, PipeDirection.InOut);
                
                try
                {
                    pipeClient.Connect(500); // 500ms timeout
                }
                catch (TimeoutException)
                {
                    // No existing console
                    return false;
                }
                catch (IOException)
                {
                    // Pipe doesn't exist
                    return false;
                }
                
                // Send get_status request (lightweight, doesn't use main runspace)
                var request = "{\"name\":\"get_status\"}";
                var requestBytes = Encoding.UTF8.GetBytes(request);
                var lengthBytes = BitConverter.GetBytes(requestBytes.Length);
                
                pipeClient.Write(lengthBytes, 0, lengthBytes.Length);
                pipeClient.Write(requestBytes, 0, requestBytes.Length);
                pipeClient.Flush();
                
                // Read response
                var responseLengthBuffer = new byte[4];
                if (pipeClient.Read(responseLengthBuffer, 0, 4) == 4)
                {
                    var responseLength = BitConverter.ToInt32(responseLengthBuffer, 0);
                    if (responseLength > 0 && responseLength < 1000)
                    {
                        var responseBuffer = new byte[responseLength];
                        pipeClient.Read(responseBuffer, 0, responseLength);
                        var response = Encoding.UTF8.GetString(responseBuffer);
                        
                        // If response contains "Busy", existing console is busy - OK to import
                        // Otherwise, existing console is Ready - don't import
                        return !response.Contains("| Status: Busy |");
                    }
                }
                
                // Couldn't determine status, assume not ready
                return false;
            }
            catch (Exception)
            {
                // Error checking - assume no ready console
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
