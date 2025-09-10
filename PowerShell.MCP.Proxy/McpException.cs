using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerShell.MCP.Proxy
{
    internal class McpException : Exception
    {
        public McpException(string message) : base(message) { }
        public McpException(string message, Exception inner) : base(message, inner) { }
    }
}
