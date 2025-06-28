using System.Management.Automation;
using System.Management.Automation.Provider;
using System.IO;
using System.Reflection;

namespace PowerShell.MCP
{
    /// <summary>
    /// Resources/ フォルダの埋め込みリソースを読み込むヘルパークラス（改善版）
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
            var assemblyName = assembly.GetName().Name;
            var resourceName = $"{assemblyName}.Resources.{scriptFileName}";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
            }
            
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    [CmdletProvider("PowerShell.MCP", ProviderCapabilities.None)]
    public class MCPProvider : CmdletProvider
    {
        private CancellationTokenSource? _tokenSource;
        private McpConfiguration? _configuration;

        protected override ProviderInfo Start(ProviderInfo providerInfo)
        {
            var pi = base.Start(providerInfo);

            try
            {
                // 基本設定の初期化
                _configuration = new McpConfiguration();
                _tokenSource = new CancellationTokenSource();

                // MCPポーリングエンジンスクリプトの読み込みと実行
                var pollingScript = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
                this.InvokeCommand.InvokeScript(pollingScript);

                // MCPサーバーの堅牢な起動（例外で終了しない）
                Task.Run(async () =>
                {
                    McpServerHost.StartServer(this, _tokenSource.Token);
                    // サーバーが正常に動作している間は無限ループ
                    await Task.Delay(Timeout.Infinite, _tokenSource.Token);
                }, _tokenSource.Token);

                WriteInformation("[PowerShell.MCP] MCP server started", ["PowerShell.MCP", "ServerStart"]);
            }
            catch (Exception ex)
            {
                WriteWarning($"[PowerShell.MCP] Failed to start: {ex.Message}");
            }

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
