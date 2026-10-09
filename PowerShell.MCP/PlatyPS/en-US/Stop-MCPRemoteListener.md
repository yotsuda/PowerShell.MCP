---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 10/09/2026
PlatyPS schema version: 2024-05-01
title: Stop-MCPRemoteListener
---

# Stop-MCPRemoteListener

## SYNOPSIS

Stop listening for a remote MCP server

## SYNTAX

### __AllParameterSets

```
Stop-MCPRemoteListener [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Stops the listener started by Start-MCPRemoteListener and drops the remote session,
if one is connected. The console becomes a plain MCP console again, which a local MCP
server may claim. Warns when the console is not listening.

## EXAMPLES

### Stop

Stop-MCPRemoteListener

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES

- Removing the module (Remove-Module PowerShell.MCP) stops the listener as well.


## RELATED LINKS

- [Start-MCPRemoteListener]()
