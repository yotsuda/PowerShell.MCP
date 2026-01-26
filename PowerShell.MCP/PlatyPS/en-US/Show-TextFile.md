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
 [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### LiteralPath
```
Show-TextFile -LiteralPath <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>]
 [-Recurse] [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Displays file contents with line numbers. When using -Contains or -Pattern, matching lines are highlighted with ANSI reverse video and displayed with 3 lines of context before and after each match. Filter by line range and/or matching text (literal or regex). Optimized for LLM use with 1-based line numbering compatible with editors and compilers.

## EXAMPLES

### Example 1: Search with context display
```powershell
Show-TextFile log.txt -Pattern "ERROR|WARN"     # Shows matches with 3-line context
Show-TextFile log.txt -Contains "[ERROR]"       # Literal match (-Pattern and -Contains are mutually exclusive)
```

### Encoding
Specifies the character encoding of the file. If omitted, encoding is auto-detected (BOM + heuristics). Supports: utf8, utf8bom, sjis, eucjp, jis, ascii, utf16, utf32, etc.

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

### LineRange
Specifies the line range to display. Accepts 1 or 2 values: single line number (e.g., 5) or start-end range (e.g., 5,10). Both endpoints are inclusive. Line numbers are 1-based. Use 0 or negative values for the end position to indicate end of file (e.g., 100,-1 displays from line 100 to end of file).

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

### Path
Specifies the path to the text file(s). Supports wildcards (* and ?) for processing multiple files. When multiple files match, they are displayed with headers and separated by blank lines.

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

### Pattern
Specifies a regular expression pattern to search for. Matching lines are displayed with 3 lines of context before and after, with matched text highlighted using ANSI reverse video. Matching lines are prefixed with * for easy identification. Can be combined with -LineRange to search within a specific range.

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

### ProgressAction
Common parameter for controlling progress output. See about_CommonParameters for details.

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

### LiteralPath
Specifies the path to the text file(s) without wildcard expansion. Use this parameter when the file path contains characters that would otherwise be interpreted as wildcards (like '[', ']', '*', '?'). Unlike -Path, this parameter treats the input literally.

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

### Contains
Specifies a literal string to search for in each line. Unlike -Pattern (which uses regex), -Contains performs simple substring matching without interpreting special characters. Matching lines are displayed with 3 lines of context before and after, with matched text highlighted using ANSI reverse video. This is useful for searching text that contains regex metacharacters like '[', ']', '(', ')', '.', '*', '+', '?', ' ' without needing to escape them. Matching lines are prefixed with * for easy identification.

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
### Recurse
Recursively searches subdirectories for files matching the -Path pattern. Requires -Pattern or -Contains to be specified. Useful for searching across an entire project directory tree.

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


### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## PARAMETERS

### -Encoding
Specifies the character encoding of the file. If omitted, encoding is auto-detected (BOM + heuristics). Supports: utf8, utf8bom, sjis, eucjp, jis, ascii, utf16, utf32, etc.

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
Specifies the line range to display. Accepts 1 or 2 values: single line number (e.g., 5) or start-end range (e.g., 5,10). Both endpoints are inclusive. Line numbers are 1-based.

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
Specifies the path to the text file(s). Supports wildcards (* and ?) for processing multiple files. When multiple files match, they are displayed with headers and separated by blank lines.

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
Specifies a regular expression pattern to search for. Only lines matching the pattern are displayed. Matching lines are prefixed with * for easy identification. Can be combined with -LineRange to search within a specific range.

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

### -ProgressAction
Common parameter for controlling progress output. See about_CommonParameters for details.

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

### -LiteralPath
Specifies the path to the text file(s) without wildcard expansion. Use this parameter when the file path contains characters that would otherwise be interpreted as wildcards (like '[', ']', '*', '?'). Unlike -Path, this parameter treats the input literally.

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

### -Contains
Specifies a literal string to search for in each line. Unlike -Pattern (which uses regex), -Contains performs simple substring matching without interpreting special characters. This is useful for searching text that contains regex metacharacters like '[', ']', '(', ')', '.', ' ', '+', '?', ' ' without needing to escape them. Matching lines are prefixed with for easy identification.

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
Recursively searches subdirectories for files matching the -Path pattern. Requires -Pattern or -Contains to be specified. Useful for searching across an entire project directory tree.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.String[]

## OUTPUTS

### System.Object

## NOTES

Typical LLM workflow:

1. Find the problem area: Show-TextFile Program.cs -Pattern "ERROR"

2. Examine context: Show-TextFile Program.cs -LineRange 45,55

3. Make the fix: Update-MatchInFile Program.cs -LineRange 48 -OldValue "Bug" -NewValue "Fix"

4. Verify the fix: Show-TextFile Program.cs -LineRange 45,55

Line numbering:

- Line numbers are 1-based (first line is 1, not 0)
- Matches editor line numbers and compiler error messages

Pattern matching:

- Matching lines are prefixed with * for easy identification
- Combine -Pattern with -LineRange to search within a specific section
- Use Show-TextFile to test regex patterns before using them in Update-MatchInFile

Contains vs Pattern:

- Use -Contains for simple literal string searches (no regex knowledge needed)
- Use -Pattern for advanced regex pattern matching
- -Contains does not require escaping special characters: [, ], (, ), ., *, +, ?, $, etc.
- -Contains and -Pattern are mutually exclusive (cannot be used together)

Multiple files:

- Wildcards are supported (*.cs, test*.txt, etc.)
- Files are separated by blank lines with headers
- Useful for quickly surveying related files

## RELATED LINKS
