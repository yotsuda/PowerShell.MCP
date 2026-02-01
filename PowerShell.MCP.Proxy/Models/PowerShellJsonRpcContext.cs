using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerShell.MCP.Proxy.Models;

// JsonSerializerContext for Source Generator
[JsonSerializable(typeof(GetCurrentLocationParams))]
[JsonSerializable(typeof(InvokeExpressionParams))]
[JsonSerializable(typeof(StartPowerShellConsoleParams))]
[JsonSerializable(typeof(GetStatusParams))]
[JsonSerializable(typeof(ConsumeOutputParams))]
[JsonSerializable(typeof(ClaimConsoleParams))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default
)]

public partial class PowerShellJsonRpcContext : JsonSerializerContext
{
}
