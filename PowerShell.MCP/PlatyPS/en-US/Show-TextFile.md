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
Show-TextFile [-Path] <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>]
 [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### LiteralPath
```
Show-TextFile -LiteralPath <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>]
 [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Displays file contents with line numbers. Filter by line range and/or matching text (literal or regex). Optimized for LLM use with 1-based line numbering compatible with editors and compilers.

## EXAMPLES

### Example 1: Display files - entire file or specific lines
```powershell
# Display entire file with line numbers (1-based)
PS C:\> Show-TextFile Program.cs
==> Program.cs <==
   1: using System;
   2: using System.Collections.Generic;
   3:
   4: namespace Example

# Display a single line (e.g., line 17 from compiler error message)
PS C:\> Show-TextFile Program.cs -LineRange 17
==> Program.cs <==
  17:             return a + b; // BUG: Should be a - b

# Display a range of lines (e.g., context around error on line 15)
PS C:\> Show-TextFile Program.cs -LineRange 14,17
==> Program.cs <==
  14:         // ERROR: This method is broken
  15:         public int Subtract(int a, int b)
  16:         {
  17:             return a + b; // BUG: Should be a - b
```

Line numbers are 1-based to match editor line numbers and compiler error messages. Use -LineRange to view specific sections.

### Example 2: Search for patterns - literal string or regex
```powershell
# Search with regex pattern (powerful matching)
PS C:\> Show-TextFile Program.cs -Pattern "TODO|ERROR"
==> Program.cs <==
*  6:     // TODO: Implement this class
* 14:         // ERROR: This method is broken

# Search with literal string (simple, no escaping needed)
PS C:\> Show-TextFile log.txt -Contains "[ERROR]"
==> log.txt <==
*  15: [ERROR] Failed to connect to database
*  42: [ERROR] File not found: config.ini

# Pattern vs Contains comparison
PS C:\> Show-TextFile data.txt -Pattern "price.*\$100"        # Regex - requires escaping $
PS C:\> Show-TextFile data.txt -Contains "price: $100.50"     # Literal - no escaping needed
```

Matching lines are prefixed with * for easy identification. Use -Contains for simple literal searches, -Pattern for advanced regex matching.

### Example 3: Combine filters - line range + pattern
```powershell
# Search for pattern only within specific lines (lines 7-12)
PS C:\> Show-TextFile Program.cs -Pattern "public" -LineRange 7,12
==> Program.cs <==
*  7:     public class Calculator
*  9:         public int Add(int a, int b)

# View specific section and highlight errors
PS C:\> Show-TextFile app.log -LineRange 100,200 -Contains "ERROR"
==> app.log <==
* 145: [ERROR] Connection timeout
* 187: [ERROR] Invalid response
```

Combine -LineRange with -Pattern or -Contains to search within specific sections. Only matching lines within the range are shown.

### Example 4: Process multiple files
```powershell
# Display multiple files with wildcards
PS C:\> Show-TextFile *.cs
==> Calculator.cs <==
   1: public class Calculator
   
==> Program.cs <==
   1: using System;

# Search across multiple files
PS C:\> Show-TextFile *.log -Contains "FATAL"
==> app.log <==
*  34: FATAL: Database connection failed
   
==> system.log <==
*  12: FATAL: Out of memory
```

Files are separated by blank lines with headers showing filenames. Useful for quickly surveying related files.

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
Aliases: FullName

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
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
Specifies a literal string to search for in each line. Unlike -Pattern (which uses regex), -Contains performs simple substring matching without interpreting special characters. This is useful for searching text that contains regex metacharacters like '[', ']', '(', ')', '.', '*', '+', '?', ' without needing to escape them. Matching lines are prefixed with * for easy identification.

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

3. Make the fix: Update-TextFile Program.cs -LineRange 48 -OldValue "Bug" -NewValue "Fix"

4. Verify the fix: Show-TextFile Program.cs -LineRange 45,55

Line numbering:

- Line numbers are 1-based (first line is 1, not 0)
- Matches editor line numbers and compiler error messages

Pattern matching:

- Matching lines are prefixed with * for easy identification
- Combine -Pattern with -LineRange to search within a specific section
- Use Show-TextFile to test regex patterns before using them in Update-TextFile

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
