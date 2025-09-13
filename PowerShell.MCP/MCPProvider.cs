using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Reflection;
using System.Text.Json;
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

            using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
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
            // TODO: named pipe が既に存在する場合は、このプロバイダのインポートに失敗させる
            // 例外をスローする？ nullを返す？

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
                WriteInformation("[PowerShell.MCP] Requesting polling engine cleanup...", ["PowerShell.MCP", "Cleanup"]);

                // 埋め込みリソースからクリーンアップスクリプトを読み込み
                var cleanupCommand = EmbeddedResourceLoader.LoadScript("MCPCleanup.ps1");

                // メインスレッドでクリーンアップコマンドを実行
                McpServerHost.outputFromCommand = null;
                McpServerHost.executeCommandSilent = cleanupCommand;

                // 少し待機してクリーンアップの完了を待つ
                Thread.Sleep(1000);

                // 状態管理の終了処理
                ExecutionState.SetIdle();

                _tokenSource?.Cancel();
                _namedPipeServer?.Dispose();
                _tokenSource?.Dispose();
                _tokenSource = null;
                _namedPipeServer = null;

                WriteInformation("[PowerShell.MCP] MCP server stopped", ["PowerShell.MCP", "ServerStop"]);
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
        /// 現在の場所とドライブ情報を取得します
        /// </summary>
        public static string GetCurrentLocation()
        {
            try
            {
                // 既存のMCPLocationProvider.ps1スクリプトを使用
                var locationCommand = EmbeddedResourceLoader.LoadScript("MCPLocationProvider.ps1");

                // 状態管理付きでサイレント実行
                return McpServerHost.ExecuteSilentCommand(locationCommand);
            }
            catch (Exception ex)
            {
                return $"Error getting current location: {ex.Message}";
            }
        }

        /// <summary>
        /// 通知を専用 pipe でEXE側に送信
        /// </summary>
        //public static void SendNotification(object notificationData)
        //{
        //    Services.NamedPipeServer.SendNotificationToPipe(notificationData);
        //}
    }
}
