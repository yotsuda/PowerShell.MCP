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

#region Attributes and Infrastructure

/// <summary>
/// MCPメソッドハンドラーを示す属性
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class McpMethodAttribute : Attribute
{
    public string MethodName { get; }
    //public bool RequiresAuth { get; set; } = false;
    public int TimeoutSeconds { get; set; } = 300; // 5分デフォルト

    public McpMethodAttribute(string methodName) => MethodName = methodName;
}

/// <summary>
/// MCPメソッドハンドラーの実行コンテキスト
/// </summary>
public class McpMethodContext(JsonRpcRequest request, CmdletProvider host, CancellationToken cancellationToken)
{
    public JsonRpcRequest Request { get; } = request;
    public CmdletProvider Host { get; } = host;
    public CancellationToken CancellationToken { get; } = cancellationToken;
}

/// <summary>
/// MCPメソッドハンドラーの結果
/// </summary>
public class McpMethodResult
{
    public bool IsSuccess { get; set; }
    public object? Result { get; set; }
    public object? ErrorInfo { get; set; }

    public static McpMethodResult Success(object? result = null)
        => new() { IsSuccess = true, Result = result };

    public static McpMethodResult Failure(object error)
        => new() { IsSuccess = false, ErrorInfo = error };
}

#endregion

#region DTOs

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

#endregion

#region Configuration

public static class McpServerConfig
{
    public const string PIPE_NAME = "PowerShell.MCP.Communication";
    public const int PIPE_BUFFER_SIZE = 8192;
    public const int MAX_CLIENTS = 1;
    public const int CONNECTION_TIMEOUT_MINUTES = 10;
    public const int MESSAGE_TIMEOUT_SECONDS = 30;
    public const int COMMAND_TIMEOUT_MINUTES = 30;
    public const int POLL_INTERVAL_MS = 100;
}

#endregion

public static class McpServerHost
{
    #region Private Fields

    private static NamedPipeServerStream? _pipeServer;
    private static Task? _serverTask;
    private static CancellationTokenSource? _cancellationTokenSource;
    private static readonly object _lockObject = new object();
    private static readonly Version _serverVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
    private static readonly Dictionary<string, MethodInfo> _methodHandlers = new();

    #endregion

    #region Public Properties (PowerShell Communication)

    public static string? insertCommand = null;
    public static string? executeCommand = null;
    public static string? executeCommandSilent = null;
    public static string? outputFromCommand = null;

    #endregion

    #region Lifecycle Management

    static McpServerHost()
    {
        InitializeMethodHandlers();
    }

    public class Cleanup : IModuleAssemblyCleanup
    {
        public void OnRemove(PSModuleInfo psModuleInfo)
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                lock (_lockObject)
                {
                    CleanupPipeServer();
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

    #endregion

    #region Method Handler Management

    private static void InitializeMethodHandlers()
    {
        var methods = typeof(McpServerHost).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<McpMethodAttribute>() != null);

        foreach (var method in methods)
        {
            var attribute = method.GetCustomAttribute<McpMethodAttribute>()!;
            _methodHandlers[attribute.MethodName] = method;
        }

        LogSecurityEvent("HANDLERS_INITIALIZED", $"Registered {_methodHandlers.Count} method handlers",
            string.Join(", ", _methodHandlers.Keys));
    }

    #endregion

    #region Core Server Logic

    private static async Task RunPipeServerAsync(CancellationToken cancellationToken, CmdletProvider host)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;

            try
            {
                pipeServer = CreateSecurePipeServer();
                lock (_lockObject)
                {
                    _pipeServer = pipeServer;
                }

                LogSecurityEvent("PIPE_WAITING", "Waiting for client connection", "");

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMinutes(McpServerConfig.CONNECTION_TIMEOUT_MINUTES));

                await pipeServer.WaitForConnectionAsync(timeoutCts.Token);

                if (!pipeServer.IsConnected)
                {
                    LogSecurityEvent("PIPE_CONNECTION_FAILED", "Failed to establish connection", "");
                    continue;
                }

                LogSecurityEvent("PIPE_CONNECTED", "Client connected to Named Pipe", "");

                while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessPipeMessageAsync(pipeServer, host, cancellationToken);
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
                        break;
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
                host.WriteError(new ErrorRecord(ex, "PipeCommunicationError", ErrorCategory.ConnectionError, pipeServer));
            }
            finally
            {
                lock (_lockObject)
                {
                    CleanupPipeServer();
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private static void CleanupPipeServer()
    {
        try
        {
            if (_pipeServer != null)
            {
                if (_pipeServer.IsConnected)
                {
                    _pipeServer.Disconnect();
                }
                _pipeServer.Dispose();
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

    #endregion

    #region Message Processing

    private static async Task ProcessPipeMessageAsync(NamedPipeServerStream pipeServer, CmdletProvider host, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(McpServerConfig.MESSAGE_TIMEOUT_SECONDS));

            var message = await ReadPipeMessageAsync(pipeServer, timeoutCts.Token);
            if (string.IsNullOrEmpty(message))
            {
                await Task.Delay(100, cancellationToken);
                return;
            }

            LogSecurityEvent("MESSAGE_RECEIVED", $"Processing message: {message.Substring(0, Math.Min(100, message.Length))}...", "");

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

            var response = await ProcessRpcRequestAsync(rpc, host, cancellationToken);
            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            await SendPipeMessageAsync(pipeServer, responseJson, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            LogSecurityEvent("MESSAGE_TIMEOUT", $"Message processing timed out: {ex.Message}", "");
            try
            {
                await SendPipeErrorAsync(pipeServer, null, "Message processing timed out", cancellationToken);
            }
            catch { }
            throw;
        }
        catch (Exception ex)
        {
            LogSecurityEvent("MESSAGE_PROCESSING_ERROR", $"Error processing message: {ex.Message}", ex.StackTrace ?? "");
            try
            {
                await SendPipeErrorAsync(pipeServer, null, $"Message processing error: {ex.Message}", cancellationToken);
            }
            catch { }
            throw;
        }
    }

    private static async Task<JsonRpcResponse> ProcessRpcRequestAsync(JsonRpcRequest rpc, CmdletProvider host, CancellationToken cancellationToken)
    {
        try
        {
            if (_methodHandlers.TryGetValue(rpc.Method, out var handler))
            {
                var context = new McpMethodContext(rpc, host, cancellationToken);
                var result = await InvokeMethodHandler(handler, context);

                return new JsonRpcResponse(
                    Id: rpc.Id,
                    Result: result.IsSuccess ? result.Result : null,
                    Error: result.IsSuccess ? null : result.ErrorInfo
                );
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

    private static async Task<McpMethodResult> InvokeMethodHandler(MethodInfo handler, McpMethodContext context)
    {
        try
        {
            var attribute = handler.GetCustomAttribute<McpMethodAttribute>()!;

            // タイムアウト設定
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(attribute.TimeoutSeconds));

            var contextWithTimeout = new McpMethodContext(context.Request, context.Host, timeoutCts.Token);

            // メソッド呼び出し
            var resultTask = handler.Invoke(null, new object[] { contextWithTimeout }) as Task<McpMethodResult>;
            if (resultTask == null)
            {
                return McpMethodResult.Failure(new { code = -32603, message = "Handler returned invalid result" });
            }

            return await resultTask;
        }
        catch (OperationCanceledException)
        {
            return McpMethodResult.Failure(new { code = -32002, message = "Request timed out" });
        }
        catch (Exception ex)
        {
            LogSecurityEvent("HANDLER_ERROR", $"Handler error: {ex.Message}", ex.StackTrace ?? "");
            return McpMethodResult.Failure(new { code = -32603, message = "Handler execution failed" });
        }
    }

    #endregion

    #region MCP Method Handlers

    [McpMethod("initialize")]
    private static McpMethodResult HandleInitialize(McpMethodContext context)
    {
        try
        {
            var template = ReturnTemplate("initialize.json", context.Request.Id?.ToString() ?? "0");
            var jsonContent = JsonSerializer.Deserialize<JsonElement>(template);
            return McpMethodResult.Success(jsonContent);
        }
        catch (Exception ex)
        {
            LogSecurityEvent("INITIALIZE_ERROR", $"Initialize error: {ex.Message}", "");
            return McpMethodResult.Failure(new { code = -32603, message = "Initialize failed" });
        }
    }

    [McpMethod("tools/list")]
    private static McpMethodResult HandleToolsList(McpMethodContext context)
    {
        try
        {
            var template = ReturnTemplate("tools_list.json", context.Request.Id?.ToString() ?? "0");
            var jsonContent = JsonSerializer.Deserialize<JsonElement>(template);
            return McpMethodResult.Success(jsonContent);
        }
        catch (Exception ex)
        {
            LogSecurityEvent("TOOLS_LIST_ERROR", $"Tools list error: {ex.Message}", "");
            return McpMethodResult.Failure(new { code = -32603, message = "Tools list failed" });
        }
    }

    [McpMethod("resources/list")]
    private static McpMethodResult HandleResourcesList(McpMethodContext context)
    {
        try
        {
            var template = ReturnTemplate("resources_list.json", context.Request.Id?.ToString() ?? "0");
            var jsonContent = JsonSerializer.Deserialize<JsonElement>(template);
            return McpMethodResult.Success(jsonContent);
        }
        catch (Exception ex)
        {
            LogSecurityEvent("RESOURCES_LIST_ERROR", $"Resources list error: {ex.Message}", "");
            return McpMethodResult.Failure(new { code = -32603, message = "Resources list failed" });
        }
    }

    [McpMethod("prompts/list")]
    private static McpMethodResult HandlePromptsList(McpMethodContext context)
    {
        try
        {
            var template = ReturnTemplate("prompts_list.json", context.Request.Id?.ToString() ?? "0");
            var jsonContent = JsonSerializer.Deserialize<JsonElement>(template);
            return McpMethodResult.Success(jsonContent);
        }
        catch (Exception ex)
        {
            LogSecurityEvent("PROMPTS_LIST_ERROR", $"Prompts list error: {ex.Message}", "");
            return McpMethodResult.Failure(new { code = -32603, message = "Prompts list failed" });
        }
    }

    [McpMethod("tools/call", TimeoutSeconds = 1800)] // 30分タイムアウト
    private static async Task<McpMethodResult> HandleToolsCall(McpMethodContext context)
    {
        try
        {
            var ps = context.Request.Params;
            var cmdName = ps.GetProperty("name").GetString()!;

            // getCurrentLocationの特別処理
            if (cmdName == "getCurrentLocation")
            {
                return await HandleGetCurrentLocation(context);
            }

            bool executeImmediately = context.Request.Params.TryGetProperty("arguments", out var args) &&
                                args.TryGetProperty("executeImmediately", out var exeFlag) &&
                                exeFlag.GetBoolean();

            string command = cmdName == "invokeExpression"
                ? args.GetProperty("pipeline").GetString()! ?? throw new JsonException("Missing 'pipeline'")
                : BuildCommand(cmdName, args);

            // コマンド検証
            var validationError = ValidateCommand(command, executeImmediately);
            if (validationError != null)
            {
                LogSecurityEvent("COMMAND_VALIDATION_FAILED", $"Command validation failed: {validationError}",
                    $"Command: {command.Substring(0, Math.Min(100, command.Length))}...");
                return McpMethodResult.Success(CreateContentResponse(validationError));
            }

            if (!executeImmediately)
            {
                insertCommand = command;
                LogSecurityEvent("COMMAND_ENQUEUED", "Command enqueued for manual execution",
                    $"Command: {command.Substring(0, Math.Min(100, command.Length))}...");
                return McpMethodResult.Success(CreateContentResponse("Command enqueued. Press Enter in console."));
            }
            else
            {
                return await ExecuteCommandImmediate(command, context);
            }
        }
        catch (Exception ex)
        {
            LogSecurityEvent("TOOLS_CALL_ERROR", $"Tools call error: {ex.Message}", ex.StackTrace ?? "");
            return McpMethodResult.Failure(new { code = -32603, message = "Tools call failed" });
        }
    }

    #endregion

    #region Command Execution

    private static async Task<McpMethodResult> ExecuteCommandImmediate(string command, McpMethodContext context)
    {
        outputFromCommand = null;
        executeCommand = command;

        LogSecurityEvent("COMMAND_EXECUTION", "Executing command immediately",
            $"Command: {command.Substring(0, Math.Min(100, command.Length))}...");

        const int maxTimeoutMs = McpServerConfig.COMMAND_TIMEOUT_MINUTES * 60 * 1000;
        const int defaultTimeoutMs = 5 * 60 * 1000; // 5分

        int timeoutMs = defaultTimeoutMs;
        if (context.Request.Params.TryGetProperty("arguments", out var args) &&
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
                return McpMethodResult.Success(CreateContentResponse(output));
            }

            await Task.Delay(McpServerConfig.POLL_INTERVAL_MS, context.CancellationToken);
            elapsed += McpServerConfig.POLL_INTERVAL_MS;
        }

        var timeoutMinutes = timeoutMs / (60 * 1000);
        LogSecurityEvent("COMMAND_TIMEOUT", $"Command execution timed out after {timeoutMinutes} minutes", $"RequestId: {context.Request.Id}");

        return McpMethodResult.Success(CreateContentResponse(
            $"Command execution timed out after {timeoutMinutes} minutes. " +
            $"Consider using a shorter command or increasing the timeout with the timeoutSeconds parameter."));
    }

    private static async Task<McpMethodResult> HandleGetCurrentLocation(McpMethodContext context)
    {
        try
        {
            string locationCommand = EmbeddedResourceLoader.LoadScript("MCPLocationProvider.ps1");

            outputFromCommand = null;
            executeCommandSilent = locationCommand;

            LogSecurityEvent("CURRENT_LOCATION_COMMAND", "Executing PowerShell command to get current location and all drive locations", "Silent execution");

            const int timeoutMs = 10000; // 10秒
            var elapsed = 0;

            while (elapsed < timeoutMs)
            {
                if (outputFromCommand != null)
                {
                    var output = outputFromCommand;
                    outputFromCommand = null;
                    LogSecurityEvent("CURRENT_LOCATION_SUCCESS", "Successfully retrieved current location and all drive locations",
                        output.Substring(0, Math.Min(200, output.Length)));
                    return McpMethodResult.Success(CreateContentResponse(output));
                }

                await Task.Delay(McpServerConfig.POLL_INTERVAL_MS, context.CancellationToken);
                elapsed += McpServerConfig.POLL_INTERVAL_MS;
            }

            LogSecurityEvent("CURRENT_LOCATION_TIMEOUT", "Timeout while getting current location", "Command execution timed out");
            return McpMethodResult.Failure(new { code = -32603, message = "Timeout while getting current location" });
        }
        catch (Exception ex)
        {
            LogSecurityEvent("CURRENT_LOCATION_ERROR", $"Error retrieving current location: {ex.Message}", ex.StackTrace ?? "");
            return McpMethodResult.Failure(new { code = -32603, message = $"Internal error: {ex.Message}" });
        }
    }

    #endregion

    #region Utility Methods

    private static NamedPipeServerStream CreateSecurePipeServer()
    {
        try
        {
            var pipeSecurity = new PipeSecurity();
            var identity = WindowsIdentity.GetCurrent();
            var userSid = identity.User;
            if (userSid != null)
            {
                pipeSecurity.AddAccessRule(new PipeAccessRule(userSid, PipeAccessRights.FullControl, AccessControlType.Allow));
            }

            var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            pipeSecurity.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                McpServerConfig.PIPE_NAME,
                PipeDirection.InOut,
                McpServerConfig.MAX_CLIENTS,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                McpServerConfig.PIPE_BUFFER_SIZE,
                McpServerConfig.PIPE_BUFFER_SIZE,
                pipeSecurity
            );
        }
        catch (Exception ex)
        {
            LogSecurityEvent("PIPE_SECURITY_ERROR", $"Failed to create secure pipe, falling back to basic pipe: {ex.Message}", "");

            return new NamedPipeServerStream(
                McpServerConfig.PIPE_NAME,
                PipeDirection.InOut,
                McpServerConfig.MAX_CLIENTS,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough,
                McpServerConfig.PIPE_BUFFER_SIZE,
                McpServerConfig.PIPE_BUFFER_SIZE
            );
        }
    }

    private static async Task<string> ReadPipeMessageAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        var buffer = new byte[McpServerConfig.PIPE_BUFFER_SIZE];
        using var ms = new MemoryStream();

        do
        {
            var bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            ms.Write(buffer, 0, bytesRead);

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

        // TODO: コマンド検証ロジックを実装
        // 必要に応じて設定ファイルから危険なパターンを読み込む

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

    public static void LogSecurityEvent(string eventType, string message, string details)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");
        var logMessage = $"[PowerShell.MCP] [{timestamp}] [PIPE-{eventType}] {message}";

        if (!string.IsNullOrEmpty(details))
        {
            logMessage += $" | Details: {details}";
        }

        // デバッグ出力
        System.Diagnostics.Debug.WriteLine(logMessage);

        // 重要なイベントの場合、追加の警告
        if (eventType.Contains("ERROR") || eventType.Contains("FAILED") ||
            eventType.Contains("TIMEOUT") || eventType.Contains("VALIDATION"))
        {
            var alertMessage = $"[PowerShell.MCP] [PIPE-ALERT] {eventType} - check server status";
            System.Diagnostics.Debug.WriteLine(alertMessage);
        }
    }

    #endregion
}
