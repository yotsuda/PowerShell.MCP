using System.Text.Json.Serialization;

namespace PowerShell.MCP.Proxy.Models;

public abstract class PowerShellMcpParams
{
    [JsonPropertyName("proxy_version")]
    public string ProxyVersion { get; } = Program.ProxyVersion;

    [JsonPropertyName("name")]
    public abstract string Name { get; }
}

// Parameter type definitions for PowerShell.MCP module
public class GetCurrentLocationParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "get_current_location";
}

public class InvokeExpressionParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "invoke_expression";

    [JsonPropertyName("pipeline")]
    public string Pipeline { get; set; } = string.Empty;
}


public class StartPowerShellConsoleParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "start_powershell_console";
}

public class GetStatusParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "get_status";
}

public class ConsumeOutputParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "consume_output";
}

// Response type for get_status
public class GetStatusResponse
{
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "standby";

    [JsonPropertyName("pipeline")]
    public string? Pipeline { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }
}

[JsonSerializable(typeof(GetStatusResponse))]
public partial class GetStatusResponseContext : JsonSerializerContext { }
