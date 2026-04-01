---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Add-LinesToFile
---

# Add-LinesToFile

## SYNOPSIS

Insert lines into a text file at a specific position or at the end

## SYNTAX

### Path

```
Add-LinesToFile [-Path] <string[]> [[-Content] <Object[]>] [-LineNumber <int>] [-Encoding <string>]
 [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath

```
Add-LinesToFile [[-Content] <Object[]>] -LiteralPath <string[]> [-LineNumber <int>]
 [-Encoding <string>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Inserts lines at specified position or appends to end.
Creates new file if not exists.

Add-LinesToFile file.txt -Content "new line"                    # append to end

Add-LinesToFile file.txt -LineNumber 5 -Content "inserted"      # insert at line 5

Add-LinesToFile file.txt -Content @("line1", "line2")           # add multiple lines

Add-LinesToFile new.txt -Content "first line"                   # create new file

## EXAMPLES

### Basic usage

Add-LinesToFile file.txt -Content "new line"
Add-LinesToFile file.txt -LineNumber 1 -Content "first"
Add-LinesToFile file.txt -Content @("line1", "line2")

## PARAMETERS

### -Backup

Creates a backup file before modifying.

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

### -Content

Lines to insert.
String or array of strings.

```yaml
Type: System.Object[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
  IsRequired: false
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Encoding

Character encoding.
Auto-detected if omitted.

```yaml
Type: System.String
DefaultValue: None
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

### -LineNumber

Insert position (1-based).
Omit to append at end.

```yaml
Type: System.Int32
DefaultValue: None
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

### -LiteralPath

Literal file path(s) without wildcard expansion.

```yaml
Type: System.String[]
DefaultValue: None
SupportsWildcards: false
Aliases:
- [PSPath]()
ParameterSets:
- Name: LiteralPath
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Path

File path(s).
Supports wildcards.

```yaml
Type: System.String[]
DefaultValue: None
SupportsWildcards: true
Aliases: []
ParameterSets:
- Name: Path
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: true
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

### System.String[]

File path(s) to process.

### System.Object[]

File path(s) to process.

## OUTPUTS

## NOTES

- Creates the file if it does not exist (no flag needed).
Exception: wildcards in `-Path` cannot create new files.

- Omitting `-LineNumber` appends to end; `-LineNumber 1` inserts at beginning (existing lines shift down).

- To pass content containing `$`, backticks, or quotes, use the `var1` parameter of `invoke_expression`: `Add-LinesToFile path -Content $var1`


## RELATED LINKS

- [Update-LinesInFile]()
- [Update-MatchInFile]()
- [Remove-LinesFromFile]()
- [Show-TextFiles]()

