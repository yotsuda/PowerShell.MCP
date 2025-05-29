using System.Management.Automation;
using System.Management.Automation.Provider;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Principal;
using System.Security.AccessControl;

namespace PowerShell.MCP;

public record JsonRpcRequest(
    [property: JsonPropertyName("id")] object? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement Params,
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc = "2.0"
);

public record JsonRpcResponse(
    [property: JsonPropertyName("id")] object? Id,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] object? Error = null,
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc = "2.0"
);

public static class McpServerHost
{
    private static NamedPipeServerStream? _pipeServer;
    private static Task? _serverTask;
    private static CancellationTokenSource? _cancellationTokenSource;
    private static readonly object _lockObject = new object();

    // Named Pipe設定
    private const string PIPE_NAME = "PowerShell.MCP.Communication";
    private const int PIPE_BUFFER_SIZE = 8192;
    private const int MAX_CLIENTS = 1;

    // セキュリティ設定
    private const int MAX_COMMAND_LENGTH = 5000;
    private const int MAX_MESSAGE_SIZE = 1024 * 1024; // 1MB制限

    // バージョン管理（起動時に一度だけ取得）
    private static readonly Version _serverVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    public class Cleanup : IModuleAssemblyCleanup
    {
        public Cleanup() { }

        public void OnRemove(PSModuleInfo psModuleInfo)
        {
            try
            {
                _cancellationTokenSource?.Cancel();

                lock (_lockObject)
                {
                    if (_pipeServer != null)
                    {
                        try
                        {
                            if (_pipeServer.IsConnected)
                            {
                                _pipeServer.Disconnect();
                            }
                        }
                        catch { /* 無視 */ }
                        finally
                        {
                            _pipeServer.Dispose();
                            _pipeServer = null;
                        }
                    }
                }

                _serverTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Stop処理でのエラーは無視
            }
            finally
            {
                _serverTask = null;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
    }

    public static string? insertCommand = null;
    public static string? executeCommand = null;
    public static string? outputFromCommand = null;

    public static void StartServer(CmdletProvider host, CancellationToken token)
    {
        try
        {
            lock (_lockObject)
            {
                if (_pipeServer != null)
                {
                    LogSecurityEvent("SERVER_ALREADY_RUNNING", "MCP server is already running", "");
                    return;
                }
            }

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);

            LogSecurityEvent("SERVER_START", $"MCP server starting with Named Pipes (Version: {_serverVersion})", "");

            _serverTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        await RunPipeServerAsync(_cancellationTokenSource.Token, host);
                    }
                    catch (OperationCanceledException)
                    {
                        LogSecurityEvent("SERVER_CANCELLED", "Server operation cancelled", "");
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogSecurityEvent("SERVER_ERROR", $"Pipe server error: {ex.Message}", ex.StackTrace ?? "");
                        host.WriteWarning($"[PowerShell.MCP] Pipe server error: {ex.Message}");

                        // エラー後、短時間待機してから再実行
                        try
                        {
                            await Task.Delay(2000, _cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }, _cancellationTokenSource.Token);

            LogSecurityEvent("SERVER_START", "MCP Named Pipe server started successfully", "");
        }
        catch (Exception ex)
        {
            LogSecurityEvent("SERVER_START_FAILED", $"Failed to start pipe server: {ex.Message}", ex.StackTrace ?? "");
            host.WriteWarning($"[PowerShell.MCP] {ex.Message}");
            throw;
        }
    }

    private static async Task RunPipeServerAsync(CancellationToken cancellationToken, CmdletProvider host)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;

            try
            {
                // セキュアなNamed Pipeサーバーを作成
                pipeServer = CreateSecurePipeServer();

                lock (_lockObject)
                {
                    _pipeServer = pipeServer;
                }

                LogSecurityEvent("PIPE_WAITING", "Waiting for client connection", "");

                // クライアントの接続を待機（タイムアウト付き）
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(10)); // 10分でタイムアウト

                await pipeServer.WaitForConnectionAsync(timeoutCts.Token);

                if (!pipeServer.IsConnected)
                {
                    LogSecurityEvent("PIPE_CONNECTION_FAILED", "Failed to establish connection", "");
                    continue; // 次の接続を待つ
                }

                LogSecurityEvent("PIPE_CONNECTED", "Client connected to Named Pipe", "");

                // 接続中のメッセージ処理
                while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessPipeMessageAsync(pipeServer, cancellationToken);
                    }
                    catch (IOException ex) when (ex.Message.Contains("pipe is broken") || ex.Message.Contains("pipe has been ended"))
                    {
                        LogSecurityEvent("PIPE_DISCONNECTED", "Client disconnected", ex.Message);
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogSecurityEvent("PIPE_MESSAGE_ERROR", $"Error processing message: {ex.Message}", ex.StackTrace ?? "");
                        // メッセージ処理エラーの場合は接続を維持してリトライ
                        await Task.Delay(100, cancellationToken);
                    }
                }

                LogSecurityEvent("PIPE_CONNECTION_ENDED", "Connection ended, preparing for next client", "");
            }
            catch (OperationCanceledException)
            {
                LogSecurityEvent("PIPE_CANCELLED", "Pipe operation cancelled", "");
                break;
            }
            catch (TimeoutException)
            {
                LogSecurityEvent("PIPE_TIMEOUT", "Pipe connection timeout", "");
            }
            catch (Exception ex)
            {
                LogSecurityEvent("PIPE_ERROR", $"Pipe communication error: {ex.Message}", ex.StackTrace ?? "");
            }
            finally
            {
                lock (_lockObject)
                {
                    try
                    {
                        if (pipeServer != null)
                        {
                            if (pipeServer.IsConnected)
                            {
                                pipeServer.Disconnect();
                            }
                            pipeServer.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        LogSecurityEvent("PIPE_CLEANUP_ERROR", $"Error during pipe cleanup: {ex.Message}", "");
                    }
                    finally
                    {
                        _pipeServer = null;
                    }
                }
            }

            // 短時間待機してから次の接続受付を開始
            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private static NamedPipeServerStream CreateSecurePipeServer()
    {
        try
        {
            // .NET 8対応: セキュリティ設定付きのパイプサーバーを作成
            var pipeSecurity = new PipeSecurity();

            // 現在のユーザーにフルアクセス権限を付与
            var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User;
            if (userSid != null)
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            }

            // 管理者グループにフルアクセス権限を付与
            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                PIPE_NAME,
                PipeDirection.InOut,
                MAX_CLIENTS,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                PIPE_BUFFER_SIZE,
                PIPE_BUFFER_SIZE,
                pipeSecurity
            );
        }
        catch (Exception ex)
        {
            LogSecurityEvent("PIPE_SECURITY_ERROR", $"Failed to create secure pipe, falling back to basic pipe: {ex.Message}", "");

            // フォールバック: セキュリティ設定なしの基本的なパイプ
            return new NamedPipeServerStream(
                PIPE_NAME,
                PipeDirection.InOut,
                MAX_CLIENTS,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                PIPE_BUFFER_SIZE,
                PIPE_BUFFER_SIZE
            );
        }
    }

    private static async Task ProcessPipeMessageAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        try
        {
            // メッセージを読み取り（タイムアウト付き）
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30秒でタイムアウト

            var message = await ReadPipeMessageAsync(pipeServer, timeoutCts.Token);
            if (string.IsNullOrEmpty(message))
            {
                await Task.Delay(100, cancellationToken); // 短時間待機
                return;
            }

            LogSecurityEvent("MESSAGE_RECEIVED", $"Processing message: {message.Substring(0, Math.Min(100, message.Length))}...", "");

            // JSON-RPC リクエストを解析
            JsonRpcRequest? rpc;
            try
            {
                rpc = JsonSerializer.Deserialize<JsonRpcRequest>(message);
            }
            catch (JsonException ex)
            {
                LogSecurityEvent("JSON_PARSE_ERROR", $"Invalid JSON: {ex.Message}", message);
                await SendPipeErrorAsync(pipeServer, null, "Invalid JSON-RPC request", cancellationToken);
                return;
            }

            if (rpc == null)
            {
                await SendPipeErrorAsync(pipeServer, null, "Invalid JSON-RPC request", cancellationToken);
                return;
            }

            // リクエストを処理
            var response = await ProcessRpcRequestAsync(rpc);

            // レスポンスを送信
            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            await SendPipeMessageAsync(pipeServer, responseJson, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSecurityEvent("MESSAGE_PROCESSING_ERROR", $"Error processing message: {ex.Message}", ex.StackTrace ?? "");
            try
            {
                await SendPipeErrorAsync(pipeServer, null, $"Message processing error: {ex.Message}", cancellationToken);
            }
            catch
            {
                // エラー送信に失敗した場合は無視
            }
        }
    }

    private static async Task<string> ReadPipeMessageAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        var buffer = new byte[PIPE_BUFFER_SIZE];
        using var ms = new MemoryStream();

        do
        {
            var bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            ms.Write(buffer, 0, bytesRead);

            // メッセージサイズ制限チェック
            if (ms.Length > MAX_MESSAGE_SIZE)
            {
                throw new InvalidOperationException($"Message too large: {ms.Length} bytes");
            }

        } while (!pipeServer.IsMessageComplete);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static async Task SendPipeMessageAsync(NamedPipeServerStream pipeServer, string message, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes(message);
        await pipeServer.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        await pipeServer.FlushAsync(cancellationToken);
    }

    private static async Task SendPipeErrorAsync(NamedPipeServerStream pipeServer, object? id, string errorMessage, CancellationToken cancellationToken)
    {
        var errorResponse = new JsonRpcResponse(
            Id: id,
            Error: new { code = -32000, message = errorMessage }
        );

        var errorJson = JsonSerializer.Serialize(errorResponse);
        await SendPipeMessageAsync(pipeServer, errorJson, cancellationToken);
    }

    // 以下、既存のメソッドはそのまま維持（ProcessRpcRequestAsync以降）
    private static async Task<JsonRpcResponse> ProcessRpcRequestAsync(JsonRpcRequest rpc)
    {
        try
        {
            var ps = rpc.Params;
            var method = rpc.Method;

            // メソッド別処理
            if (method == "tools/call")
            {
                return await ProcessToolCallAsync(rpc);
            }
            else if (method is "initialize" or "tools/list" or "resources/list" or "prompts/list")
            {
                return ProcessTemplateRequest(rpc, method);
            }
            else
            {
                return new JsonRpcResponse(
                    Id: rpc.Id,
                    Error: new { code = -32601, message = "Method not found" }
                );
            }
        }
        catch (Exception ex)
        {
            LogSecurityEvent("RPC_PROCESSING_ERROR", $"RPC processing error: {ex.Message}", ex.StackTrace ?? "");
            return new JsonRpcResponse(
                Id: rpc.Id,
                Error: new { code = -32603, message = "Internal error" }
            );
        }
    }

    private static async Task<JsonRpcResponse> ProcessToolCallAsync(JsonRpcRequest rpc)
    {
        var ps = rpc.Params;
        var cmdName = ps.GetProperty("name").GetString()!;
        bool executeImmediately = rpc.Params.TryGetProperty("arguments", out var args) &&
                            args.TryGetProperty("executeImmediately", out var exeFlag) &&
                            exeFlag.GetBoolean();

        string command;
        switch (cmdName)
        {
            case "invokeExpression":
                command = args.GetProperty("pipeline").GetString()!
                          ?? throw new JsonException("Missing 'pipeline'");
                break;
            default:
                command = BuildCommand(cmdName, args);
                break;
        }

        // コマンド検証
        var validationError = ValidateCommand(command, executeImmediately);
        if (validationError != null)
        {
            LogSecurityEvent("COMMAND_VALIDATION_FAILED",
                $"Command validation failed: {validationError}",
                $"Command: {command.Substring(0, Math.Min(100, command.Length))}...");

            return new JsonRpcResponse(
                Jsonrpc: rpc.Jsonrpc,
                Id: rpc.Id,
                Result: CreateContentResponse(validationError)
            );
        }

        if (!executeImmediately)
        {
            insertCommand = command;
            LogSecurityEvent("COMMAND_ENQUEUED", "Command enqueued for manual execution",
                $"Command: {command.Substring(0, Math.Min(100, command.Length))}...");

            return new JsonRpcResponse(
                Jsonrpc: rpc.Jsonrpc,
                Id: rpc.Id,
                Result: CreateContentResponse("Command enqueued. Press Enter in console.")
            );
        }
        else
        {
            outputFromCommand = null;
            executeCommand = command;

            LogSecurityEvent("COMMAND_EXECUTION", "Executing command immediately",
                $"Command: {command.Substring(0, Math.Min(100, command.Length))}...");

            var response = await CollectImmediateResponse(rpc);
            return response;
        }
    }

    // 以下、既存のメソッドをそのまま維持
    private static JsonRpcResponse ProcessTemplateRequest(JsonRpcRequest rpc, string method)
    {
        try
        {
            var template = method.Replace('/', '_') + ".json";
            var content = ReturnTemplate(template, rpc.Id?.ToString() ?? "0");

            // JSONとしてパースして返す
            var jsonContent = JsonSerializer.Deserialize<JsonElement>(content);

            return new JsonRpcResponse(
                Jsonrpc: rpc.Jsonrpc,
                Id: rpc.Id,
                Result: jsonContent
            );
        }
        catch (Exception ex)
        {
            LogSecurityEvent("TEMPLATE_ERROR", $"Template processing error: {ex.Message}", $"Method: {method}");
            return new JsonRpcResponse(
                Id: rpc.Id,
                Error: new { code = -32603, message = "Template processing failed" }
            );
        }
    }

    private static string ReturnTemplate(string templateName, string id)
    {
        try
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var assemblyDir = Path.GetDirectoryName(assemblyLocation)!;
            var templatePath = Path.Combine(assemblyDir, "..", "Templates", templateName);

            if (File.Exists(templatePath))
            {
                var content = File.ReadAllText(templatePath);
                return content.Replace("{0}", id);
            }
            else
            {
                throw new FileNotFoundException($"Template not found: {templateName}");
            }
        }
        catch (Exception ex)
        {
            LogSecurityEvent("TEMPLATE_READ_ERROR", $"Failed to read template: {ex.Message}", $"Template: {templateName}");
            throw;
        }
    }

    private static string? ValidateCommand(string command, bool executeImmediately)
    {
        if (string.IsNullOrEmpty(command))
            return "Command cannot be empty";

        if (command.Length > MAX_COMMAND_LENGTH)
            return $"Command too long (max {MAX_COMMAND_LENGTH} characters)";

        //// 危険なコマンドのチェック
        //var dangerousPatterns = new[]
        //{
        //    "Remove-Item", "rm ", "del ", "format ", "shutdown", "restart", "reboot"
        //};

        //var lowerCommand = command.ToLowerInvariant();
        //foreach (var pattern in dangerousPatterns)
        //{
        //    if (lowerCommand.Contains(pattern.ToLowerInvariant()))
        //    {
        //        return $"Potentially dangerous command detected: {pattern}";
        //    }
        //}

        return null;
    }

    private static string BuildCommand(string cmdName, JsonElement args)
    {
        var sb = new StringBuilder(cmdName);

        foreach (var prop in args.EnumerateObject())
        {
            if (prop.Name == "executeImmediately" || prop.Name == "timeoutSeconds")
                continue;

            var value = prop.Value;
            sb.Append($" -{prop.Name}");

            if (value.ValueKind == JsonValueKind.String)
            {
                var str = value.GetString()!;
                sb.Append($" '{str.Replace("'", "''")}'");
            }
            else if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                sb.Append($" {value.GetRawText()}");
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                var items = value.EnumerateArray().Select(v =>
                    v.ValueKind == JsonValueKind.String ? $"'{v.GetString()!.Replace("'", "''")}'" : v.GetRawText()
                );
                sb.Append($" @({string.Join(", ", items)})");
            }
        }

        return sb.ToString();
    }

    private static async Task<JsonRpcResponse> CollectImmediateResponse(JsonRpcRequest rpc)
    {
        const int maxTimeoutMs = 30 * 60 * 1000; // 30分
        const int defaultTimeoutMs = 5 * 60 * 1000; // 5分
        const int pollIntervalMs = 100;

        int timeoutMs = defaultTimeoutMs;
        if (rpc.Params.TryGetProperty("arguments", out var args) &&
            args.TryGetProperty("timeoutSeconds", out var timeoutParam) &&
            timeoutParam.TryGetInt32(out var customTimeout))
        {
            timeoutMs = Math.Min(customTimeout * 1000, maxTimeoutMs);
        }

        var elapsed = 0;

        while (elapsed < timeoutMs)
        {
            if (outputFromCommand != null)
            {
                var output = outputFromCommand;
                outputFromCommand = null;
                return new JsonRpcResponse(
                    Jsonrpc: rpc.Jsonrpc,
                    Id: rpc.Id,
                    Result: CreateContentResponse(output)
                );
            }

            await Task.Delay(pollIntervalMs);
            elapsed += pollIntervalMs;
        }

        var timeoutMinutes = timeoutMs / (60 * 1000);
        LogSecurityEvent("COMMAND_TIMEOUT",
            $"Command execution timed out after {timeoutMinutes} minutes",
            $"RequestId: {rpc.Id}");

        return new JsonRpcResponse(
            Jsonrpc: rpc.Jsonrpc,
            Id: rpc.Id,
            Result: CreateContentResponse($"Command execution timed out after {timeoutMinutes} minutes. " +
                                       $"Consider using a shorter command or increasing the timeout with the timeoutSeconds parameter.")
        );
    }

    private static object CreateContentResponse(string text)
    {
        return new
        {
            content = new object[]
            {
                new { type = "text", text = text }
            }
        };
    }

    /// <summary>
    /// セキュリティイベントのログ記録
    /// </summary>
    private static void LogSecurityEvent(string eventType, string message, string details)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        var logMessage = $"[PowerShell.MCP] [{timestamp}] [PIPE-{eventType}] {message}";

        if (!string.IsNullOrEmpty(details))
        {
            logMessage += $" | Details: {details}";
        }

        // コンソールに出力
        //Console.WriteLine(logMessage);

        // デバッグ出力
        System.Diagnostics.Debug.WriteLine(logMessage);

        // 重要なイベントの場合、追加の警告
        if (eventType.Contains("ERROR") || eventType.Contains("FAILED") ||
            eventType.Contains("TIMEOUT") || eventType.Contains("VALIDATION"))
        {
            var alertMessage = $"[PowerShell.MCP] [PIPE-ALERT] {eventType} - check server status";
            //Console.WriteLine(alertMessage);
            System.Diagnostics.Debug.WriteLine(alertMessage);
        }
    }
}
