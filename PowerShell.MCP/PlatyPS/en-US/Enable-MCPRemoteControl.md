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

### Listen (Default)

```
Enable-MCPRemoteControl [[-Port] <int>] [-ListenAddress <string>] [-AllowFrom <string[]>] [<CommonParameters>]
```

### Dial

```
Enable-MCPRemoteControl -ConnectTo <string> -Code <string> [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Lets an AI on another machine run commands in this console, with you watching.

Listening (the default): binds a TCP port and waits for a PowerShell.MCP server on
another machine. Prints a pairing code, this machine's host name and its IP
addresses; you give those to the AI on the other machine, which calls
connect_remote_console and then runs commands here with execute_remote_command.

Dialing (-ConnectTo): connects to the machine where the AI called
accept_remote_console, for a console behind NAT or a firewall that cannot be opened;
only an outbound connection is needed here. The AI gives you the line to run (or the
one-line bootstrap, which runs it for you).

Either way you keep the prompt and see every command the AI runs, in this window; a
Read-Host is answered here. While remote control is on, every prompt starts with
[AI from <host>] (the path is still this machine's), and the window title says so.

Before it starts, it asks you to confirm, saying which user the commands will run as
and whether elevated; the default answer is No, and there is no switch to skip the
question. It refuses to run where nobody could answer or watch: a service or scheduled
task, redirected input, PowerShell started with -NonInteractive, or a host that is not
a console. The console must be one you opened (Import-Module PowerShell.MCP), not one a
local MCP server started.

The pairing code is 12 characters and works for one connection, within 10 minutes. A
dropped connection reconnects by itself with a key the pairing handed over. When the
AI disconnects, a listening console shows a new code; if nobody connects within 10
minutes, remote control stops. It also stops after five failed pairing attempts, with
Disable-MCPRemoteControl, or when the module is removed. A second console on the same
port is refused, and one MCP server at a time can connect.

Security: TLS under a certificate made for the session; the pairing handshake is bound
to that certificate and to the code (PBKDF2-SHA256, 300,000 iterations). The side that
accepts the connection proves first, so a peer that reaches a wrong address learns
nothing, but anyone who reaches the port can try codes offline: hence the short-lived,
single-use code. Meant for a LAN or VPN; use -AllowFrom on any network you do not fully
trust, and do not expose the port to the Internet. Both machines must run the same
PowerShell.MCP version. See the Remote Control section of SECURITY.md.

Inbound TCP on the port must be allowed through this machine's firewall. On Windows,
as administrator, for the AI's machine only, and removed when you are done:

New-NetFirewallRule -DisplayName 'PowerShell.MCP remote console' -Direction Inbound -Protocol TCP -LocalPort 42777 -RemoteAddress <IP of the AI's machine> -Action Allow

Remove-NetFirewallRule -DisplayName 'PowerShell.MCP remote console'

## EXAMPLES

### Listen, for one machine only

Enable-MCPRemoteControl -AllowFrom 192.168.0.10

Connections from any other address are closed before TLS.

### Another port, one local address

Enable-MCPRemoteControl -Port 5000 -ListenAddress 192.168.0.20

### Dial the AI's machine instead (this machine behind NAT or a firewall)

Enable-MCPRemoteControl -ConnectTo my-pc:51234 -Code 7Q4K-M2XP-H9RT

The AI ran accept_remote_console on its machine and gave you this line; only an
outbound connection is needed here.

## PARAMETERS

### -Port

TCP port to listen on. Default 42777.

```yaml
Type: System.Int32
DefaultValue: 42777
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Listen
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
- Name: Listen
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -AllowFrom

IP addresses allowed to connect (the machine the AI runs on). Connections from any
other address are closed before TLS, and the printed firewall rule is scoped to them.
IPv4 addresses must be complete (all four numbers).

```yaml
Type: System.String[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Listen
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -ConnectTo

Dial the MCP server instead of listening: host:port of the machine where the AI called
accept_remote_console (the port is in its answer). For a console behind NAT or a
firewall that cannot be opened. The connection is redialed by itself if it drops.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Dial
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Code

The pairing code accept_remote_console gave the AI, which the AI passes on. Case and
dashes do not matter. It works once, within 10 minutes.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Dial
  Position: Named
  IsRequired: true
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

- The pairing code is shown in this window (and stays in its scrollback and in any transcript you are recording); in dial mode it is part of the line you were given. It is worthless once used or after 10 minutes.

- The window title shows the state, as the prompt does: `AI from <host>` while connected, `AI remote control: waiting` otherwise, after `Administrator: ` in an elevated console.

- A local MCP server on this machine does not claim a console under remote control, and other local requests to it are refused.

- Everything the AI reads here may have been written by someone else (logs, file names, file contents). The AI is told to treat it as data; ask it for read-only work first, and to confirm before changes.


## RELATED LINKS

- [Disable-MCPRemoteControl]()
