---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Show-TextFiles

## SYNOPSIS
Display text file contents with line numbers

## SYNTAX

### Path
```
Show-TextFiles [-Path] <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>] [-Recurse]
 [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### LiteralPath
```
Show-TextFiles -LiteralPath <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>]
 [-Recurse] [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Displays file contents with line numbers. Matching lines highlighted with context.

    Show-TextFiles file.txt                          # entire file

    Show-TextFiles file.txt -LineRange 10,20         # lines 10-20

    Show-TextFiles file.txt -LineRange -10           # last 10 lines

    Show-TextFiles file.txt -Pattern "error"         # regex search with context

    Show-TextFiles file.txt -Contains "[Error]"      # literal search (no escaping needed)

    Show-TextFiles file.txt -Contains "line1`nline2" # multiline literal search (whole-file mode)

    Show-TextFiles *.md,*.txt -Recurse -Pattern "TODO" # recursive search with multiple extensions

## EXAMPLES

### Example 1: Basic usage
```powershell
Show-TextFiles file.txt
Show-TextFiles file.txt -LineRange 10,20
Show-TextFiles file.txt -Pattern "error"
Show-TextFiles *.md,*.txt -Recurse -Pattern "TODO"
```

## PARAMETERS

### -Path
File path(s). Supports wildcards. With `-Recurse`, wildcard patterns (e.g., `*.md,*.txt`) filter files by extension across all subdirectories.

```yaml
Type: String[]
Parameter Sets: Path
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: True
```

### -LiteralPath
Literal file path(s) without wildcard expansion.

```yaml
Type: String[]
Parameter Sets: LiteralPath
Aliases: PSPath

Required: True
Position: Named
Default value: None
Accept pipeline input: True (ByPropertyName)
Accept wildcard characters: False
```

### -LineRange
Line range (e.g., 5 or 5,10). Negative value = tail count.

```yaml
Type: Int32[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Pattern
Regex pattern. Matches shown with context.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Contains
Literal string search. No regex escaping needed. Supports multiline strings (newlines allowed) for whole-file search mode. Cannot be combined with `-Pattern` when multiline. Cannot be used with `-Recurse` when multiline.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Recurse
Search subdirectories. Requires -Pattern or -Contains.

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

### -Encoding
Character encoding. Auto-detected if omitted.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

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

## NOTES
- `-LineRange -10` = last 10 lines (single negative value = tail)
- `-Contains` and `-Pattern` can be combined (OR condition)
- `-Contains` supports multiline strings for whole-file search mode (like `Update-MatchInFile -OldText`)
- Multiline `-Contains` cannot be combined with `-Pattern` or `-Recurse`
- `-Recurse` requires `-Pattern` or `-Contains`
- `-Recurse` with wildcard `-Path` (e.g., `*.cs`) filters files by extension
- `-LiteralPath` for paths with `[`, `]`, `*`, `?`

## RELATED LINKS
