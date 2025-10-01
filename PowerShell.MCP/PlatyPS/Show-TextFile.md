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

### Default (Default)
```
Show-TextFile [-Path] <String[]> [-LineRange <Int32[]>] [-Encoding <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### LineRange
```
Show-TextFile [-Path] <String[]> [-LineRange <Int32[]>] [-Encoding <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### Pattern
```
Show-TextFile [-Path] <String[]> [-LineRange <Int32[]>] -Pattern <String> [-Encoding <String>]
 [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Shows text file contents with 1-based line numbers. Supports line range display, pattern matching, and multiple encodings. Optimized for LLM use with editor/compiler-compatible line numbering.

## EXAMPLES

### Example 1: Display entire file with line numbers
```powershell
PS C:\> Show-TextFile Program.cs
==> Program.cs <==
   1: using System;
   2: using System.Collections.Generic;
   3:
   4: namespace Example
```

Displays the entire file with 1-based line numbers (4-digit format).

### Example 2: Display a single line
```powershell
PS C:\> Show-TextFile Program.cs -LineRange 17
==> Program.cs <==
  17:             return a + b; // BUG: Should be a - b
```

Shows only line 17. Useful for checking a specific line mentioned in compiler errors.

### Example 3: Display a range of lines
```powershell
PS C:\> Show-TextFile Program.cs -LineRange 14,17
==> Program.cs <==
  14:         // ERROR: This method is broken
  15:         public int Subtract(int a, int b)
  16:         {
  17:             return a + b; // BUG: Should be a - b
```

Shows lines 14 through 17 (inclusive). Both endpoints are included in the output.

### Example 4: Search for pattern
```powershell
PS C:\> Show-TextFile Program.cs -Pattern "TODO|ERROR"
==> Program.cs <==
*  6:     // TODO: Implement this class
* 14:         // ERROR: This method is broken
```

Searches for lines matching the regex pattern. Matching lines are prefixed with * for easy identification.

### Example 5: Search within a line range
```powershell
PS C:\> Show-TextFile Program.cs -Pattern "public" -LineRange 7,12
==> Program.cs <==
*  7:     public class Calculator
*  9:         public int Add(int a, int b)
```

Combines pattern search with line range. Only searches within lines 7-12.

### Example 6: Process multiple files with wildcards
```powershell
PS C:\> Show-TextFile *.cs
==> Calculator.cs <==
   1: public class Calculator
   
==> Program.cs <==
   1: using System;
```

Displays multiple files. Files are separated by blank lines with headers showing filenames.

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
Parameter Sets: (All)
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
Parameter Sets: Pattern
Aliases:

Required: True
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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.String[]

## OUTPUTS

### System.Object
## NOTES

**Typical LLM workflow:**
```powershell
# 1. Find the problem area
Show-TextFile Program.cs -Pattern "ERROR"

# 2. Examine context around the match
Show-TextFile Program.cs -LineRange 45,55

# 3. Make the fix
Update-TextFile Program.cs -LineRange 48 -OldValue "Bug" -NewValue "Fix"

# 4. Verify the fix
Show-TextFile Program.cs -LineRange 45,55
```

**Line numbering:**
- Line numbers are 1-based (first line is 1, not 0)
- Matches editor line numbers and compiler error messages
- Line numbers are always displayed in 4-digit format for alignment

**Pattern matching:**
- Matching lines are prefixed with * for easy identification
- Combine -Pattern with -LineRange to search within a specific section
- Use Show-TextFile to test regex patterns before using them in Update-TextFile

**Multiple files:**
- Wildcards are supported (*.cs, test*.txt, etc.)
- Files are separated by blank lines with headers
- Useful for quickly surveying related files
## RELATED LINKS
