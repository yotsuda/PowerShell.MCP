using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerShell.MCP.Proxy.Models;

// Source Generator 用の JsonSerializerContext
[JsonSerializable(typeof(GetCurrentLocationParams))]
[JsonSerializable(typeof(InvokeExpressionParams))]
[JsonSerializable(typeof(StartPowerShellConsoleParams))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default
)]

public partial class PowerShellJsonRpcContext : JsonSerializerContext
{
}
