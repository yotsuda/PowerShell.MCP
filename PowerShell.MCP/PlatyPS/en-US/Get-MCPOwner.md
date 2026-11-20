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
whether it is owned by an MCP proxy, the proxy's PID, the agent ID, the
client name (e.g., Claude Desktop, Claude Code, VS Code), and the name the
proxy gave the console (e.g., Lithium, as in the window title "#43036 Lithium").

ConsoleName is the name as the proxy gave it, so it stays the same if the window title is changed
afterwards. It is empty while the console is not owned, and until its owner has named it.

## EXAMPLES

### EXAMPLE 1

Get-MCPOwner

EngineReady : True
Owned       : True
ProxyPid    : 22208
AgentId     : cc19706b
ClientName  : Claude Desktop
ConsoleName : Lithium
LastError   :

### EXAMPLE 2

Get-MCPOwner

EngineReady : True
Owned       : False
ProxyPid    :
AgentId     :
ClientName  :
ConsoleName :
LastError   :

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### PowerShell.MCP.Status

EngineReady, Owned, ProxyPid, AgentId, ClientName, ConsoleName and LastError properties.

## NOTES

## RELATED LINKS

- [Get-MCPProxyPath]()
- [Register-PwshToClaudeCode]()
- [Register-PwshToClaudeDesktop]()
- [Stop-AllPwsh]()

