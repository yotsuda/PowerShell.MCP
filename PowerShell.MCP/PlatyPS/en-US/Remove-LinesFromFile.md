---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Remove-LinesFromFile

## SYNOPSIS
Remove lines from a text file by line range or pattern matching

## SYNTAX

### Path
```
Remove-LinesFromFile [-Path] <String[]> [-LineRange <Int32[]>] [-Contains <String>] [-Pattern <String>]
 [-Encoding <String>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Remove-LinesFromFile -LiteralPath <String[]> [-LineRange <Int32[]>] [-Contains <String>] [-Pattern <String>]
 [-Encoding <String>] [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Removes lines by range, literal match, or regex. Range + pattern = AND condition.

    Remove-LinesFromFile file.txt -LineRange 5,10                   # remove lines 5-10

    Remove-LinesFromFile file.txt -LineRange -10                    # remove last 10 lines

    Remove-LinesFromFile file.txt -Pattern "^#"                     # remove all comment lines

    Remove-LinesFromFile file.txt -Contains "DEBUG"                 # remove lines containing "DEBUG"

    Remove-LinesFromFile file.txt -LineRange 1,100 -Pattern "TODO"  # AND condition

## EXAMPLES

### Example 1: Basic usage
```powershell
Remove-LinesFromFile file.txt -LineRange 5,10
Remove-LinesFromFile file.txt -Pattern "^#"
Remove-LinesFromFile file.txt -Contains "DEBUG"
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
Line range to remove (e.g., 5 or 5,10). Negative value = tail count.

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

### -Contains
Literal string to match lines for removal.

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
Regex pattern to match lines for removal.

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

## INPUTS

## OUTPUTS

## NOTES
- At least one of `-LineRange`, `-Contains`, or `-Pattern` required
- `-Contains` and `-Pattern` are mutually exclusive
- `-LineRange` + `-Pattern`/`-Contains` = AND condition


## RELATED LINKS
