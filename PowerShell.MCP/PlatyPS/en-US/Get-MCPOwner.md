---
document type: cmdlet
external help file: PowerShell.MCP-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Get-MCPOwner
---

# Get-MCPOwner

## SYNOPSIS

Gets information about the MCP client that owns this console.

## SYNTAX

### __AllParameterSets

```
Get-MCPOwner [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Returns ownership information for the current PowerShell console, including
whether it is owned by an MCP proxy, the proxy's PID, the agent ID,
and the client name (e.g., Claude Desktop, Claude Code, VS Code).

## EXAMPLES

### EXAMPLE 1

Get-MCPOwner

Owned      : True
ProxyPid   : 22208
AgentId    : cc19706b
ClientName : Claude Desktop

### EXAMPLE 2

Get-MCPOwner

Owned      : False
ProxyPid   :
AgentId    :
ClientName :

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### PSCustomObject with Owned

PSCustomObject with Owned, ProxyPid, AgentId, and ClientName properties.

### System.Management.Automation.PSObject

PSCustomObject with Owned, ProxyPid, AgentId, and ClientName properties.

## NOTES

## RELATED LINKS

- [Get-MCPProxyPath](Get-MCPProxyPath.md)
- [Register-PwshToClaudeCode](Register-PwshToClaudeCode.md)
- [Register-PwshToClaudeDesktop](Register-PwshToClaudeDesktop.md)
- [Stop-AllPwsh](Stop-AllPwsh.md)

