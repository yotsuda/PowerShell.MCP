# PowerShell.MCP

[![PowerShell Gallery](https://img.shields.io/powershellgallery/v/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)

## Security Warning
**This module provides complete PowerShell access to your system.**  
Malicious use could result in severe damage. Use responsibly and only in trusted environments.

## Overview
PowerShell.MCP enables PowerShell 7 to function as an MCP server, allowing MCP clients like Claude Desktop to access the entire PowerShell ecosystem through secure named pipe communication.

## Key Features
- **🔒 Secure Named Pipe Communication** - Local-only access, no remote connections
- **⚡ Real-time Command Execution** - Comprehensive output capture
- **📚 Rich Prompt Examples** - Ready-to-use practical scenarios
- **🏢 Enterprise-Ready** - Designed for safe corporate environments

## System Requirements
- Windows 10/11 or Windows Server 2016+
- PowerShell 7.2.15 or higher
- PSReadLine 2.3.4 or higher

## Installation
`powershell
Install-Module PowerShell.MCP
Import-Module PowerShell.MCP,PSReadLine
`

## Claude Desktop Configuration
`json
{
  "mcpServers": {
    "PowerShell": {
      "command": "[ModuleBase]\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
`

Find your module base path:
`powershell
(Get-Module PowerShell.MCP).ModuleBase
`

## Architecture
1. **PowerShell Module** - Named pipe-based MCP server functionality
2. **Stdio Proxy Server** - Bridge between MCP clients and named pipe MCP server
3. **MCP Client** - Connects to proxy server via stdio for operations

## Security
- **Local Communication Only** - Named pipe prevents remote access
- **Enterprise-Grade** - Works with appropriate security policies
- **Full Transparency** - Open source for complete auditability

## Example Use Cases
- System administration and monitoring
- File and directory operations
- Network diagnostics and analysis
- Automated reporting and data processing
- Development environment management

## Disclaimer
This software is provided "AS IS" without warranty of any kind, either expressed or implied.  
The author assumes no responsibility for any damages arising from the use of this software.

## License
MIT License - see [LICENSE](LICENSE) for details.

## Author
Yoshifumi Tsuda

---
**For enterprise use, ensure compliance with your organization's security policies.**
