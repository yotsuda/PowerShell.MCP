---
document type: module
Help Version: 1.0.0.0
HelpInfoUri: https://github.com/yotsuda/PowerShell.MCP#readme
Locale: ja-JP
Module Guid: 313962fa-c90b-424a-9c8a-d4a05f4a1481
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: PowerShell.MCP Module
---

# PowerShell.MCP Module

## Description

The universal MCP server for Claude Code and other MCP-compatible clients. One installation gives AI access to 10,000+ PowerShell modules and any CLI tool. You and AI collaborate in the same console with full transparency. Supports Windows, Linux, and macOS.

## PowerShell.MCP

### [Add-LinesToFile](Add-LinesToFile.md)

Insert lines into a text file at a specific position or at the end

### [Remove-LinesFromFile](Remove-LinesFromFile.md)

Remove lines from a text file by line range or pattern matching

### [Show-TextFiles](Show-TextFiles.md)

Display file contents with line numbers, or search across files with regex or literal patterns

### [Update-LinesInFile](Update-LinesInFile.md)

Replace or delete specific lines in a text file

### [Update-MatchInFile](Update-MatchInFile.md)

Replace text in a file using literal string or regex pattern

### [Get-MCPOwner](Get-MCPOwner.md)

Gets information about the MCP client that owns this console.

### [Get-MCPProxyPath](Get-MCPProxyPath.md)

Gets the path to the PowerShell.MCP.Proxy executable for the current platform.

### [Register-PwshToClaudeCode](Register-PwshToClaudeCode.md)

Registers PowerShell.MCP as an MCP server in Claude Code.

### [Register-PwshToClaudeDesktop](Register-PwshToClaudeDesktop.md)

Registers PowerShell.MCP as an MCP server in Claude Desktop.

### [Stop-AllPwsh](Stop-AllPwsh.md)

Stops all pwsh processes to release DLL locks.

