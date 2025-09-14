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
        // Start notification server
        //      _notificationServer = new NotificationPipeServer();
        //      await _notificationServer.StartAsync();

        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            // Process each request directly without Task.Run
            // This avoids ThreadPool issues while maintaining async processing
            _ = ProcessRequestAsync(line, writer);
        }
    }

    private readonly object _writerLock = new(); // Writer synchronization lock

    public static string ClientName { get; private set; } = "unknown";
    public static string ClientVersion { get; private set; } = "unknown";

    private async Task ProcessRequestAsync(string requestLine, StreamWriter writer)
    {
        double id = 0;
        string? method = null;
        
        try
        {
            using var jsonRequest = JsonDocument.Parse(requestLine);
            method = jsonRequest.RootElement.GetProperty("method").GetString();
            id = GetJsonRpcId(jsonRequest);
            var paramsElement = jsonRequest.RootElement.TryGetProperty("params", out var p) ? p : new JsonElement();

            var result = method switch
            {
                "initialize" => await InitializeAsync(paramsElement, id),
                "tools/list" => await ToolsListAsync(paramsElement, id),
                "tools/call" => await ToolsCallAsync(paramsElement, id),
                "prompts/list" => await PromptsListAsync(paramsElement, id),
                "prompts/get" => await PromptsGetAsync(paramsElement, id),
                "ping" => await PingAsync(paramsElement, id),
                _ => throw new InvalidOperationException($"Method not found: {method}")
            };

            // Synchronize output (prevent concurrent writes from multiple threads)
            lock (_writerLock)
            {
                WriteJsonResponse(writer, id, result).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Method not found"))
        {
            var id2 = GetJsonRpcId(JsonDocument.Parse(requestLine));
            var method2 = JsonDocument.Parse(requestLine).RootElement.GetProperty("method").GetString();
            lock (_writerLock)
            {
                WriteJsonError(writer, id2, -32601, "Method not found", method2).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
        catch (JsonException ex)
        {
            await Console.Error.WriteLineAsync($"JSON parsing error: {ex.Message}");
            lock (_writerLock)
            {
                WriteJsonError(writer, 0, -32700, "Parse error", ex.Message).ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Request processing error: {ex.Message}");
            lock (_writerLock)
            {
                WriteJsonError(writer, 0, -32603, "Internal error", ex.Message).ConfigureAwait(false).GetAwaiter().GetResult();
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

    private static Task<object> InitializeAsync(JsonElement parameters, double id)
    {
        // Extract name and version from clientInfo

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
                tools = new { },
                prompts = new { }
            },
            serverInfo = new
            {
                name = "PowerShell.MCP",
                version = ProxyVersion
            }
        });
    }

    private static Task<object> PromptsListAsync(JsonElement parameters, double id)
    {
        Console.Error.WriteLine($"[DEBUG] PromptsListAsync called with ID: {id}");
        
        var result = new
        {
            prompts = new object[]
            {
                new
                {
                    name = "try-powershell",
                    description = "Try PowerShell",
                    arguments = new object[]
                    {
                        new
                        {
                            name = "topic",
                            description = "Topic of PowerShell features or commands to try (e.g., file operations, process management, text processing)",
                            required = false
                        }
                    }
                },
                new
                {
                    name = "explore-folder",
                    description = "Explore folder contents and suggest next actions",
                    arguments = new object[]
                    {
                        new
                        {
                            name = "path",
                            description = "Path of the folder to explore",
                            required = true
                        }
                    }
                },
                new
                {
                    name = "import-module",
                    description = "Import PowerShell module and explore usage",
                    arguments = new object[]
                    {
                        new
                        {
                            name = "module_name",
                            description = "Name of the PowerShell module to import",
                            required = true
                        }
                    }
                }
            }
        };
        
        Console.Error.WriteLine($"[DEBUG] PromptsListAsync returning result for ID: {id}");
        return Task.FromResult<object>(result);
    }
    private static Task<object> PromptsGetAsync(JsonElement parameters, double id)
    {
        var promptName = parameters.GetProperty("name").GetString();
        var arguments = parameters.TryGetProperty("arguments", out var args) ? args : new JsonElement();

        var promptText = promptName switch
        {
            "try-powershell" => GenerateTryPowerShellPrompt(arguments),
            "explore-folder" => GenerateExploreFolderPrompt(arguments),
            "import-module" => GenerateImportModulePrompt(arguments),
            _ => throw new ArgumentException($"Unknown prompt: {promptName}")
        };

        return Task.FromResult<object>(new
        {
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new
                    {
                        type = "text",
                        text = promptText
                    }
                }
            }
        });
    }

    private static string GenerateTryPowerShellPrompt(JsonElement arguments)
    {
        var topic = "";
        if (arguments.TryGetProperty("topic", out var topicProp))
        {
            topic = topicProp.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(topic))
        {
            return "Let's try PowerShell's basic features. Please demonstrate practical commands for file operations, process management, text processing, and other useful functionality. Include explanations and execution examples for each command to provide a hands-on experience with PowerShell's powerful capabilities.";
        }
        else
        {
            return $"Let's explore PowerShell features related to '{topic}'. Please show practical commands and usage examples for this topic. Include explanations of each command and interpretation of execution results to enable learning through actual practice.";
        }
    }

    private static string GenerateExploreFolderPrompt(JsonElement arguments)
    {
        var path = arguments.GetProperty("path").GetString() ?? ".";
        
        return $"Please thoroughly examine the contents of folder '{path}' and suggest next actions. Follow these steps:\n\n" +
               "1. Check basic folder information (location, size, permissions, etc.)\n" +
               "2. Display list of files and subfolders\n" +
               "3. Analyze by file types (extensions, sizes, modification dates, etc.)\n" +
               "4. Investigate details of noteworthy files or structures\n" +
               "5. Suggest useful operations or maintenance tasks for this folder\n\n" +
               "Use PowerShell's rich cmdlets to perform comprehensive analysis.";
    }

    private static string GenerateImportModulePrompt(JsonElement arguments)
    {
        var moduleName = arguments.GetProperty("module_name").GetString() ?? "";
        
        return $"Please import PowerShell module '{moduleName}' and explore its usage:\n\n" +
               "1. Check basic module information (version, description, author, etc.)\n" +
               "2. Import the module\n" +
               "3. Display list of available cmdlets\n" +
               "4. Check help for main cmdlets\n" +
               "5. Execute several practical usage examples\n" +
               "6. Introduce commonly used features and best practices\n\n" +
               "Proceed with explanations at each step to make it beginner-friendly. Include troubleshooting for potential errors.";
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

    // TODO: Call PowerShellProcessManager() directly would be better
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

                // Wait briefly for PowerShell process startup
                // Pipe communication was confirmed, but immediate pipe communication sometimes failed
                //Thread.Sleep(100);

                toolName = "get_current_location";
                break;
            default: // Other tools are delegated to PowerShell module via Named Pipe
                break;
        }

        // Send request to PowerShell module via Named Pipe
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
