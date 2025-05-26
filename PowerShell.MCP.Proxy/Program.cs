using System.Text.Json;
using System.Text;

namespace PowerShell.MCP.Proxy
{
    internal class Program
    {
        private static readonly Uri ProxyUri = new("http://localhost:8086/");
        private static readonly HttpClient Http = new();

        // POSTメソッドを定数として定義
        private static readonly HashSet<string> PostMethods = new()
        {
            "invoke", "initialize", "tools/list", "resources/list", "prompts/list", "tools/call"
        };

        static async Task Main(string[] args)
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

            while (true)
            {
                try
                {
                    string? requestJson = await ReadRequestAsync(reader);
                    if (requestJson == null) continue;

                    var (method, id, isValid) = ParseJsonRpcRequest(requestJson);
                    if (!isValid || ShouldSkipMethod(method, id)) continue;

                    string responseJson = await ProcessRequestAsync(requestJson, method, id);
                    await writer.WriteLineAsync(responseJson);
                }
                catch (Exception)
                {
                    // ログ出力が必要な場合はここで実装
                    // Console.Error.WriteLine($"[ERROR] {ex.Message}");
                }
            }
        }

        private static async Task<string?> ReadRequestAsync(StreamReader reader)
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

        private static (string method, JsonElement id, bool isValid) ParseJsonRpcRequest(string requestJson)
        {
            try
            {
                using var rpcReq = JsonDocument.Parse(requestJson);
                var root = rpcReq.RootElement;

                string method = root.GetProperty("method").GetString() ?? "";

                if (!root.TryGetProperty("id", out var id))
                {
                    return (method, default, false);
                }

                return (method, id, true);
            }
            catch (JsonException)
            {
                return ("", default, false);
            }
        }

        private static bool ShouldSkipMethod(string method, JsonElement id)
        {
            // 通知メソッドはスキップ
            return method.StartsWith("notifications/");
        }

        private static async Task<string> ProcessRequestAsync(string requestJson, string method, JsonElement id)
        {
            try
            {
                HttpResponseMessage httpResp = await SendHttpRequestAsync(requestJson, method);
                return await httpResp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(id, ex.Message);
            }
        }

        private static async Task<HttpResponseMessage> SendHttpRequestAsync(string requestJson, string method)
        {
            if (PostMethods.Contains(method))
            {
                var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                return await Http.PostAsync(new Uri(ProxyUri, method), content);
            }
            else
            {
                Uri targetUri = BuildGetUri(method, requestJson);
                return await Http.GetAsync(targetUri);
            }
        }

        private static Uri BuildGetUri(string method, string requestJson)
        {
            if (method == "scheme")
            {
                // schemeメソッドの特別処理
                using var doc = JsonDocument.Parse(requestJson);
                var toolName = doc.RootElement
                    .GetProperty("params")
                    .GetProperty("tool")
                    .GetString()!;
                return new Uri(ProxyUri, $"scheme?tool={Uri.EscapeDataString(toolName)}");
            }

            return new Uri(ProxyUri, method);
        }

        private static string CreateErrorResponse(JsonElement id, string message)
        {
            return JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                error = new { code = -32000, message }
            });
        }
    }
}