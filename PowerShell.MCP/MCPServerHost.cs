using System.Management.Automation;
using System.Management.Automation.Provider;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private static HttpListener? _listener;
    private static Task? _serverTask;

    public class Cleanup : IModuleAssemblyCleanup
    {
        public Cleanup() { }

        public void OnRemove(PSModuleInfo psModuleInfo)
        {
            if (_listener != null)
            {
                try
                {
                    if (_listener.IsListening)
                    {
                        _listener.Stop();
                    }
                }
                catch { /* 無視 */ }
                finally
                {
                    _listener.Close();
                    _listener = null;
                }
            }

            try
            {
                // サーバータスクの完了を待機（短時間）
                _serverTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                // Stop処理でのエラーは無視
            }
            finally
            {
                _serverTask = null;
            }
        }
    }

    public static string? insertCommand = null;
    public static string? executeCommand = null;
    public static string? outputFromCommand = null;

    public static void StartServer(CmdletProvider host, string prefix, CancellationToken token)
    {
        try
        {
            if (_listener != null) return;
 
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();

            _serverTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();
                        _ = HandleContextAsync(ctx);
                    }
                    catch (ObjectDisposedException)
                    {
                        // リスナーが破棄された場合は正常終了
                        break;
                    }
                    catch (HttpListenerException)
                    {
                        // リスナーが停止された場合は正常終了
                        break;
                    }
                    catch (Exception ex)
                    {
                        host.WriteWarning($"[PowerShell.MCP] Server error: {ex.Message}");
                    }
                }
            }, token);
        }
        catch (Exception ex)
        {
            // 初期化エラー時のクリーンアップ
            //_listener?.Stop(); // Dispose 済みなので呼び出せない
            //_listener?.Close();
            _listener = null;
            host.WriteWarning($"[PowerShell.MCP] {ex.Message}");
            throw;
        }
    }

    private static async Task HandleContextAsync(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var resp = ctx.Response;
        try
        {
            if (req.HttpMethod == "POST")
            {
                var path = req.Url?.AbsolutePath;
                if (path == "/tools/call")
                {
                    await ProcessRpcCall(req, resp);
                    return;
                }
                if (path is "/initialize" or "/tools/list" or "/resources/list" or "/prompts/list")
                {
                    var template = path[1..].Replace('/', '_') + ".json";
                    await ReturnTemplate(req, resp, template);
                    return;
                }
            }

            resp.StatusCode = 404;
        }
        catch (JsonException jex)
        {
            await WriteJson(resp, new { error = "JSON parse error", detail = jex.Message });
        }
        catch (Exception ex)
        {
            await WriteJson(resp, new { error = "Internal Server Error", detail = ex.Message });
        }
        finally
        {
            resp.Close();
        }
    }

    private static async Task ProcessRpcCall(HttpListenerRequest req, HttpListenerResponse resp)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync();
        var rpc = JsonSerializer.Deserialize<JsonRpcRequest>(body)
                  ?? throw new JsonException("Invalid RPC payload");
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

        // コマンド検証を実行
        var validationError = ValidateCommand(command, executeImmediately);
        if (validationError != null)
        {
            // 検証エラーがある場合、即座にエラーレスポンスを返す
            var errorResponse = new JsonRpcResponse(
                Jsonrpc: rpc.Jsonrpc,
                Id: rpc.Id,
                Result: CreateContentResponse(validationError)
            );
            var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
            resp.ContentType = "application/json";
            await resp.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(errorJson));
            return;
        }

        JsonRpcResponse response;
        if (!executeImmediately)
        {
            insertCommand = command;
            response = new JsonRpcResponse(
                Jsonrpc: rpc.Jsonrpc,
                Id: rpc.Id,
                Result: CreateContentResponse("Command enqueued. Press Enter in console.")
            );
        }
        else
        {
            outputFromCommand = null;
            executeCommand = command;
            response = await CollectImmediateResponse(rpc);
            Console.WriteLine();
        }
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        resp.ContentType = "application/json";
        await resp.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json));
    }

    // 検証メソッドを追加
    private static string? ValidateCommand(string command, bool executeImmediately)
    {
        // 1. null/空文字チェック
        if (string.IsNullOrWhiteSpace(command))
        {
            return "ERROR: Empty command provided.";
        }

        // 2. 改行文字チェック
        if (!executeImmediately && (command.Contains('\n') || command.Contains('\r')))
        {
            return "ERROR: Multi-line commands are not supported when executeImmediately is false. Please use a single-line PowerShell command.";
        }

        // 3. 長すぎるコマンドのチェック（オプション）
        if (command.Length > 5000)
        {
            var excess = command.Length - 5000;
            var percentage = Math.Round((double)excess / 5000 * 100, 1);

            return $"COMMAND_LENGTH_ERROR: Command too long for execution" +
                   $"\n├─ Current length: {command.Length:N0} characters" +
                   $"\n├─ Maximum allowed: 5,000 characters" +
                   $"\n├─ Excess: {excess:N0} characters ({percentage}% over limit)" +
                   $"\n├─ Command preview: {(command.Length > 100 ? command.Substring(0, 100) + "..." : command)}" +
                   $"\n└─ Solutions:" +
                   $"\n   • Split into multiple commands" +
                   $"\n   • Use variables to store intermediate results" +
                   $"\n   • Simplify complex pipelines" +
                   $"\n   • Use Select-Object to limit output earlier in pipeline";
        }

        // 検証通過
        return null;
    }

    private static string BuildCommand(string name, JsonElement args)
    {
        var sb = new StringBuilder(name);
        foreach (var prop in args.EnumerateObject())
        {
            if (prop.NameEquals("executeImmediately")) continue;
            sb.Append(' ').Append(prop.Name);
            if (prop.Value.ValueKind is JsonValueKind.False) continue;
            sb.Append(' ');
            sb.Append(prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetRawText(),
                _ => prop.Value.GetRawText()
            });
        }
        return sb.ToString();
    }

    private static async Task<JsonRpcResponse> CollectImmediateResponse(JsonRpcRequest rpc)
    {
        // タイムアウト時刻を設定
        var timeout = DateTime.UtcNow.AddSeconds(30);

        // outputForCmdlet がセットされるまで、またはタイムアウトまで待機
        while (outputFromCommand == null && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        // 最終的なテキストを決定
        var text = outputFromCommand ?? "Timeout. No response received";

        // JSON-RPC レスポンス用の content 配列
        var content = new[]
        {
            new { type = "text", text }
        };

        // content 配列を組み立て
        var resultObj = new
        {
            content = new[]
            {
                new { type = "text", text }
            }
        };

        return new JsonRpcResponse(
            Jsonrpc: rpc.Jsonrpc,
            Id: rpc.Id,
            Result: resultObj
        );
    }

    private static async Task ReturnTemplate(HttpListenerRequest req, HttpListenerResponse resp, string templateFile)
    {
        var module_dll_path = Assembly.GetExecutingAssembly().Location;
        var module_dir = Path.GetDirectoryName(module_dll_path);

        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(module_dir!, "Templates", templateFile));
        if (!File.Exists(path))
        {
            resp.StatusCode = 500;
            await WriteJson(resp, new { error = "Template not found.", path });
            return;
        }

        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);
        if (templateFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var id = JsonDocument.Parse(await new StreamReader(req.InputStream).ReadToEndAsync())
                         .RootElement.GetProperty("id").GetRawText();
            content = content.Replace("{0}", id);
        }

        await WriteJson(resp, JsonSerializer.Deserialize<object>(content) ?? "");
    }

    private static JsonSerializerOptions jsoWriteIndented = new() { WriteIndented = false };
    private static Task WriteJson(HttpListenerResponse resp, object obj)
    {
        var json = JsonSerializer.Serialize(obj, jsoWriteIndented);
        var data = Encoding.UTF8.GetBytes(json);
        resp.ContentType = "application/json";
        resp.ContentLength64 = data.Length;
        return resp.OutputStream.WriteAsync(data, 0, data.Length);
    }

    private static object CreateContentResponse(string message)
        => new { content = new[] { new { type = "text", text = message } } };
}
