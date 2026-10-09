---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 10/09/2026
PlatyPS schema version: 2024-05-01
title: Enable-MCPRemoteControl
---

# Enable-MCPRemoteControl

## SYNOPSIS

Let an MCP server on another machine drive this console

## SYNTAX

### __AllParameterSets

```
Enable-MCPRemoteControl [[-Port] <int>] [-ListenAddress <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Binds a TCP port and waits for a PowerShell.MCP proxy on another machine.
Prints a pairing code, this machine's host name and its IP addresses; the user gives
those to the AI on the other machine, which calls connect_remote_console and then runs
commands here with execute_remote_command. The user at this machine keeps the prompt
and sees every command the AI runs, in this window; a Read-Host is answered here.

The console must be one the user opened (Import-Module PowerShell.MCP), not one a
local MCP server started. A second console on the same port is refused. One proxy
at a time; the listener keeps waiting after a disconnect, and the same code reconnects.
It stops after five failed pairing attempts, with Disable-MCPRemoteControl, or when the
module is removed.

Security: TLS under a certificate generated for this listener; the pairing handshake
is bound to that certificate and to the code (PBKDF2-SHA256, 300,000 iterations), and
the console proves first, so a proxy that reaches a wrong address sends nothing.
Meant for a LAN or VPN; do not expose the port to the Internet. Both machines must
run the same PowerShell.MCP version.

Inbound TCP on the port must be allowed through this machine's firewall. On Windows,
as administrator:

New-NetFirewallRule -DisplayName 'PowerShell.MCP remote console' -Direction Inbound -Protocol TCP -LocalPort 42777 -Action Allow

## EXAMPLES

### Default port

Enable-MCPRemoteControl

### Another port, one address only

Enable-MCPRemoteControl -Port 5000 -ListenAddress 192.168.0.10

## PARAMETERS

### -Port

TCP port to listen on. Default 42777.

```yaml
Type: System.Int32
DefaultValue: 42777
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -ListenAddress

'Any' (IPv4 and IPv6, the default) or one IP address of this machine to bind.

```yaml
Type: System.String
DefaultValue: Any
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES

- The pairing code is shown once, in this window. It is not written anywhere else.

- The window title shows the state: `PowerShell.MCP [remote control: listening :42777]`, then `[remote control: connected from <address>]`.

- A local MCP server on this machine does not claim a listening console.


## RELATED LINKS

- [Disable-MCPRemoteControl]()
