using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Reflection;
using PowerShell.MCP.Services;

namespace PowerShell.MCP
{
    /// <summary>
    /// Resources/ フォルダの埋め込みリソースを読み込むヘルパークラス
    /// </summary>
    public static class EmbeddedResourceLoader
    {
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
        private NamedPipeServer? _namedPipeServer;

        protected override ProviderInfo Start(ProviderInfo providerInfo)
        {
            var pi = base.Start(providerInfo);

            try
            {
                _tokenSource = new CancellationTokenSource();
                _namedPipeServer = new NamedPipeServer(this);
                
                // MCPポーリングエンジンスクリプトの読み込みと実行
                var pollingScript = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
                this.InvokeCommand.InvokeScript(pollingScript);

                // Named Pipe Server の起動
                Task.Run(async () =>
                {
                    await _namedPipeServer.StartAsync(_tokenSource.Token);
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
                _namedPipeServer?.Dispose();
                _tokenSource?.Dispose();
                _tokenSource = null;
                _namedPipeServer = null;
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

        /// <summary>
        /// PowerShellコマンドを実行します
        /// </summary>
        public static string ExecuteCommand(string command, bool executeImmediately)
        {
            try
            {
                // 結果をクリア
                McpServerHost.outputFromCommand = null;
                
                if (executeImmediately)
                {
                    // 元のMCPPollingEngineの仕組みを直接使用
                    McpServerHost.executeCommand = command;
                }
                else
                {
                    // コマンドをコンソールに挿入
                    McpServerHost.insertCommand = command;
                }

                // 結果を待機（最大30秒）
                return PowerShellCommunication.WaitForResult(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                return $"Error executing command: {ex.Message}";
            }
        }

        /// <summary>
        /// 現在の場所とドライブ情報を取得します
        /// 元の MCPServerHost.HandleGetCurrentLocation() と同じ実装パターン
        /// </summary>
        public static string GetCurrentLocation()
        {
            try
            {
                // 既存のMCPLocationProvider.ps1スクリプトを使用
                var locationCommand = EmbeddedResourceLoader.LoadScript("MCPLocationProvider.ps1");

                // 結果をクリアしてからコマンドを設定（元の実装と同じパターン）
                McpServerHost.outputFromCommand = null;
                McpServerHost.executeCommandSilent = locationCommand;

                // 結果を待機（最大10秒 - 元の実装と同じタイムアウト）
                const int timeoutMs = 10000;
                var elapsed = 0;
                const int pollIntervalMs = 100; // ポーリング間隔

                while (elapsed < timeoutMs)
                {
                    if (McpServerHost.outputFromCommand != null)
                    {
                        var output = McpServerHost.outputFromCommand;
                        McpServerHost.outputFromCommand = null; // 使用後はクリア
                        return output;
                    }

                    Thread.Sleep(pollIntervalMs);
                    elapsed += pollIntervalMs;
                }

                return "Timeout while getting current location";
            }
            catch (Exception ex)
            {
                return $"Error getting current location: {ex.Message}";
            }
        }
    }
}
