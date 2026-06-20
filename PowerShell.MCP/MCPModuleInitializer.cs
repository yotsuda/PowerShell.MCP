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
        private static readonly object _serverLock = new();
        private static CancellationTokenSource? _tokenSource;
        private static NamedPipeServer? _namedPipeServer;
        public static readonly string ServerVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

        /// <summary>
        /// True once the embedded polling engine has been loaded and started on
        /// the runspace. Stays false if startup was blocked (typically a
        /// transient AMSI/antivirus false positive on the embedded script).
        /// Read from the pipe-server threads to fast-fail commands instead of
        /// hanging until timeout when the engine is down.
        /// </summary>
        public static volatile bool EngineReady;

        // Last exception from a failed engine start (AMSI block, etc.), surfaced
        // by Restart-MCPServer and the engine-not-ready command response.
        private static Exception? _lastEngineError;
        public static string? LastEngineErrorMessage => _lastEngineError?.Message;

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

                // Read agent ID from global variable (set by PowerShell.MCP.Proxy before Import-Module)
                string? agentId = null;
                using (var ps2 = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps2.AddScript("$global:PowerShellMCPAgentId");
                    var agentResult = ps2.Invoke();
                    if (agentResult.Count > 0 && agentResult[0]?.BaseObject is string aid)
                    {
                        agentId = aid;
                    }
                }

                lock (_serverLock)
                {
                    // Clean up existing server if module is being re-imported (e.g., profile loaded it first)
                    if (_namedPipeServer != null)
                    {
                        try
                        {
                            _tokenSource?.Cancel();
                            _namedPipeServer.Dispose();
                        }
                        catch { }
                        _namedPipeServer = null;
                        _tokenSource = null;
                    }

                    // Create Named Pipe server with proxy PID, agent ID (if available), and pwsh PID
                    _namedPipeServer = new NamedPipeServer(proxyPid, agentId);
                    _tokenSource = new CancellationTokenSource();
                }

                // Set initial window title for unowned consoles (Proxy-launched consoles get titled immediately after)
                if (!proxyPid.HasValue)
                {
                    using var psTitle = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace);
                    psTitle.AddScript($"$Host.UI.RawUI.WindowTitle = '#{Environment.ProcessId} ____'");
                    psTitle.Invoke();
                }

                // Load and start the MCP polling engine. Isolated in
                // TryStartEngine so Restart-MCPServer reuses the exact same path,
                // and so a startup block (typically a transient AMSI/antivirus
                // false positive on the embedded script) degrades gracefully:
                // the module stays loaded, the pipe server still starts below,
                // and the user can retry — instead of the whole import dying.
                if (!TryStartEngine())
                {
                    var detail = (_lastEngineError?.Message ?? "unknown error").Replace("'", "''");
                    using var psWarn = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace);
                    psWarn.AddScript(
                        "Write-Warning '[PowerShell.MCP] Console engine failed to start: " + detail +
                        ". Often a transient antivirus/AMSI false positive. The module is loaded but " +
                        "commands are unavailable until it starts — run ''Restart-MCPServer'' to retry. " +
                        "If it persists, see https://github.com/yotsuda/PowerShell.MCP (it may be " +
                        "Constrained Language Mode / WDAC or an AV policy).'");
                    psWarn.Invoke();
                }

                // Start Named Pipe Server
                CancellationToken token;
                NamedPipeServer server;
                lock (_serverLock)
                {
                    token = _tokenSource!.Token;
                    server = _namedPipeServer!;
                }
                Task.Run(async () =>
                {
                    try
                    {
                        await server.StartAsync(token);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[ERROR] Named Pipe server failed: {ex.Message}");
                    }
                }, token);
            }
            catch (Exception ex)
            {
                // Output warning message for errors
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript($"Write-Warning '[PowerShell.MCP] Failed to start: {ex.Message.Replace("'", "''")}'");
                    ps.Invoke();
                }
            }
        }

        /// <summary>
        /// Loads and starts the embedded MCP polling engine on the CURRENT
        /// runspace (home thread). Uses ScriptBlock invocation, which runs
        /// in-context — NOT a nested Runspace pipeline — so it is equally safe
        /// to call from OnImport and from a function's execution thread
        /// (Restart-MCPServer). ScriptBlock.Create triggers the AMSI content
        /// scan, so an antivirus block surfaces as a caught exception here and
        /// leaves EngineReady false rather than tearing down the module. The
        /// engine script is idempotent (its setup block no-ops when
        /// $global:McpTimer already exists), so calling this while already
        /// running is harmless.
        /// </summary>
        /// <returns>True if the engine is running after this call.</returns>
        public static bool TryStartEngine()
        {
            try
            {
                var pollingScript = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
                ScriptBlock.Create(pollingScript).Invoke();
                EngineReady = true;
                _lastEngineError = null;
                return true;
            }
            catch (Exception ex)
            {
                EngineReady = false;
                _lastEngineError = ex;
                return false;
            }
        }

        /// <summary>
        /// Message returned when a command arrives while the engine is not
        /// running. Centralized so the pipe-server fast-fail and any caller
        /// surface identical, actionable guidance.
        /// </summary>
        public static string GetEngineNotReadyMessage()
        {
            const string baseMsg =
                "[PowerShell.MCP] The console engine is not running — startup was blocked, " +
                "usually a transient antivirus/AMSI false positive. Run 'Restart-MCPServer' in " +
                "this console to retry. If it persists it may be Constrained Language Mode / WDAC " +
                "or an AV policy — see https://github.com/yotsuda/PowerShell.MCP.";
            var detail = _lastEngineError?.Message;
            return string.IsNullOrEmpty(detail) ? baseMsg : baseMsg + " Last error: " + detail;
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
        /// <param name="agentId">Agent ID for console isolation</param>
        /// <returns>The new pipe name after claiming</returns>
        public static string? ClaimConsole(int proxyPid, string? agentId = null)
        {
            lock (_serverLock)
            {
                if (_namedPipeServer == null || _tokenSource == null)
                    return null;

                try
                {
                    // Stop current server
                    _tokenSource.Cancel();
                    _namedPipeServer.Dispose();

                    // Create new server with proxy PID and agent ID
                    _namedPipeServer = new NamedPipeServer(proxyPid, agentId);
                    _tokenSource = new CancellationTokenSource();

                    // Capture for closure
                    var server = _namedPipeServer;
                    var token = _tokenSource.Token;

                    // Start new server
                    Task.Run(async () =>
                    {
                        try
                        {
                            await server.StartAsync(token);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[ERROR] Named Pipe server failed (ClaimConsole): {ex.Message}");
                        }
                    }, token);

                    return _namedPipeServer.PipeName;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] ClaimConsole failed: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Releases this console from proxy ownership by restarting the Named Pipe server as unowned.
        /// Called when the owning proxy process is detected as dead.
        /// </summary>
        /// <returns>The new unowned pipe name, or null on failure</returns>
        public static string? ReleaseConsole()
        {
            lock (_serverLock)
            {
                if (_namedPipeServer == null || _tokenSource == null)
                    return null;

                try
                {
                    // Stop current server
                    _tokenSource.Cancel();
                    _namedPipeServer.Dispose();

                    // Create new server without proxy PID (unowned: 2-segment pipe name)
                    _namedPipeServer = new NamedPipeServer(null);
                    _tokenSource = new CancellationTokenSource();

                    // Capture for closure
                    var server = _namedPipeServer;
                    var token = _tokenSource.Token;

                    // Start new server
                    Task.Run(async () =>
                    {
                        try
                        {
                            await server.StartAsync(token);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[ERROR] Named Pipe server failed (ReleaseConsole): {ex.Message}");
                        }
                    }, token);

                    return _namedPipeServer.PipeName;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] ReleaseConsole failed: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Gets the current pipe name for this console.
        /// </summary>
        /// <returns>The pipe name, or null if not initialized</returns>
        public static string? GetPipeName()
        {
            lock (_serverLock)
            {
                return _namedPipeServer?.PipeName;
            }
        }
    }
}
