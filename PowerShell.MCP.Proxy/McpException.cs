namespace PowerShell.MCP.Proxy
{
    internal class McpException : Exception
    {
        public McpException(string message) : base(message) { }
        public McpException(string message, Exception inner) : base(message, inner) { }
    }
}
