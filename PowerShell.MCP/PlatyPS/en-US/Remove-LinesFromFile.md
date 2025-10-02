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

```
Remove-LinesFromFile [-Path] <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Encoding <String>]
 [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Removes one or more lines from a text file either by specifying a line range, matching a regular expression pattern, or both. When both LineRange and Pattern are specified, only lines within the range that match the pattern are removed. Preserves file metadata (encoding, newlines).

## EXAMPLES

### Example 1: Remove specific lines or ranges
```powershell
PS C:\> Remove-LinesFromFile data.txt -LineRange 5
Removed 1 line(s) from data.txt

PS C:\> Remove-LinesFromFile data.txt -LineRange 10,15
Removed 6 line(s) from data.txt
```

Removes line 5, or lines 10-15 (inclusive). Use -LineRange for precise line-based deletion.

### Example 2: Remove all DEBUG lines
```powershell
PS C:\> Remove-LinesFromFile app.log -Pattern "^DEBUG:"
Removed 23 line(s) from app.log
```

Removes all lines starting with "DEBUG:". Useful for cleaning log files.

### Example 3: Remove empty lines
```powershell
PS C:\> Remove-LinesFromFile data.txt -Pattern "^\s*$"
Removed 7 line(s) from data.txt
```

Removes all empty or whitespace-only lines.

### Example 4: Remove TODO comments
```powershell
PS C:\> Remove-LinesFromFile Program.cs -Pattern "//\s*TODO"
Removed 3 line(s) from Program.cs
```

Removes all lines containing TODO comments.

### Example 5: Combine LineRange and Pattern (AND condition)
```powershell
PS C:\> Remove-LinesFromFile app.log -LineRange 100,200 -Pattern "^DEBUG:"
Removed 12 line(s) from app.log
```

Removes only DEBUG lines within lines 100-200. Both conditions must be met (AND).

### Example 6: No matches found
```powershell
PS C:\> Remove-LinesFromFile data.txt -Pattern "NOTEXIST"
WARNING: No lines matched. File not modified.
```

If no lines match the pattern, a warning is displayed and the file remains unchanged.

## PARAMETERS

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

### -LineRange
Specifies the line range to remove. Accepts 1 or 2 values: single line (e.g., 5) or range (e.g., 10,20). Both endpoints are inclusive. Can be combined with -Pattern for AND condition.

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

### -Pattern
Regular expression pattern to match lines for removal. All matching lines are removed. Can be combined with -LineRange for AND condition.

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
Common parameter for controlling progress display behavior. See about_CommonParameters for details.

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

At least one of -LineRange or -Pattern must be specified.

BEST PRACTICE - Verify before deletion:

1. Preview what will be deleted: Show-TextFile app.log -Pattern "^DEBUG:"
2. Remove matching lines: Remove-LinesFromFile app.log -Pattern "^DEBUG:"
3. Verify the result: Show-TextFile app.log -LineRange 1,20

Pattern matching tips:

- Test your pattern with Show-TextFile -Pattern first
- Use anchors (^ for start, $ for end) for precise matching
- Remember that ALL matching lines are removed

Combining parameters:

- LineRange only: Removes specific lines by number
- Pattern only: Removes all lines matching the pattern
- LineRange + Pattern: Removes only matching lines within the specified range (AND condition)

Safety considerations:

- No undo after removal (unless you used -Backup or have version control)
- If no lines match, the file remains unchanged with a warning
- Use -LineRange for precise deletion of known line numbers
- Use -Pattern for content-based deletion
- Use both for targeted deletion within a specific section

Common patterns:

- Empty lines: ^\s*$
- Comments: ^\s*// or ^\s*#
- Debug statements: console\.log|System\.out\.println

## RELATED LINKS
