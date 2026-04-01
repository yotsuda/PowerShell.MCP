---
document type: cmdlet
external help file: PowerShell.MCP-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Stop-AllPwsh
---

# Stop-AllPwsh

## SYNOPSIS

Stops all pwsh processes to release DLL locks.

## SYNTAX

### __AllParameterSets

```
Stop-AllPwsh [[-PwshPath] <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Stops all pwsh processes on the system to release DLL locks.
Useful for PowerShell module developers before dotnet build.

With -PwshPath: starts a new pwsh session from the specified binary with
PowerShell.MCP loaded, then stops all other pwsh processes.

WARNING: This stops ALL pwsh processes, including those used by other users
or other MCP clients on the same machine.

## EXAMPLES

### EXAMPLE 1

Stop-AllPwsh
Stops all pwsh processes. Use before rebuilding a PowerShell module to release DLL locks.

### EXAMPLE 2

Stop-AllPwsh -PwshPath (Get-PSOutput)
Starts a new session using the built pwsh binary, then stops all other pwsh processes.
Use in the PowerShell repository after Start-PSBuild.

## PARAMETERS

### -Confirm

Prompts you for confirmation before running the cmdlet.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases:
- cf
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

### -PwshPath

Path to the pwsh binary to start.
If specified, a new session is started
from this binary with PowerShell.MCP imported before stopping other processes.

```yaml
Type: System.String
DefaultValue: ''
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

### -WhatIf

Runs the command in a mode that only reports what would happen without performing the actions.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases:
- wi
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

## RELATED LINKS

- [Get-MCPOwner](Get-MCPOwner.md)
- [Get-MCPProxyPath](Get-MCPProxyPath.md)
- [Register-PwshToClaudeCode](Register-PwshToClaudeCode.md)
- [Register-PwshToClaudeDesktop](Register-PwshToClaudeDesktop.md)

