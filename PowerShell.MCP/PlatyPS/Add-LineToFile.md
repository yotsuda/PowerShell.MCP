---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Add-LineToFile

## SYNOPSIS
Insert lines into a text file at a specific position or at the end

## SYNTAX

### LineNumber
```
Add-LineToFile [-Path] <String[]> [-Content] <Object[]> -LineNumber <Int32> [-Encoding <String>] [-Backup]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### AtEnd
```
Add-LineToFile [-Path] <String[]> [-Content] <Object[]> [-AtEnd] [-Encoding <String>] [-Backup]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Inserts one or more lines into a text file at a specified line number or at the end. The inserted content becomes the specified line number, shifting existing lines down. Handles empty files gracefully. Preserves file metadata (encoding, newlines).


## PARAMETERS

### -AtEnd
Appends the content at the end of the file. Cannot be used with -LineNumber.

```yaml
Type: SwitchParameter
Parameter Sets: AtEnd
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Backup
Creates a timestamped backup file before modifying. Recommended for important files.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Confirm
Prompts you for confirmation before running the cmdlet.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Content
The line(s) to insert. Can be a single string or an array of strings for multiple lines.

```yaml
Type: Object[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Encoding
Specifies the character encoding. If omitted, encoding is auto-detected and preserved.

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

### -LineNumber
The line number where content will be inserted (1-based). The inserted content becomes this line number, shifting existing lines down. Cannot be used with -AtEnd.

```yaml
Type: Int32
Parameter Sets: LineNumber
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Specifies the path to the text file. Supports wildcards for processing multiple files.

```yaml
Type: String[]
Parameter Sets: (All)
Aliases: FullName

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: True
```

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

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

### System.String[]

## OUTPUTS

### System.Object
## NOTES

**BEST PRACTICE - Verify insertion point:**
```powershell
# 1. Check the area where you'll insert
Show-TextFile Program.cs -LineRange 1,5

# 2. Insert the line
Add-LineToFile Program.cs -LineNumber 3 -Content "using System.Linq;"

# 3. Verify the insertion
Show-TextFile Program.cs -LineRange 1,6
```

**Understanding line numbers:**
- The content becomes the specified line number
- Original lines at and after that position shift down
- Line number 1 means "insert at the beginning"
- -AtEnd always appends at the end, regardless of file size

**Multiple line insertion:**
- Pass an array to insert multiple lines at once
- All lines are inserted as a contiguous block
- They are inserted in the order provided

**Empty file handling:**
- Works correctly with empty files
- -LineNumber 1 is the only valid choice for empty files

## RELATED LINKS
