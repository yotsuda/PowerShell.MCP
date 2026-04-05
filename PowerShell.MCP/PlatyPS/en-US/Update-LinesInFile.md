---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Update-LinesInFile
---

# Update-LinesInFile

## SYNOPSIS

Replace or delete specific lines in a text file

## SYNTAX

### Path

```
Update-LinesInFile [-Path] <string[]> [[-Content] <Object[]>] [-LineRange <string[]>]
 [-Skip <int>] [-First <int>] [-Encoding <string>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath

```
Update-LinesInFile [[-Content] <Object[]>] -LiteralPath <string[]> [-LineRange <string[]>]
 [-Skip <int>] [-First <int>] [-Encoding <string>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Replaces specified line range with new content.
Lines can expand or shrink.

Update-LinesInFile file.txt -LineRange 5 -Content "replaced"           # replace line 5

Update-LinesInFile file.txt -LineRange 5,10 -Content "single line"     # replace 6 lines with 1

Update-LinesInFile file.txt -LineRange 5,10 -Content @()               # delete lines 5-10

Update-LinesInFile file.txt -Content @("line1", "line2")               # replace entire file

## EXAMPLES

### Basic usage

Update-LinesInFile file.txt -LineRange 5 -Content "replaced"
Update-LinesInFile file.txt -LineRange 5,10 -Content @()
Update-LinesInFile file.txt -Content @("new content")

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

New content.
Use @() to delete lines.

```yaml
Type: System.Object[]
DefaultValue: None
SupportsWildcards: false
Aliases:
- [NewLines]()
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

### -First

Number of lines to process.
Use with `-Skip` to define a range, or alone to process from the beginning.
Mapped to `-LineRange` internally (e.g., `-First 20` becomes `-LineRange 1-20`).

```yaml
Type: System.Nullable`1[System.Int32]
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

### -Skip

Number of lines to skip from the beginning.
Use with `-First` to define a window (e.g., `-Skip 200 -First 50` becomes `-LineRange 201-250`).
Use alone to skip lines and process to end of file (e.g., `-Skip 100` becomes `-LineRange 101,-1`).

```yaml
Type: System.Nullable`1[System.Int32]
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

### -LineRange

Line range to replace.
Accepts: `5` (single line), `5,10` (range), `5-10` (dash format).
Omit to replace entire file.

```yaml
Type: System.String[]
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

- `-Content @()` deletes lines (empty array).

- Omitting `-LineRange` replaces entire file.

- To pass content containing `$`, backticks, or quotes, use the `var1` parameter of `invoke_expression`: `Update-LinesInFile path -LineRange 5 -Content $var1`


## RELATED LINKS

- [Add-LinesToFile]()
- [Update-MatchInFile]()
- [Remove-LinesFromFile]()
- [Show-TextFiles]()

