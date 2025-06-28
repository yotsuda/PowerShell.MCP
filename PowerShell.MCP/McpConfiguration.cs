namespace PowerShell.MCP;

/// <summary>
/// MCP サーバー基本設定
/// </summary>
public class McpConfiguration
{
    public string PipeName { get; set; } = "PowerShell.MCP.Communication";
    public int PipeBufferSize { get; set; } = 8192;
    public int MaxClients { get; set; } = 1;
    public int ConnectionTimeoutMinutes { get; set; } = 10;
    public int MessageTimeoutSeconds { get; set; } = 30;
    public int CommandTimeoutMinutes { get; set; } = 30;
    public int PollIntervalMs { get; set; } = 100;
    
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(PipeName) && 
               PipeBufferSize > 0 && 
               MaxClients > 0;
    }
}
