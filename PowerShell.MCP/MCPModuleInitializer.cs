using System.Management.Automation;
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

            using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// PowerShell.MCP モジュールの初期化クラス
    /// モジュールインポート時に自動的に実行される
    /// </summary>
    public class MCPModuleInitializer : IModuleAssemblyInitializer
    {
        private static CancellationTokenSource? _tokenSource;
        private static NamedPipeServer? _namedPipeServer;
        public static readonly string ServerVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

        public void OnImport()
        {
            // TODO: named pipe が既に存在する場合は、このモジュールのインポートに失敗させる
            // 例外をスローする？

            try
            {
                _tokenSource = new CancellationTokenSource();
                _namedPipeServer = new NamedPipeServer();

                // MCPポーリングエンジンスクリプトの読み込みと実行
                var pollingScript = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
                
                // PowerShell から実行するため、ScriptBlock として実行
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript(pollingScript);
                    ps.Invoke();
                }

                // Named Pipe Server の起動
                Task.Run(async () =>
                {
                    await _namedPipeServer.StartAsync(_tokenSource.Token);
                }, _tokenSource.Token);

                // 情報メッセージの出力
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript("Write-Information '[PowerShell.MCP] MCP server started' -Tags 'PowerShell.MCP','ServerStart'");
                    ps.Invoke();
                }
            }
            catch (Exception ex)
            {
                // 警告メッセージの出力
                using (var ps = System.Management.Automation.PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    ps.AddScript($"Write-Warning '[PowerShell.MCP] Failed to start: {ex.Message}'");
                    ps.Invoke();
                }
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
    }
}
