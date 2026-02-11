---
external help file: PowerShell.MCP-help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Install-ClaudeSkill

## SYNOPSIS
Installs PowerShell.MCP skills for Claude Code.

## SYNTAX

```
Install-ClaudeSkill [[-Name] <String[]>] [-Force] [-PassThru] [-ProgressAction <ActionPreference>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Copies skill files from the PowerShell.MCP module to the Claude Code skills directory
(~/.claude/skills/).
These skills provide slash commands for common PowerShell.MCP operations.

## EXAMPLES

### Example 1: Install all skills
```powershell
Install-ClaudeSkill
```

Installs all available skills to ~/.claude/skills/

### Example 2: Install specific skills
```powershell
Install-ClaudeSkill ps-analyze, ps-learn
```

Installs only the specified skills.

### Example 3: Preview installation
```powershell
Install-ClaudeSkill -WhatIf
```

Shows which skills would be installed without actually installing them.

### Example 4: Force overwrite
```powershell
Install-ClaudeSkill ps-analyze -Force
```

Installs the skill, overwriting if it already exists.

## PARAMETERS

### -Name
Specifies the names of skills to install.
If not specified, all available skills are installed.
Available skills: ps-analyze, ps-create-procedure, ps-dictation, ps-exec-procedure, ps-html-guidelines, ps-learn, ps-map

```yaml
Type: String[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -Force
Overwrites existing skill files without prompting.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -PassThru
Returns the installed skill file objects.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

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

### System.IO.FileInfo (when -PassThru is specified)

## NOTES

## RELATED LINKS
