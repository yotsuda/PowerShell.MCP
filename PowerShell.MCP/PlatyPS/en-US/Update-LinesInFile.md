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
Update-LinesInFile [-Path] <String[]> [-LineRange <Int32[]>] [[-Content] <Object[]>] [-Encoding <String>]
 [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Update-LinesInFile -LiteralPath <String[]> [-LineRange <Int32[]>] [[-Content] <Object[]>] [-Encoding <String>]
 [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Replaces specified line range with new content or creates new file. Omit content to delete lines. Can replace single line, range, or entire file. Preserves file metadata (encoding, newlines).

## EXAMPLES

### Example 1: Create or replace entire file
```powershell
Update-LinesInFile output.txt -Content @("Line 1", "Line 2", "Line 3")    # Create/replace all
Update-LinesInFile config.ini -Content "Setting=Value"                    # Single line
```

### Example 2: Replace specific lines
```powershell
Update-LinesInFile app.log -LineRange 5 -Content "New line 5"             # Single line
Update-LinesInFile app.log -LineRange 10,15 -Content "Replacement"        # Range -> single
Update-LinesInFile app.log -LineRange 20,22 -Content @("A", "B", "C")     # Range -> multiple
```

### Example 3: Delete lines or use pipeline
```powershell
Update-LinesInFile app.log -LineRange 5,10                                # Delete (no -Content)
Get-ChildItem *.txt | Update-LinesInFile -LineRange 1 -Content "# Updated: $(Get-Date)"
Get-ChildItem *.log | Where-Object Length -gt 1MB | Update-LinesInFile -LineRange 1,100 -Backup
```

Important:
- Without -LineRange: creates/replaces entire file
- With -LineRange but no -Content: deletes lines
- Content accepts string or string array
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

2. Then perform the replacement: Update-LinesInFile config.txt -LineRange 30,40 -Content $newContent

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
