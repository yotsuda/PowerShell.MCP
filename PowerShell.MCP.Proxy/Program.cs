using System.Text.Json;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.IO.Pipes;

namespace PowerShell.MCP.Proxy
{
    internal class Program
    {
        // Named Pipe設定
        private const string PIPE_NAME = "PowerShell.MCP.Communication";
        private const int PIPE_BUFFER_SIZE = 8192;
        private const int PIPE_TIMEOUT_MS = 10000; // 10秒に増加
        private const int MAX_RETRIES = 3;

        // バージョン管理（起動時に一度だけ取得）
        private static readonly Version _clientVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

        // テンプレート処理用のメソッド定義
        private static readonly HashSet<string> TemplateMethods =
        [
            "initialize", "tools/list", "resources/list", "prompts/list"
        ];

        private static string ReturnTemplate(string method, int id)
        {
            try
            {
                // 実行アセンブリのパスとテンプレートフォルダーへのパスを組み立て
                var module_exe_path = Assembly.GetExecutingAssembly().Location;
                var module_dir = Path.GetDirectoryName(module_exe_path)!;
                var template_name = method.Replace('/', '_') + ".json";

                var template_path = Path.GetFullPath(Path.Combine(module_dir, @"..\Templates", template_name));

                // セキュリティ: パストラバーサル攻撃の防止
                var allowedDir = Path.GetFullPath(Path.Combine(module_dir, @"..\Templates"));
                if (!template_path.StartsWith(allowedDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Invalid template path");
                }

                // ファイル読み込み
                if (!File.Exists(template_path))
                {
                    throw new FileNotFoundException($"Template not found: {template_name}");
                }

                var content = File.ReadAllText(template_path, Encoding.UTF8);
                return content.Replace("{0}", id.ToString());
            }
            catch (Exception ex)
            {
                LogEvent("TEMPLATE_ERROR", $"Template error: {ex.Message}");
                return CreateErrorResponse(id, $"Template error: {ex.Message}");
            }
        }

        static async Task Main(string[] args)
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

            LogEvent("PROXY_START", $"PowerShell.MCP.Proxy started (Version: {_clientVersion})");

            while (true)
            {
                try
                {
                    string? requestJson = await ReadRequestAsync(reader);
                    if (requestJson == null) continue;

                    var (method, id, isValid) = ParseJsonRpcRequest(requestJson);
                    if (!isValid || ShouldSkipMethod(method)) continue;

                    string? responseJson = null;
                    if (TemplateMethods.Contains(method))
                    {
                        // テンプレートをローカルで処理
                        responseJson = ReturnTemplate(method, id)
                            .Replace("\n", "")
                            .Replace("\r", "");
                    }
                    else if (method == "tools/call")
                    {
                        // Named Pipe経由でサーバーに送信
                        responseJson = await ProcessRequestViaPipeAsync(requestJson, id);
                    }
                    else
                    {
                        // 未知のメソッド
                        responseJson = CreateErrorResponse(id, "Method not found");
                    }

                    if (responseJson != null)
                    {
                        await writer.WriteLineAsync(responseJson);
                    }
                }
                catch (Exception ex)
                {
                    LogEvent("MAIN_LOOP_ERROR", $"Main loop error: {ex.Message}");
                    // メインループでのエラーの場合、短時間待機してから継続
                    await Task.Delay(1000);
                }
            }
        }

        private static async Task<string?> ReadRequestAsync(StreamReader reader)
        {
            try
            {
                string? requestJson = await reader.ReadLineAsync();
                if (requestJson == null)
                {
                    await Task.Delay(500);
                    return null;
                }

                requestJson = requestJson.Trim();
                return string.IsNullOrEmpty(requestJson) ? null : requestJson;
            }
            catch (Exception ex)
            {
                LogEvent("READ_ERROR", $"Error reading request: {ex.Message}");
                return null;
            }
        }

        private static (string method, int id, bool isValid) ParseJsonRpcRequest(string requestJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(requestJson);
                var root = doc.RootElement;

                // method を取得
                var method = root.GetProperty("method").GetString() ?? "";

                // id がなければ or Number 以外なら isValid=false
                if (!root.TryGetProperty("id", out var idElem) || idElem.ValueKind != JsonValueKind.Number)
                    return (method, default, false);

                // 数値として取得
                int id = idElem.GetInt32();

                return (method, id, true);
            }
            catch (JsonException ex)
            {
                LogEvent("JSON_PARSE_ERROR", $"JSON parse error: {ex.Message}");
                return ("", default, false);
            }
        }

        private static bool ShouldSkipMethod(string method)
        {
            // 通知メソッドはスキップ
            return method.StartsWith("notifications/");
        }

        /// <summary>
        /// Named Pipe経由でリクエストを処理（修正版）
        /// </summary>
        private static async Task<string> ProcessRequestViaPipeAsync(string requestJson, int id)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                NamedPipeClientStream? pipeClient = null;

                try
                {
                    pipeClient = CreatePipeClient();

                    LogEvent("PIPE_CONNECTING", $"Attempting to connect to Named Pipe server (attempt {attempt}/{MAX_RETRIES})");

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(PIPE_TIMEOUT_MS));

                    // サーバーに接続
                    await pipeClient.ConnectAsync(timeoutCts.Token);

                    if (!pipeClient.IsConnected)
                    {
                        throw new InvalidOperationException("Failed to connect to Named Pipe server");
                    }

                    // 重要: ReadModeをMessageに設定
                    pipeClient.ReadMode = PipeTransmissionMode.Message;

                    LogEvent("PIPE_CONNECTED", "Successfully connected to Named Pipe server");

                    // リクエストを送信
                    await SendPipeMessageAsync(pipeClient, requestJson, timeoutCts.Token);

                    // レスポンスを受信
                    var response = await ReceivePipeMessageAsync(pipeClient, timeoutCts.Token);

                    LogEvent("PIPE_SUCCESS", "Request processed successfully via Named Pipe");

                    return response;
                }
                catch (OperationCanceledException) when (attempt < MAX_RETRIES)
                {
                    lastException = new TimeoutException($"Pipe connection timeout (attempt {attempt}/{MAX_RETRIES})");
                    LogEvent("PIPE_TIMEOUT", lastException.Message);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("does not exist") && attempt < MAX_RETRIES)
                {
                    lastException = ex;
                    LogEvent("PIPE_NOT_FOUND", "Named Pipe server not found, retrying...");
                }
                catch (UnauthorizedAccessException ex) when (attempt < MAX_RETRIES)
                {
                    lastException = ex;
                    LogEvent("PIPE_ACCESS_DENIED", "Access denied to Named Pipe, retrying...");
                }
                catch (IOException ex) when (attempt < MAX_RETRIES)
                {
                    lastException = ex;
                    LogEvent("PIPE_IO_ERROR", $"IO error: {ex.Message}, retrying...");
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    LogEvent("PIPE_ERROR", $"Unexpected error: {ex.Message}");
                    break; // 予期しないエラーの場合はリトライしない
                }
                finally
                {
                    try
                    {
                        pipeClient?.Dispose();
                    }
                    catch
                    {
                        // Disposeエラーは無視
                    }
                }

                // 最後の試行でない場合は待機
                if (attempt < MAX_RETRIES)
                {
                    var delay = TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt - 1)); // 指数バックオフ
                    LogEvent("PIPE_RETRY_DELAY", $"Waiting {delay.TotalSeconds} seconds before retry");
                    await Task.Delay(delay);
                }
            }

            // すべてのリトライが失敗した場合
            LogEvent("PIPE_ALL_RETRIES_FAILED", $"All connection attempts failed. Last error: {lastException?.Message}");

            // サーバーが動作していない場合の判定を改善
            if (IsServerNotRunningError(lastException))
            {
                // 期待する形式のレスポンスを返す
                return CreatePowerShellNotRunningResponse(id);
            }
            else
            {
                // その他のエラー（アクセス権限など）
                return CreateSecurityErrorResponse(id, lastException?.Message ?? "Unknown error");
            }
        }

        /// <summary>
        /// サーバーが動作していないエラーかどうかを判定
        /// </summary>
        private static bool IsServerNotRunningError(Exception? exception)
        {
            if (exception == null) return false;

            return exception is TimeoutException ||
                   exception is OperationCanceledException ||
                   exception.Message.Contains("does not exist") ||
                   exception.Message.Contains("operation was canceled") ||
                   exception.Message.Contains("pipe is not available") ||
                   exception.Message.Contains("All pipe instances are busy");
        }

        /// <summary>
        /// パイプクライアントを作成（修正版）
        /// </summary>
        private static NamedPipeClientStream CreatePipeClient()
        {
            return new NamedPipeClientStream(
                ".",                    // ローカルサーバー
                PIPE_NAME,              // パイプ名
                PipeDirection.InOut,    // 双方向
                PipeOptions.None        // 基本オプション（メッセージモードは接続後に設定）
            );
        }

        /// <summary>
        /// パイプにメッセージを送信（修正版）
        /// </summary>
        private static async Task SendPipeMessageAsync(NamedPipeClientStream pipeClient, string message, CancellationToken cancellationToken)
        {
            var buffer = Encoding.UTF8.GetBytes(message);

            await pipeClient.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
            await pipeClient.FlushAsync(cancellationToken);

            LogEvent("PIPE_MESSAGE_SENT", $"Sent message: {message.Length} bytes");
        }

        /// <summary>
        /// パイプからメッセージを受信
        /// </summary>
        private static async Task<string> ReceivePipeMessageAsync(NamedPipeClientStream pipeClient, CancellationToken cancellationToken)
        {
            var buffer = new byte[PIPE_BUFFER_SIZE];
            using var ms = new MemoryStream();

            do
            {
                var bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                ms.Write(buffer, 0, bytesRead);

                // メッセージサイズ制限（1MB）
                if (ms.Length > 1024 * 1024)
                {
                    throw new InvalidOperationException("Response message too large");
                }

            } while (!pipeClient.IsMessageComplete);

            var response = Encoding.UTF8.GetString(ms.ToArray());
            LogEvent("PIPE_MESSAGE_RECEIVED", $"Received message: {response.Length} bytes");

            return response;
        }

        /// <summary>
        /// PowerShellが動作していない場合のレスポンス生成
        /// </summary>
        private static string CreatePowerShellNotRunningResponse(int id)
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = "It looks like PowerShell isn't running. Please do the following:\n" +
                                   "1. Press Win+R to open the Run dialog.\n" +
                                   "2. Type `pwsh` and press Enter to launch PowerShell 7.\n" +
                                   "3. In the PowerShell window, paste this command and press Enter:\n" +
                                   "   Import-Module PowerShell.MCP, PSReadLine"
                        }
                    }
                }
            });
        }

        /// <summary>
        /// セキュリティエラー用のレスポンス生成
        /// </summary>
        private static string CreateSecurityErrorResponse(int id, string message)
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                error = new
                {
                    code = -32001, // セキュリティエラー用のカスタムコード
                    message = $"MCP error -32001: {message}"
                }
            });
        }

        /// <summary>
        /// 一般的なエラーレスポンス生成
        /// </summary>
        private static string CreateErrorResponse(int id, string message)
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32000, message }
            });
        }

        /// <summary>
        /// イベントログ記録
        /// </summary>
        private static void LogEvent(string eventType, string message)
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
            var logMessage = $"[PowerShell.MCP.Proxy] [{timestamp}] [PIPE-{eventType}] {message}";

            // エラー出力にログを記録
            Console.Error.WriteLine(logMessage);

            // デバッグ出力
            System.Diagnostics.Debug.WriteLine(logMessage);

            // 重要なイベントの場合、追加の警告
            if (eventType.Contains("ERROR") || eventType.Contains("FAILED") ||
                eventType.Contains("TIMEOUT") || eventType.Contains("ACCESS_DENIED"))
            {
                var alertMessage = $"[PowerShell.MCP.Proxy] [PIPE-ALERT] {eventType} - check connection status";
                Console.Error.WriteLine(alertMessage);
                System.Diagnostics.Debug.WriteLine(alertMessage);
            }
        }
    }
}
