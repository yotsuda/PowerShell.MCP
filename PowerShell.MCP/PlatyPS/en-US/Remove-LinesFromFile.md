---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Remove-LinesFromFile
---

# Remove-LinesFromFile

## SYNOPSIS

Remove lines from a text file by line range or pattern matching

## SYNTAX

### Path

```
Remove-LinesFromFile [-Path] <string[]> [-LineRange <string[]>] [-Skip <int>] [-First <int>]
 [-Contains <string>] [-Pattern <string>] [-Encoding <string>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath

```
Remove-LinesFromFile -LiteralPath <string[]> [-LineRange <string[]>] [-Skip <int>] [-First <int>]
 [-Contains <string>] [-Pattern <string>] [-Encoding <string>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Removes lines by range, literal match, or regex.
Range + pattern = AND condition.

Remove-LinesFromFile file.txt -LineRange 5,10                   # remove lines 5-10

Remove-LinesFromFile file.txt -LineRange -10                    # remove last 10 lines

Remove-LinesFromFile file.txt -Pattern "^#"                     # remove all comment lines

Remove-LinesFromFile file.txt -Contains "DEBUG"                 # remove lines containing "DEBUG"

Remove-LinesFromFile file.txt -Contains "DEBUG" -Pattern "^#"   # OR condition (either match)

Remove-LinesFromFile file.txt -Contains "line1`nline2"          # multiline removal (whole-file mode)

Remove-LinesFromFile file.txt -LineRange 1,100 -Pattern "TODO"  # AND condition

## EXAMPLES

### Basic usage

Remove-LinesFromFile file.txt -LineRange 5,10
Remove-LinesFromFile file.txt -Pattern "^#"
Remove-LinesFromFile file.txt -Contains "DEBUG"

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

### -Contains

Literal string to match lines for removal.
Can be combined with `-Pattern` (OR condition).
Supports multiline strings (newlines allowed) for whole-file removal mode.
Cannot be combined with `-Pattern` when multiline.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Path
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
- Name: LiteralPath
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
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

Line range to remove.
Accepts: `5` (single line), `5,10` (range), `5-10` (dash format).
Negative value = tail count (e.g., `-10` = last 10 lines).

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

### -Pattern

Regex pattern to match lines for removal.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: Path
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
- Name: LiteralPath
  Position: Named
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

### System.String[]

File path(s) to process.

## OUTPUTS

## NOTES

- At least one of `-LineRange`, `-Contains`, or `-Pattern` required.

- `-Contains` and `-Pattern` can be combined (OR condition).

- `-Contains` supports multiline strings for whole-file removal mode (like `Update-MatchInFile -OldText`).

- Multiline `-Contains` cannot be combined with `-Pattern` or tail `-LineRange`.

- `-LineRange` + `-Pattern`/`-Contains` = AND condition.

- To pass literal text containing `$`, backticks, or quotes to `-Contains`, use the `var1` parameter of `invoke_expression`: `Remove-LinesFromFile path -Contains $var1`


## RELATED LINKS

- [Add-LinesToFile]()
- [Update-LinesInFile]()
- [Update-MatchInFile]()
- [Show-TextFiles]()

