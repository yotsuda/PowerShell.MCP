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

Registers this module's proxy executable as the 'pwsh' MCP server at user scope, via the claude CLI.

If a 'pwsh' entry already exists at that scope (typically written by an earlier install, and still
pointing at that install's proxy), it is replaced, because 'claude mcp add' refuses a name that is
already registered.
The arguments and environment variables the old entry carried (proxy flags such as --no-profile,
settings such as POWERSHELL_MCP_TIMEOUT_CEILING) are carried over to the new one, and if the new
entry cannot be added the old one is put back.

An entry at local or project scope for the current directory takes precedence over the user-scope
entry there. It is left alone, and a warning names it.

A legacy "PowerShell" entry pointing at PowerShell.MCP.Proxy is removed first.

## EXAMPLES

### EXAMPLE 1

Register-PwshToClaudeCode

### EXAMPLE 2

Update-Module PowerShell.MCP
Register-PwshToClaudeCode

Points Claude Code at the proxy of the version just installed.

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

