---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Update-LinesInFile

## SYNOPSIS
Replace or delete specific lines in a text file

## SYNTAX

### Path
```
Update-LinesInFile [-Path] <String[]> [-LineRange <Int32[]>] [[-Content] <Object[]>] [-Encoding <String>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Update-LinesInFile -LiteralPath <String[]> [-LineRange <Int32[]>] [[-Content] <Object[]>] [-Encoding <String>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Replaces specified line range with new content. Lines can expand or shrink.

    Update-LinesInFile file.txt -LineRange 5 -Content "replaced"           # replace line 5

    Update-LinesInFile file.txt -LineRange 5,10 -Content "single line"     # replace 6 lines with 1

    Update-LinesInFile file.txt -LineRange 5,10 -Content @()               # delete lines 5-10

    Update-LinesInFile file.txt -Content @("line1", "line2")               # replace entire file

## EXAMPLES

### Example 1: Basic usage
```powershell
Update-LinesInFile file.txt -LineRange 5 -Content "replaced"
Update-LinesInFile file.txt -LineRange 5,10 -Content @()
Update-LinesInFile file.txt -Content @("new content")
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
Line range to replace (e.g., 5 or 5,10). Omit to replace entire file.

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

### -Content
New content. Use @() to delete lines.

```yaml
Type: Object[]
Parameter Sets: (All)
Aliases:

Required: False
Position: 1
Default value: None
Accept pipeline input: True (ByValue)
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
Shows what would happen without modifying.

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
- `-Content @()` deletes lines (empty array)
- Omitting `-LineRange` replaces entire file
