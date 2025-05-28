using System.Text.Json;
using System.Text;
using System.Reflection;
using System.Diagnostics;

namespace PowerShell.MCP.Proxy
{
    internal class Program
    {
        private static readonly Uri ProxyUri = new("http://localhost:8086/");
        private static readonly HttpClient Http = new();

        // POSTメソッドを定数として定義
        private static readonly HashSet<string> PostMethods = new()
        {
            "initialize", "tools/list", "resources/list", "prompts/list", "tools/call"
        };

        private static string ReturnTemplate(string method, int id)
        {
            // 実行アセンブリのパスとテンプレートフォルダーへのパスを組み立て
            var module_exe_path = Assembly.GetExecutingAssembly().Location;
            var module_dir = Path.GetDirectoryName(module_exe_path)!;
            var template_name = method.Replace('/', '_') + ".json";

            var template_path = Path.GetFullPath(Path.Combine(module_dir, @"..\Templates", template_name));

            // ファイル読み込み
            var content = File.ReadAllText(template_path, Encoding.UTF8);
            return content.Replace("{0}", id.ToString());
        }

        static async Task Main(string[] args)
        {
            //Debugger.Launch();
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            using var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

            while (true)
            {
                try
                {
                    string? requestJson = await ReadRequestAsync(reader);
                    if (requestJson == null) continue;

                    var (method, id, isValid) = ParseJsonRpcRequest(requestJson);
                    if (!isValid || ShouldSkipMethod(method)) continue;

                    string? responseJson = null;
                    if (method != "tools/call")
                    {
                        responseJson = ReturnTemplate(method, id)
                            .Replace("\n", "")
                            .Replace("\r", "");
                    }
                    else
                    {
                        responseJson = await ProcessRequestAsync(requestJson, method, id);
                    }
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
            catch (JsonException)
            {
                return ("", default, false);
            }
        }

        private static bool ShouldSkipMethod(string method)
        {
            // 通知メソッドはスキップ
            return method.StartsWith("notifications/");
        }

        private static async Task<string> ProcessRequestAsync(string requestJson, string method, int id)
        {
            try
            {
                HttpResponseMessage httpResp = await SendHttpRequestAsync(requestJson, method);
                return await httpResp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return CreateErrorResponse(
                    id,
                    $@"It looks like PowerShell isn't running. Please do the following:
1. Press Win+R to open the Run dialog.
2. Type `pwsh` and press Enter to launch PowerShell 7.
3. In the PowerShell window, paste this command and press Enter:
   Import-Module PowerShell.MCP, PSReadLine

Once that's done, try again. (Error details: {ex.Message})"
                );
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

        private static string CreateErrorResponse(int id, string message)
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