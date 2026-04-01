---
document type: cmdlet
external help file: PowerShell.MCP.dll-Help.xml
HelpUri: ''
Locale: ja-JP
Module Name: PowerShell.MCP
ms.date: 04/01/2026
PlatyPS schema version: 2024-05-01
title: Show-TextFiles
---

# Show-TextFiles

## SYNOPSIS

Display file contents with line numbers, or search across files with regex or literal patterns

## SYNTAX

### Path

```
Show-TextFiles [-Path] <string[]> [-LineRange <string[]>] [-Pattern <string>] [-Contains <string>]
 [-Recurse] [-Encoding <string>] [<CommonParameters>]
```

### LiteralPath

```
Show-TextFiles -LiteralPath <string[]> [-LineRange <string[]>] [-Pattern <string>]
 [-Contains <string>] [-Recurse] [-Encoding <string>] [<CommonParameters>]
```

## ALIASES

This cmdlet has no aliases.

## DESCRIPTION

Display file contents with line numbers, or search across files using regex/literal patterns with highlighted matches and context lines.

Show-TextFiles file.txt                          # entire file

Show-TextFiles file.txt -LineRange 10,20         # lines 10-20     Show-TextFiles file.txt -LineRange 10-20         # same (dash format)

Show-TextFiles file.txt -LineRange -10           # last 10 lines

Show-TextFiles file.txt -Pattern "error"         # regex search with context

Show-TextFiles file.txt -Contains "[Error]"      # literal search (no escaping needed)

Show-TextFiles file.txt -Contains "line1`nline2" # multiline literal search (whole-file mode)

Show-TextFiles app.?? -Recurse -Pattern "TODO"   # recursive wildcard search

## EXAMPLES

### Basic usage

Show-TextFiles file.txt
Show-TextFiles file.txt -LineRange 10,20
Show-TextFiles file.txt -Pattern "error"
Show-TextFiles *.cs -Recurse -Pattern "class\s+\w+"
Show-TextFiles *.log -Recurse -Contains "[FATAL]"

## PARAMETERS

### -Contains

Literal string search.
No regex escaping needed.
Supports multiline strings (newlines allowed) for whole-file search mode.
Cannot be combined with `-Pattern` when multiline.
Cannot be used with `-Recurse` when multiline.

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

Line range.
Accepts: `5` (single line), `10,20` (range), `10-20` (dash format).
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
With `-Recurse`, wildcard patterns filter files by extension across all subdirectories.

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

Regex pattern.
Matches shown with context.

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

### -Recurse

Search subdirectories.
Requires -Pattern or -Contains.

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

### System.String[]

File path(s) to process.

## OUTPUTS

## NOTES

- `-LineRange -10` = last 10 lines (single negative value = tail)

- `-Contains` and `-Pattern` can be combined (OR condition)

- `-Contains` supports multiline strings for whole-file search mode (like `Update-MatchInFile -OldText`)

- Multiline `-Contains` cannot be combined with `-Pattern` or `-Recurse`

- `-Recurse` requires `-Pattern` or `-Contains`

- `-Recurse` with wildcard `-Path` (e.g., `*.cs`) filters files by extension

- `-LiteralPath` for paths with `[`, `]`, `*`, `?`


## RELATED LINKS

- [Add-LinesToFile]()
- [Update-LinesInFile]()
- [Update-MatchInFile]()
- [Remove-LinesFromFile]()

