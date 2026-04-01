---
document type: cmdlet
external help file: PowerShell.MCP-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Register-PwshToClaudeCode
---

# Register-PwshToClaudeCode

## SYNOPSIS

Registers PowerShell.MCP as an MCP server in Claude Code.

## SYNTAX

### __AllParameterSets

```
Register-PwshToClaudeCode [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Runs 'claude mcp add pwsh -s user' with the current module's
proxy executable path.
If a legacy "PowerShell" entry pointing to
PowerShell.MCP.Proxy exists, it is removed first.

## EXAMPLES

### EXAMPLE 1

Register-PwshToClaudeCode

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### None. Passes through output from the claude CLI.

None.

## NOTES

## RELATED LINKS

- [Register-PwshToClaudeDesktop]()

