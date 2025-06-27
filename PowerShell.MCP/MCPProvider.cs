using System.Management.Automation;
using System.Management.Automation.Provider;
using System.IO;
using System.Reflection;

namespace PowerShell.MCP
{
    /// <summary>
    /// Resources/ フォルダの埋め込みリソースを読み込むヘルパークラス
    /// </summary>
    public static class EmbeddedResourceLoader
    {
        /// <summary>
        /// Resources/ フォルダからPowerShellスクリプトを読み込む
        /// </summary>
        /// <param name="scriptFileName">スクリプトファイル名（例: "MCPPollingEngine.ps1"）</param>
        /// <returns>スクリプト内容</returns>
        public static string LoadScript(string scriptFileName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"PowerShell.MCP.Resources.{scriptFileName}";
            
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
                }
                
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
        
        /// <summary>
        /// 利用可能な埋め込みリソースの一覧を取得（デバッグ用）
        /// </summary>
        /// <returns>リソース名の配列</returns>
        public static string[] GetAvailableResources()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();
            var resources = new List<string>();
            
            foreach (var resourceName in resourceNames)
            {
                if (resourceName.StartsWith("PowerShell.MCP.Resources."))
                {
                    var shortName = resourceName.Replace("PowerShell.MCP.Resources.", "");
                    resources.Add(shortName);
                }
            }
            
            return resources.ToArray();
        }
    }

    [CmdletProvider("PowerShell.MCP", ProviderCapabilities.None)]
    public class MCPProvider : CmdletProvider
    {
        private CancellationTokenSource? _tokenSource;

        protected override ProviderInfo Start(ProviderInfo providerInfo)
        {
            var pi = base.Start(providerInfo);

            _tokenSource = new CancellationTokenSource();

            try
            {
                // MCPポーリングエンジンスクリプトの読み込みと実行
                var pollingScript = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
                this.InvokeCommand.InvokeScript(pollingScript);

                #if DEBUG
                // デバッグ時のリソース確認
                WriteVerbose($"[PowerShell.MCP] Available resources: {string.Join(", ", EmbeddedResourceLoader.GetAvailableResources())}");
                #endif
            }
            catch (FileNotFoundException ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "EmbeddedScriptNotFound",
                    ErrorCategory.ResourceUnavailable,
                    null));
                
                WriteWarning("[PowerShell.MCP] MCPPollingEngine.ps1 not found. Please check embedded resources.");
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "ScriptExecutionError",
                    ErrorCategory.InvalidOperation,
                    null));
                
                WriteWarning($"[PowerShell.MCP] Script execution error: {ex.Message}");
            }

            Task.Run(() =>
            {
                try
                {
                    McpServerHost.StartServer(this, _tokenSource.Token);
                }
                catch (Exception ex)
                {
                    WriteWarning($"[PowerShell.MCP] Failed to start Named Pipe server: {ex.Message}");
                }
            }, _tokenSource.Token);

            WriteInformation(
                "[PowerShell.MCP] MCP Named Pipe server started",
                ["PowerShell.MCP", "ServerStart"]
            );
            return pi;
        }

        protected override void Stop()
        {
            try
            {
                _tokenSource?.Cancel();
                _tokenSource?.Dispose();
                _tokenSource = null;
            }
            catch (Exception ex)
            {
                WriteWarning($"[PowerShell.MCP] Error during stop: {ex.Message}");
            }
            finally
            {
                base.Stop();
            }
        }
    }
}
