using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerShell.MCP.Proxy.Models;

// JsonSerializerContext for Source Generator
[JsonSerializable(typeof(GetCurrentLocationParams))]
[JsonSerializable(typeof(InvokeExpressionParams))]
[JsonSerializable(typeof(GetStatusParams))]
[JsonSerializable(typeof(ConsumeOutputParams))]
[JsonSerializable(typeof(ClaimConsoleParams))]
[JsonSerializable(typeof(SetWindowTitleParams))]
[JsonSerializable(typeof(ExecuteSilentParams))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Default
)]

public partial class PowerShellJsonRpcContext : JsonSerializerContext
{
}
