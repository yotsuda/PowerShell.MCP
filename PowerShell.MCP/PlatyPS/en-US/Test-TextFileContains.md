---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Test-TextFileContains

## SYNOPSIS
Tests if file contains matching text (literal or regex)

## SYNTAX

### Path
```
Test-TextFileContains [-Path] <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>]
 [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

### LiteralPath
```
Test-TextFileContains -LiteralPath <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>]
 [-Encoding <String>] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Tests if file contains matching text (literal or regex) within specified line range. Returns Boolean value (True/False) for use in conditional statements. Optimized for LLM use with early termination on first match.

## EXAMPLES

### Example 1: Basic search - literal string or regex pattern
```powershell
# Check if file contains literal string (simple, no escaping needed)
PS C:\> Test-TextFileContains app.log -Contains "ERROR"
True

# Check if file matches regex pattern (powerful matching)
PS C:\> Test-TextFileContains app.log -Pattern "^(ERROR|CRITICAL):"
True

# Returns False when not found
PS C:\> Test-TextFileContains app.log -Contains "SUCCESS"
False
```

Use -Contains for simple literal string searches. Use -Pattern for advanced regex matching. Returns True/False for easy use in conditional logic.

### Example 2: Use in conditional statements and scripts
```powershell
# Simple if statement
PS C:\> if (Test-TextFileContains config.ini -Contains "Debug=true") {
    Write-Host "Debug mode is enabled" -ForegroundColor Yellow
}

# Complex conditional logic
PS C:\> if (Test-TextFileContains app.log -Pattern "FATAL|CRITICAL") {
    Send-MailMessage -To "admin@example.com" -Subject "Critical Error Detected"
}

# Validation in deployment scripts
PS C:\> if (-not (Test-TextFileContains web.config -Contains "ProductionMode=true")) {
    throw "Configuration file is not set to production mode"
}
```

Common usage pattern for conditional execution based on file content. Perfect for validation, monitoring, and deployment scripts.

### Example 3: Search within line range and multiple files
```powershell
# Search only within specific section (lines 1000-2000)
PS C:\> Test-TextFileContains large.txt -Contains "TARGET" -LineRange 1000,2000
False

# Check multiple files with wildcards (returns True if ANY file matches)
PS C:\> Test-TextFileContains *.log -Contains "FATAL"
True

# Combine wildcards and line range
PS C:\> Test-TextFileContains *.config -LineRange 1,10 -Contains "version=2.0"
True
```

Use -LineRange to search specific sections of large files. Use wildcards to check multiple files (OR logic - returns True if any file matches).

### Example 4: Advanced usage with encodings
```powershell
# Specify encoding for non-UTF8 files
PS C:\> Test-TextFileContains sjis.txt -Contains "日本語" -Encoding Shift_JIS
True

# Auto-detection works for most files (when -Encoding omitted)
PS C:\> Test-TextFileContains utf8.txt -Contains "テキスト"
True
```

Specify -Encoding for files with non-standard encodings. Auto-detection works for most common encodings when omitted.

## PARAMETERS

### -Contains
Specifies a literal string to search for in each line. Unlike -Pattern (which uses regex), -Contains performs simple substring matching without interpreting special characters. Matching lines are identified immediately and return True. This is useful for searching text that contains regex metacharacters like '[', ']', '(', ')', '.', '*', '+', '?', '$' without needing to escape them.

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
Specifies the character encoding of the file. If omitted, encoding is auto-detected using BOM and heuristics. Common values: utf-8, shift_jis, utf-16, ascii.

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
Specifies the line range to search. Accepts 1 or 2 values: single line number (e.g., 5) or start-end range (e.g., 5,10). Both endpoints are inclusive. Line numbers are 1-based. Only lines within this range are searched.

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
Specifies the path to the text file(s). Supports wildcards (* and ?) for processing multiple files. When multiple files match, returns True if any file contains the search term (OR logic).

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
Specifies a regular expression pattern to search for. Lines matching the pattern return True immediately. Can be combined with -LineRange to search within a specific range.

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

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.String[]

## OUTPUTS

### System.Boolean

## NOTES

Typical LLM workflow:

1. Check if file contains error: Test-TextFileContains app.log -Contains "ERROR"

2. Conditional action: if (Test-TextFileContains file.txt -Contains "TODO") { ... }

3. Search specific range: Test-TextFileContains large.txt -Pattern "BUG" -LineRange 100,200

Performance:

- Early termination: Returns True immediately on first match (does not read entire file)
- Efficient for large files: Uses streaming I/O with minimal memory footprint
- Boolean output: Optimized for conditional statements in scripts

Contains vs Pattern:

- Use -Contains for simple literal string searches (no regex knowledge needed)
- Use -Pattern for advanced regex pattern matching
- -Contains does not require escaping special characters: [, ], (, ), ., *, +, ?, $
- -Contains and -Pattern are mutually exclusive (cannot be used together)

Multiple files:

- Returns True if ANY file matches (OR operation)
- Supports wildcards: *.log, test*.txt, etc.
- Early termination applies across files (stops at first match)

## RELATED LINKS
