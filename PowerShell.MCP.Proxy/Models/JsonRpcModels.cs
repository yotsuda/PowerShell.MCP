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

    [JsonPropertyName("execute_immediately")]
    public bool ExecuteImmediately { get; set; } = true;
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
