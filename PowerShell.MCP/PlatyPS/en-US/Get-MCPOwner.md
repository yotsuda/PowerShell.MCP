---
external help file: PowerShell.MCP-help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Get-MCPOwner

## SYNOPSIS
Gets information about the MCP client that owns this console.

## SYNTAX

```
Get-MCPOwner [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Returns ownership information for the current PowerShell console, including
whether it is owned by an MCP proxy, the proxy's PID, the agent ID,
and the client name (e.g., Claude Desktop, Claude Code, VS Code).

## EXAMPLES

### Example 1: Owned console
```powershell
Get-MCPOwner
```

```
Owned      : True
ProxyPid   : 22208
AgentId    : cc19706b
ClientName : Claude Desktop
```

### Example 2: Unowned console
```powershell
Get-MCPOwner
```

```
Owned      : False
ProxyPid   :
AgentId    :
ClientName :
```

## PARAMETERS

### -ProgressAction
{{ Fill ProgressAction Description }}

```yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### PSCustomObject with Owned, ProxyPid, AgentId, and ClientName properties

## NOTES

## RELATED LINKS
