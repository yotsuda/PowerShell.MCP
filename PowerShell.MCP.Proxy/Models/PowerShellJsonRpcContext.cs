using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerShell.MCP.Proxy.Models;

// JsonSerializerContext for Source Generator
[JsonSerializable(typeof(GetCurrentLocationParams))]
[JsonSerializable(typeof(InvokeExpressionParams))]
[JsonSerializable(typeof(StartPowerShellConsoleParams))]
[JsonSerializable(typeof(GetStatusParams))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default
)]

public partial class PowerShellJsonRpcContext : JsonSerializerContext
{
}
