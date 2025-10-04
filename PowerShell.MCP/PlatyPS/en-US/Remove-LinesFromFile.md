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
 [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Remove-LinesFromFile -LiteralPath <String[]> [-LineRange <Int32[]>] [-Contains <String>] [-Pattern <String>]
 [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Removes lines matching text (literal or regex) within optional range. When range and pattern are both specified, only matching lines within the range are removed. Preserves file metadata (encoding, newlines).

## EXAMPLES

### Example 1: Remove by line number or pattern
```powershell
Remove-LinesFromFile app.log -LineRange 5                              # Single line
Remove-LinesFromFile app.log -LineRange 10,20                          # Range
Remove-LinesFromFile app.log -Contains "DEBUG"                         # Literal match
Remove-LinesFromFile app.log -Pattern "^(DEBUG|TRACE):"                # Regex
```

### Example 2: Combined filters and multiple files
```powershell
Remove-LinesFromFile app.log -LineRange 100,200 -Contains "temp"       # AND condition
Remove-LinesFromFile *.log -Contains "DEPRECATED" -Backup              # Wildcards
```

### Example 3: Pipeline
```powershell
Get-ChildItem *.log | Remove-LinesFromFile -Contains "DEBUG"
Get-ChildItem *.txt | Where-Object Length -gt 1MB | Remove-LinesFromFile -LineRange 1,100 -Backup
```

Important:
- -LineRange and -Contains/-Pattern can be combined (AND logic)
- -Contains (literal) and -Pattern (regex) are mutually exclusive
- Warns if no lines match (file unchanged)
- Pipeline: accepts FileInfo via PSPath property

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
Parameter Sets: Path
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
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

### -Contains
Specifies a literal string to match in lines. Lines containing this substring will be removed. Unlike -Pattern (which uses regex), -Contains performs simple substring matching without interpreting special characters. This is useful when searching for text that contains regex metacharacters like '[', ']', '(', ')', '.', '*', '+', '?', ' without needing to escape them.

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

### -LiteralPath
Specifies the path to the text file without wildcard expansion. Use this parameter when the file path contains characters that would otherwise be interpreted as wildcards (like '[', ']', '*', '?'). Unlike -Path, this parameter treats the input literally.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.String[]

## OUTPUTS

### System.Object

## NOTES

At least one of -LineRange, -Contains, or -Pattern must be specified. -Contains and -Pattern are mutually exclusive.

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
