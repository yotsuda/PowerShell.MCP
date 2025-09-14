using PowerShell.MCP.Proxy.Services;
using System.Reflection;
using System.Text.Json;

namespace PowerShell.MCP.Proxy;

public class McpServer
{
    private readonly NamedPipeClient _pipeClient;
    //    private NotificationPipeServer? _notificationServer;

    public static readonly string ProxyVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

    public McpServer()
    {
        _pipeClient = new NamedPipeClient();
    }

    public async Task RunAsync()
    {
        // 通知受信サーバーを開始
        //      _notificationServer = new NotificationPipeServer();
        //      await _notificationServer.StartAsync();

        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            // 各リクエストを別のタスクで並行処理
            _ = Task.Run(async () => await ProcessRequestAsync(line, writer));
        }
    }

    private readonly object _writerLock = new(); // 出力の排他制御用

    public static string ClientName { get; private set; } = "unknown";
    public static string ClientVersion { get; private set; } = "unknown";

    private async Task ProcessRequestAsync(string requestLine, StreamWriter writer)
    {
        try
        {
            using var jsonRequest = JsonDocument.Parse(requestLine);
            var method = jsonRequest.RootElement.GetProperty("method").GetString();
            var id = GetJsonRpcId(jsonRequest);
            var paramsElement = jsonRequest.RootElement.TryGetProperty("params", out var p) ? p : new JsonElement();

            var result = method switch
            {
                "initialize" => await InitializeAsync(paramsElement, id),
                "tools/list" => await ToolsListAsync(paramsElement, id),
                "tools/call" => await ToolsCallAsync(paramsElement, id),
                "ping" => await PingAsync(paramsElement, id),
                _ => throw new InvalidOperationException($"Method not found: {method}")
            };

            // 出力を同期化（複数スレッドからの同時書き込みを防ぐ）
            lock (_writerLock)
            {
                WriteJsonResponse(writer, id, result).Wait();
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Method not found"))
        {
            var id = GetJsonRpcId(JsonDocument.Parse(requestLine));
            var method = JsonDocument.Parse(requestLine).RootElement.GetProperty("method").GetString();
            lock (_writerLock)
            {
                WriteJsonError(writer, id, -32601, "Method not found", method).Wait();
            }
        }
        catch (JsonException ex)
        {
            await Console.Error.WriteLineAsync($"JSON parsing error: {ex.Message}");
            lock (_writerLock)
            {
                WriteJsonError(writer, 0, -32700, "Parse error", ex.Message).Wait();
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Request processing error: {ex.Message}");
            lock (_writerLock)
            {
                WriteJsonError(writer, 0, -32603, "Internal error", ex.Message).Wait();
            }
        }
    }

    private static double GetJsonRpcId(JsonDocument jsonRequest)
    {
        if (!jsonRequest.RootElement.TryGetProperty("id", out var idProp))
        {
            return 0;
        }

        return idProp.ValueKind switch
        {
            JsonValueKind.Number => idProp.TryGetDouble(out var doubleId) ? doubleId : 0,
            JsonValueKind.String => double.TryParse(idProp.GetString(), out var parsedId) ? parsedId : 0,
            _ => 0
        };
    }

    private Task<object> InitializeAsync(JsonElement parameters, double id)
    {
        // clientInfo から name と version を抽出

        if (parameters.TryGetProperty("clientInfo", out var clientInfo))
        {
            if (clientInfo.TryGetProperty("name", out var nameProperty))
            {
                ClientName = nameProperty.GetString() ?? "unknown";
            }

            if (clientInfo.TryGetProperty("version", out var versionProperty))
            {
                ClientVersion = versionProperty.GetString() ?? "unknown";
            }
        }

        return Task.FromResult<object>(new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "PowerShell.MCP",
                version = ProxyVersion
            }
        });
    }

    private Task<object> ToolsListAsync(JsonElement parameters, double id)
    {
        return Task.FromResult<object>(new
        {
            tools = new object[]
            {
                new
                {
                    name = "get_current_location",
                    description = "Retrieves the current location and all available drives (providers) from the PowerShell session. Returns currentLocation and otherDriveLocations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = Array.Empty<string>()
                    }
                },
                new
                {
                    name = "invoke_expression",
                    description = "Execute PowerShell commands in the PowerShell console. Supports both immediate execution and command insertion modes.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            pipeline = new
                            {
                                type = "string",
                                description = "The PowerShell command or pipeline to execute. When execute_immediately=true (immediate execution), both single-line and multi-line commands are supported, including if statements, loops, functions, and try-catch blocks. When execute_immediately=false (insertion mode), only single-line commands are supported - use semicolons to combine multiple statements into a single line."
                            },
                            execute_immediately = new
                            {
                                type = "boolean",
                                description = "If true, executes the command immediately and returns the result. If false, inserts the command into the console for manual execution.",
                                @default = true
                            }
                        },
                        required = new[] { "pipeline", "execute_immediately" }
                    }
                },
                new
                {
                    name = "start_powershell_console",
                    description = "Launch a new PowerShell console window with PowerShell.MCP module imported. This tool should only be executed when explicitly requested by the user or when other tool executions fail.",
                    inputSchema = new
                    {
                        type = "object",
                        required = Array.Empty<string>()
                    }
                }
            }
        });
    }

    private static object CreateResponse(string text)
    {
        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = text
                }
            }
        };
    }

    // TODO: PowerShellProcessManager() を直接呼び出してもらった方が良いだろう
    private static async void StartPowershellConsole(JsonElement parameters)
    {
        var processStarted = await PowerShellProcessManager.StartPowerShellWithModuleAsync();
        if (!processStarted)
        {
            throw new Exception("Failed to start PowerShell process with PowerShell.MCP module. Please ensure PowerShell 7 is installed and the PowerShell.MCP module is available.");
        }
    }

    private async Task<object> ToolsCallAsync(JsonElement parameters, double id)
    {
        var toolName = parameters.GetProperty("name").GetString();
        var arguments = parameters.TryGetProperty("arguments", out var args) ? args : new JsonElement();

        if (toolName == null)
        {
            throw new ArgumentException("Tool name is required");
        }

        switch (toolName)
        {
            case "start_powershell_console":
                StartPowershellConsole(parameters);

                // PowerShellプロセス起動のために少し待機
                // パイプで疎通確認しているのだけど、直後のパイプ通信が失敗することがあった。。
                //Thread.Sleep(100);

                toolName = "get_current_location";
                break;
            default: // 上記以外のツールは、Named Pipe 経由で PowerShell モジュールに処理を委譲
                break;
        }

        // Named Pipe 経由で PowerShell モジュールにリクエスト送信
        try
        {
            var response = await NamedPipeClient.SendRequestAsync(toolName, arguments, id);
            return CreateResponse(response);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Named pipe communication failed: {ex.Message}");
            if (ex.Message.Contains("version is outdated"))
            {
                return CreateResponse(ex.Message);
            }
            else
            {
                return CreateResponse($"PowerShell communication error: {ex.Message}\n\nPlease ensure that:\n1. PowerShell process is running\n2. PowerShell.MCP module is imported with: Import-Module PowerShell.MCP\n\nIf the issue persists, restart PowerShell and try again.");

            }
        }
    }

    private Task<object> PingAsync(JsonElement parameters, double id)
    {
        return Task.FromResult<object>(new { });
    }

    private static async Task WriteJsonResponse(StreamWriter writer, double id, object result)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id = id,
            result = result
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await writer.WriteLineAsync(json);
    }

    // TODO: remove data param
    private static async Task WriteJsonError(StreamWriter writer, double id, int code, string message, object? data = null)
    {
        var error = new
        {
            jsonrpc = "2.0",
            id = id,
            error = new
            {
                code = code,
                message = message
                //data = data
            }
        };

        var json = JsonSerializer.Serialize(error, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await writer.WriteLineAsync(json);
    }
}
