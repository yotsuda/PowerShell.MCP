---
document type: cmdlet
external help file: PowerShell.MCP-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Register-PwshToClaudeDesktop
---

# Register-PwshToClaudeDesktop

## SYNOPSIS

Registers PowerShell.MCP as an MCP server in Claude Desktop.

## SYNTAX

### __AllParameterSets

```
Register-PwshToClaudeDesktop [-ConsoleLaunch <string>] [-NoProfile] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Adds or updates the 'pwsh' entry in Claude Desktop's claude_desktop_config.json. Existing settings in
the file are preserved, including the entry's own 'args' and 'env': only its command is repointed at
this module's proxy, so proxy flags (such as --no-profile) and environment settings (such as
POWERSHELL_MCP_TIMEOUT_CEILING) survive a re-registration.

-ConsoleLaunch and -NoProfile set those two proxy flags in the entry's 'args'; a parameter that is
not given leaves its flag as it is.

## EXAMPLES

### Example 1

Register-PwshToClaudeDesktop

Registers PowerShell.MCP in Claude Desktop configuration.

### Example 2

Register-PwshToClaudeDesktop -ConsoleLaunch Minimized

Registers it with consoles opening minimized on Windows, without taking the keyboard focus. Later
re-registrations keep the option.

## PARAMETERS

### -ConsoleLaunch

Sets the proxy's --console-launch option: how consoles open on Windows.
Minimized opens a minimized classic console window that never takes the keyboard focus; Handoff is
the launch before 1.17.0; Auto, the default, removes the option.
Not given, the entry keeps what it has. No effect on macOS and Linux.

```yaml
Type: System.String
DefaultValue: ''
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
AcceptedValues:
- Auto
- Handoff
- Minimized
HelpMessage: ''
```

### -NoProfile

Adds the proxy's --no-profile option, so consoles start without your $PROFILE.
-NoProfile:$false removes it. Not given, the entry keeps what it has.

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

### System.Object

None.

## NOTES


## RELATED LINKS

- [Register-PwshToClaudeCode]()
- [Get-MCPOwner]()
- [Get-MCPProxyPath]()
- [Stop-AllPwsh]()

