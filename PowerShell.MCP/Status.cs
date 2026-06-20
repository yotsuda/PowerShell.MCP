namespace PowerShell.MCP
{
    /// <summary>
    /// Unified status object returned by the Get-MCPOwner and Restart-MCPServer
    /// module functions: the engine-readiness state plus the current console's
    /// ownership (proxy / agent / client). A dedicated type — rather than a bare
    /// PSCustomObject — gives both functions a single, discoverable OutputType.
    /// Properties use { get; set; } so PowerShell's <c>[PowerShell.MCP.Status]@{...}</c>
    /// hashtable construction can populate them after the default constructor.
    /// </summary>
    public sealed class Status
    {
        /// <summary>True once the embedded polling engine is running on this console.</summary>
        public bool EngineReady { get; set; }

        /// <summary>True when this console is owned by a proxy (4-segment pipe name).</summary>
        public bool Owned { get; set; }

        /// <summary>PID of the owning proxy, or null when unowned.</summary>
        public int? ProxyPid { get; set; }

        /// <summary>Agent ID segment of the pipe name, or null when unowned.</summary>
        public string? AgentId { get; set; }

        /// <summary>
        /// Best-effort MCP client name (Claude Code / Claude Desktop / VS Code /
        /// Cursor, else the proxy process name), or null when unowned / unknown.
        /// </summary>
        public string? ClientName { get; set; }

        /// <summary>Message from the most recent failed engine start, or null.</summary>
        public string? LastError { get; set; }
    }
}
