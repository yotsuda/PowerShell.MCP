---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Update-MatchInFile

## SYNOPSIS
Replace text in a file using literal string or regex pattern

## SYNTAX

### Path
```
Update-MatchInFile [-Path] <String[]> [-OldText <String>] [-Pattern <String>] [-Replacement <String>] [-LineRange <Int32[]>] [-Encoding <String>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Update-MatchInFile -LiteralPath <String[]> [-OldText <String>] [-Pattern <String>] [-Replacement <String>] [-LineRange <Int32[]>] [-Encoding <String>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Replaces matching text (literal or regex) within optional line range.

    Update-MatchInFile file.txt -OldText "foo" -Replacement "bar"          # literal replacement

    Update-MatchInFile file.txt -Pattern "v\d+" -Replacement "v2"          # regex replacement

    Update-MatchInFile file.txt -LineRange 10,20 -OldText "old" -Replacement "new"  # within range

## EXAMPLES

### Example 1: Basic usage
```powershell
Update-MatchInFile file.txt -OldText "foo" -Replacement "bar"
Update-MatchInFile file.txt -Pattern "v\d+" -Replacement "v2"
Update-MatchInFile file.txt -LineRange 10,20 -OldText "old" -Replacement "new"
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

### -OldText
Literal string to find and replace.

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

### -Pattern
Regex pattern to find.

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

### -Replacement
Replacement text.

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

### -LineRange
Limits replacement to specific lines (e.g., 5 or 10,20).

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

### -Backup
Creates a backup file before modifying.

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
Shows detailed preview of changes without modifying.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts for confirmation before running.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## NOTES
- `-OldText` (literal) and `-Pattern` (regex) are mutually exclusive
- `-WhatIf` shows detailed preview with highlighting
- Newlines in `-Replacement` are normalized to match file's newline style
