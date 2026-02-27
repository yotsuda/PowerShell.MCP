---
Module Name: PowerShell.MCP
Module Guid: 313962fa-c90b-424a-9c8a-d4a05f4a1481
Download Help Link: https://github.com/yotsuda/PowerShell.MCP
Help Version: 1.2.6
Locale: en-US
---

# PowerShell.MCP Module
## Description
Enables PowerShell console to function as an MCP server for Claude Desktop and other clients. Provides LLM-optimized text file manipulation cmdlets that preserve file metadata (encoding, newlines) for reliable operations.

## PowerShell.MCP Cmdlets
### [Add-LinesToFile](Add-LinesToFile.md)
Insert lines into a text file at a specific position or at the end

### [Get-MCPOwner](Get-MCPOwner.md)
Gets information about the MCP client that owns this console.

### [Get-MCPProxyPath](Get-MCPProxyPath.md)
Gets the path to the PowerShell.MCP.Proxy executable for the current platform.

### [Remove-LinesFromFile](Remove-LinesFromFile.md)
Remove lines from a text file by line range or pattern matching

### [Show-TextFile](Show-TextFile.md)
Display text file contents with line numbers

### [Update-LinesInFile](Update-LinesInFile.md)
Replace or delete specific lines in a text file

### [Update-MatchInFile](Update-MatchInFile.md)
Replace text in a file using literal string or regex pattern

