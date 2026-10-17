---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 10/09/2026
PlatyPS schema version: 2024-05-01
title: Disable-MCPRemoteControl
---

# Disable-MCPRemoteControl

## SYNOPSIS

End remote control of this console

## SYNTAX

### __AllParameterSets

```
Disable-MCPRemoteControl [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Ends what Enable-MCPRemoteControl started — listening, or dialing an MCP server on
another machine — and drops the remote session, if one is connected. A command the
remote AI sent that has not started yet does not run; one already running keeps
running until it finishes. Either way its result is discarded, not left for whoever
uses the console next. The console becomes a plain MCP console again, which a local
MCP server may claim. Warns when the console is not under remote control.

Remote control also ends by itself: when the AI disconnects a console that dialed in,
when nobody pairs or reconnects within 10 minutes, and, for a listening console, after
five forged pairing attempts. A firewall rule you added for it stays; remove it with
Remove-NetFirewallRule -DisplayName 'PowerShell.MCP remote console'.

## EXAMPLES

### Stop

Disable-MCPRemoteControl

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES

- Removing the module (Remove-Module PowerShell.MCP) ends remote control as well.


## RELATED LINKS

- [Enable-MCPRemoteControl]()
