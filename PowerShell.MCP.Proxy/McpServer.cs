using PowerShell.MCP.Proxy.Services;
using System.Reflection;
using System.Text.Json;

namespace PowerShell.MCP.Proxy;

public class McpServer
{
    private readonly Dictionary<string, Func<JsonElement, double, Task<object>>> _mcpMethods;
//    private readonly PowerShellProcessManager _processManager;
    private readonly NamedPipeClient _pipeClient;
//    private NotificationPipeServer? _notificationServer;

    private static readonly Version _serverVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    public McpServer()
    {
 //       _processManager = new PowerShellProcessManager();
        _pipeClient = new NamedPipeClient();
        
        // MCP標準メソッドを初期化（IDパラメータ追加）
        _mcpMethods = new Dictionary<string, Func<JsonElement, double, Task<object>>>
        {
            ["initialize"] = InitializeAsync,
            ["tools/list"] = ToolsListAsync,
            ["tools/call"] = ToolsCallAsync,
            ["ping"] = PingAsync
        };
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
            await ProcessRequestAsync(line, writer);
        }
    }

    private async Task ProcessRequestAsync(string requestLine, StreamWriter writer)
    {
        try
        {
            using var jsonRequest = JsonDocument.Parse(requestLine);
            var method = jsonRequest.RootElement.GetProperty("method").GetString();
            var id = GetJsonRpcId(jsonRequest);

            if (method != null && _mcpMethods.TryGetValue(method, out var handler))
            {
                var paramsElement = jsonRequest.RootElement.TryGetProperty("params", out var p) ? p : new JsonElement();
                var result = await handler(paramsElement, id); // IDを渡す
                await WriteJsonResponse(writer, id, result);
            }
            else
            {
                await WriteJsonError(writer, id, -32601, "Method not found", method);
            }
        }
        catch (JsonException ex)
        {
            await Console.Error.WriteLineAsync($"JSON parsing error: {ex.Message}");
            await WriteJsonError(writer, 0, -32700, "Parse error", ex.Message);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Request processing error: {ex.Message}");
            await WriteJsonError(writer, 0, -32603, "Internal error", ex.Message);
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
                version = _serverVersion
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
                    name = "getCurrentLocation",
                    description = "Retrieves the current location and all available drives (providers) from the PowerShell session. Returns currentLocation and otherDriveLocations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invokeExpression commands in succession, calling once at the beginning is sufficient.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { },
                        required = Array.Empty<string>()
                    }
                },
                new
                {
                    name = "invokeExpression",
                    description = "Execute PowerShell commands in the PowerShell console. Supports both immediate execution and command insertion modes.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            pipeline = new
                            {
                                type = "string",
                                description = "The PowerShell command or pipeline to execute. When executeImmediately=true (immediate execution), both single-line and multi-line commands are supported, including if statements, loops, functions, and try-catch blocks. When executeImmediately=false (insertion mode), only single-line commands are supported - use semicolons to combine multiple statements into a single line."
                            },
                            executeImmediately = new
                            {
                                type = "boolean",
                                description = "If true, executes the command immediately and returns the result. If false, inserts the command into the console for manual execution.",
                                @default = true
                            }
                        },
                        required = new[] { "pipeline", "executeImmediately" }
                    }
                }
            }
        });
    }

    private async Task<object> ToolsCallAsync(JsonElement parameters, double id)
    {
        var toolName = parameters.GetProperty("name").GetString();
        var arguments = parameters.TryGetProperty("arguments", out var args) ? args : new JsonElement();

        if (toolName == null)
        {
            throw new ArgumentException("Tool name is required");
        }

        // PowerShellプロセスが存在しなければ、自動で起動する
        // なんだかうまく動かない。。起動済みなのに別の pwsh を起動してしまう。一旦コメントアウトしておく
        //if (!PowerShellProcessManager.IsPowerShellProcessRunning())
        //{
        //    await Console.Error.WriteLineAsync("PowerShell process not found. Starting new PowerShell process...");
        //    var startupSuccess = await PowerShellProcessManager.StartPowerShellWithModuleAsync(_pipeClient);
        //    if (!startupSuccess)
        //    {
        //        return new
        //        {
        //            content = new[]
        //            {
        //                new
        //                {
        //                    type = "text",
        //                    text = "Failed to start PowerShell process with PowerShell.MCP module. Please ensure PowerShell 7 is installed and the PowerShell.MCP module is available."
        //                }
        //            }
        //        };
        //    }
        //}

        try
        {
            // Named Pipe経由でPowerShellモジュールにリクエスト送信
            var response = await NamedPipeClient.SendRequestAsync(toolName, arguments, id);
            
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = response
                    }
                }
            };
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Named pipe communication failed: {ex.Message}");
            
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = $"PowerShell communication error: {ex.Message}\n\nPlease ensure that:\n1. PowerShell process is running\n2. PowerShell.MCP module is imported with: Import-Module PowerShell.MCP\n\nIf the issue persists, restart PowerShell and try again."
                    }
                }
            };
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
