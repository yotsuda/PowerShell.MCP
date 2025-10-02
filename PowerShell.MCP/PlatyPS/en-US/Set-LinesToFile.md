---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Set-LinesToFile

## SYNOPSIS
Replace or delete specific lines in a text file

## SYNTAX

```
Set-LinesToFile [-Path] <String[]> [[-Content] <Object[]>] [-LineRange <Int32[]>] [-Encoding <String>]
 [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Replaces specified lines with new content or deletes them if content is omitted. Can replace a single line, a range of lines, or the entire file. When replacing a range with fewer lines, the extra lines are removed. Preserves file metadata (encoding, newlines).

## EXAMPLES

### Example 1: Replace a single line
```powershell
PS C:\> Set-LinesToFile config.txt -LineRange 5 -Content "Port=9000"
Updated config.txt: Replaced 1 line(s)
```

Replaces line 5 with the new content.

### Example 2: Replace multiple lines with one line
```powershell
PS C:\> Set-LinesToFile config.txt -LineRange 10,12 -Content "Server=https://localhost:8080"
Updated config.txt: Replaced 3 line(s) with 1 line(s) (net: -2)
```

Replaces lines 10-12 with a single line, effectively condensing 3 lines into 1.

### Example 3: Delete lines by omitting content
```powershell
PS C:\> Set-LinesToFile data.txt -LineRange 3
Updated data.txt: Removed 1 line(s)

PS C:\> Set-LinesToFile data.txt -LineRange 5,7
Updated data.txt: Removed 3 line(s)
```

When -Content is omitted, the specified line(s) are deleted. Works with single lines or ranges.

### Example 4: Replace entire file content
```powershell
PS C:\> Set-LinesToFile config.txt -Content @("Line 1", "Line 2", "Line 3")
Updated config.txt: Replaced 5 line(s) with 3 line(s) (net: -2)
```

When -LineRange is omitted, replaces the entire file content. The original file had 5 lines, which were replaced with 3 new lines.

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

### -Content
The new content to set. Can be a string, array of strings, or omitted to delete lines. If omitted, the specified line range is deleted.

```yaml
Type: Object[]
Parameter Sets: (All)
Aliases:

Required: False
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

### -LineRange
Specifies the line range to replace. Accepts 1 or 2 values: single line (e.g., 5) or range (e.g., 5,10). If omitted, replaces the entire file.

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

BEST PRACTICE - Always verify before replacing:

1. First, check what you're about to replace: Show-TextFile config.txt -LineRange 30,40

2. Then perform the replacement: Set-LinesToFile config.txt -LineRange 30,40 -Content $newContent

3. Verify the result: Show-TextFile config.txt -LineRange 30,40

This three-step workflow (Show -> Set -> Show) prevents accidental data loss by confirming line numbers before modification.

Line number tips:

- Use Show-TextFile -Pattern to find the line numbers you need
- Line numbers are 1-based, matching editor displays
- Both endpoints in a range are inclusive (5,10 includes lines 5 through 10)
- When Content is omitted, the specified lines are deleted

Metadata preservation:

- File encoding is auto-detected and preserved
- Newline style (CRLF/LF/CR) is preserved
- Trailing newline presence is preserved

## RELATED LINKS
