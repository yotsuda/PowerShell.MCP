---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Show-TextFile

## SYNOPSIS
Display text file contents with line numbers

## SYNTAX

### Path
```
Show-TextFile [-Path] <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>] [-Recurse]
 [-Encoding <String>] [<CommonParameters>]
```

### LiteralPath
```
Show-TextFile -LiteralPath <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>]
 [-Recurse] [-Encoding <String>] [<CommonParameters>]
```

## DESCRIPTION
Displays file contents with line numbers. Matching lines highlighted with context.

    Show-TextFile file.txt                          # entire file

    Show-TextFile file.txt -LineRange 10,20         # lines 10-20

    Show-TextFile file.txt -LineRange -10           # last 10 lines

    Show-TextFile file.txt -Pattern "error"         # regex search with context

    Show-TextFile file.txt -Contains "[Error]"      # literal search (no escaping needed)

    Show-TextFile . -Recurse -Pattern "TODO"        # recursive directory search

## EXAMPLES

### Example 1: Basic usage
```powershell
Show-TextFile file.txt
Show-TextFile file.txt -LineRange 10,20
Show-TextFile file.txt -Pattern "error"
Show-TextFile . -Recurse -Contains "TODO"
```

## PARAMETERS

### -Path
File path(s). Supports wildcards.

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
Literal string search. No regex escaping needed.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
- `-LineRange -10` = last 10 lines (single negative value = tail)
- `-Contains` and `-Pattern` can be combined (OR condition)
- `-Recurse` requires `-Pattern` or `-Contains`
- `-LiteralPath` for paths with `[`, `]`, `*`, `?`


## RELATED LINKS
