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

### Ask (Default)

```
Register-PwshToClaudeCode [<CommonParameters>]
```

### Disable

```
Register-PwshToClaudeCode -DisableBuiltInShellTools [<CommonParameters>]
```

### Keep

```
Register-PwshToClaudeCode -KeepBuiltInShellTools [<CommonParameters>]
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

Claude Code can also run commands in hidden shells of its own: the built-in Bash and PowerShell
tools. The user cannot see what runs there, and the AI tends to reach for them out of habit.
Once registration succeeds, the cmdlet offers to disable them by adding "Bash" and "PowerShell"
to permissions.deny in the user settings (~/.claude/settings.json, or settings.json under
CLAUDE_CONFIG_DIR).
It asks when it runs in an interactive console, including when an AI runs it through the pwsh
MCP server (the question appears in the shared console, for the user to answer).
Anywhere it cannot ask, it changes nothing and explains the parameters below instead.

## EXAMPLES

### EXAMPLE 1

Register-PwshToClaudeCode

Registers the server, then asks whether to disable the built-in shell tools.

### EXAMPLE 2

Register-PwshToClaudeCode -DisableBuiltInShellTools

Registers the server and disables the built-in shell tools without asking.

### EXAMPLE 3

Update-Module PowerShell.MCP
Register-PwshToClaudeCode

Points Claude Code at the proxy of the version just installed.

## PARAMETERS

### -DisableBuiltInShellTools

Disables Claude Code's built-in Bash and PowerShell tools without asking, so that every command
runs in the pwsh console where the user can see it.
If the pwsh MCP server ever fails to start, Claude Code then has no shell until "Bash" and
"PowerShell" are removed from permissions.deny again.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Disable
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -KeepBuiltInShellTools

Leaves the built-in tools as they are, without asking.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: False
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Keep
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

### None. Passes through output from the claude CLI.

None.

## NOTES

## RELATED LINKS

- [Register-PwshToClaudeDesktop]()

