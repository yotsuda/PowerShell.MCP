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

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 170;
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
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }


    [JsonPropertyName("output")]
    public string? Output { get; set; }
}

[JsonSerializable(typeof(GetStatusResponse))]
public partial class GetStatusResponseContext : JsonSerializerContext { }

public class ClaimConsoleParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "claim_console";

    [JsonPropertyName("proxy_pid")]
    public int ProxyPid { get; set; }
}

public class ClaimConsoleResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("new_pipe_name")]
    public string? NewPipeName { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

[JsonSerializable(typeof(ClaimConsoleResponse))]
public partial class ClaimConsoleResponseContext : JsonSerializerContext { }

public class SetWindowTitleParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "set_window_title";

    [JsonPropertyName("title")]
    public required string Title { get; set; }
}
