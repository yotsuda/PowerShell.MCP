---
document type: cmdlet
external help file: PowerShell.MCP-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Get-MCPProxyPath
---

# Get-MCPProxyPath

## SYNOPSIS

Gets the path to the PowerShell.MCP.Proxy executable for the current platform.

## SYNTAX

### __AllParameterSets

```
Get-MCPProxyPath [-Escape] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Returns the full path to the platform-specific PowerShell.MCP.Proxy executable.
Use this path in your MCP client configuration.

## EXAMPLES

### EXAMPLE 1

Get-MCPProxyPath
Returns: C:\Program Files\PowerShell\7\Modules\PowerShell.MCP\bin\win-x64\PowerShell.MCP.Proxy.exe

### EXAMPLE 2

Get-MCPProxyPath -Escape
Returns: C:\\Program Files\\PowerShell\\7\\Modules\\PowerShell.MCP\\bin\\win-x64\\PowerShell.MCP.Proxy.exe

## PARAMETERS

### -Escape

If specified, escapes backslashes for use in JSON configuration files.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
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

### System.String

Full path to the PowerShell.MCP.Proxy executable for the current platform.

## NOTES

## RELATED LINKS

- [Get-MCPOwner]()
- [Register-PwshToClaudeCode]()
- [Register-PwshToClaudeDesktop]()
- [Stop-AllPwsh]()

