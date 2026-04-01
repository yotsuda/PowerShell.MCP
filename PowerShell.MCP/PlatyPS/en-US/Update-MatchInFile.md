---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Update-MatchInFile
---

# Update-MatchInFile

## SYNOPSIS

Replace text in a file using literal string or regex pattern

## SYNTAX

### Path

```
Update-MatchInFile [-Path] <string[]> [-OldText <string>] [-Pattern <string>]
 [-Replacement <string>] [-LineRange <string[]>] [-Encoding <string>] [-Backup] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

### LiteralPath

```
Update-MatchInFile -LiteralPath <string[]> [-OldText <string>] [-Pattern <string>]
 [-Replacement <string>] [-LineRange <string[]>] [-Encoding <string>] [-Backup] [-WhatIf] [-Confirm]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Replaces matching text (literal or regex) within optional line range.
OldText supports multiline strings (whole-file replacement mode).

Update-MatchInFile file.txt -OldText "foo" -Replacement "bar"          # literal replacement

Update-MatchInFile file.txt -Pattern "v\d+" -Replacement "v2"          # regex replacement

Update-MatchInFile file.txt -LineRange 10,20 -OldText "old" -Replacement "new"  # within range

Update-MatchInFile file.txt -LineRange 10 -OldText "old" -Replacement "new"     # single line

## EXAMPLES

### Basic usage

Update-MatchInFile file.txt -OldText "foo" -Replacement "bar"
Update-MatchInFile file.txt -Pattern "v\d+" -Replacement "v2"
Update-MatchInFile file.txt -LineRange 10,20 -OldText "old" -Replacement "new"

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

Limits replacement to specific lines.
Accepts: `5` (single line), `10,20` (range), `10-20` (dash format).

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
- PSPath
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

### -OldText

Literal string to find and replace.
Supports multiline strings (newlines allowed).

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases:
- Contains
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

Regex pattern to find.

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

### -Replacement

Replacement text.

```yaml
Type: System.String
DefaultValue: None
SupportsWildcards: false
Aliases:
- NewText
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

- `-OldText` (literal) and `-Pattern` (regex) are mutually exclusive.

- `-OldText` supports multiline strings for whole-file replacement mode.

- `-WhatIf` shows detailed preview with highlighting.

- Newlines in `-Replacement` are normalized to match file's newline style.

- To pass text containing `$`, backticks, or quotes, use `var1`/`var2` parameters of `invoke_expression`: `Update-MatchInFile path -OldText $var1 -Replacement $var2`


## RELATED LINKS

- [Add-LinesToFile](Add-LinesToFile.md)
- [Update-LinesInFile](Update-LinesInFile.md)
- [Remove-LinesFromFile](Remove-LinesFromFile.md)
- [Show-TextFiles](Show-TextFiles.md)

