---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Add-LinesToFile

## SYNOPSIS
Insert lines into a text file at a specific position or at the end

## SYNTAX

### Path
```
Add-LinesToFile [-Path] <String[]> [[-Content] <Object[]>] [-LineNumber <Int32>] [-Encoding <String>] [-Backup]
 [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Add-LinesToFile -LiteralPath <String[]> [[-Content] <Object[]>] [-LineNumber <Int32>] [-Encoding <String>]
 [-Backup] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Inserts lines at specified position or appends to end. Creates new file if not exists.

    Add-LinesToFile file.txt -Content "new line"                    # append to end

    Add-LinesToFile file.txt -LineNumber 5 -Content "inserted"      # insert at line 5

    Add-LinesToFile file.txt -Content @("line1", "line2")           # add multiple lines

    Add-LinesToFile new.txt -Content "first line"                   # create new file

## EXAMPLES

### Example 1: Basic usage
```powershell
Add-LinesToFile file.txt -Content "new line"
Add-LinesToFile file.txt -LineNumber 1 -Content "first"
Add-LinesToFile file.txt -Content @("line1", "line2")
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

### -Content
Lines to insert. String or array of strings.

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

### -LineNumber
Insert position (1-based). Omit to append at end.

```yaml
Type: Int32
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
- Omitting `-LineNumber` appends to end
- `-LineNumber 1` inserts at beginning (existing lines shift down)
- Wildcards cannot create new files


## RELATED LINKS
