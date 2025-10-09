---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Update-MatchInFile

## SYNOPSIS
Update text file content using string literal or regex replacement

## SYNTAX

### Path
```
Update-MatchInFile [-Path] <String[]> [-Contains <String>] [-Pattern <String>] [-Replacement <String>]
 [-LineRange <Int32[]>] [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Update-MatchInFile -LiteralPath <String[]> [-Contains <String>] [-Pattern <String>] [-Replacement <String>]
 [-LineRange <Int32[]>] [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Replaces matching text (literal or regex) within optional line range. Preserves file metadata (encoding, newlines). Use -Contains for literal string replacement or -Pattern for regex-based replacement with capture groups support.

## EXAMPLES

### Example 1: Replace text - literal or regex
```powershell
Update-MatchInFile config.txt -Contains "debug=false" -Replacement "debug=true"     # Literal
Update-MatchInFile code.cs -Pattern "var (\w+)" -Replacement "string $1"            # Regex with capture
```

### Example 2: Targeted replacement
```powershell
Update-MatchInFile app.cs -LineRange 10,50 -Contains "oldFunc" -Replacement "newFunc"
Update-MatchInFile *.config -Contains "staging" -Replacement "production" -Backup
```

### Example 3: Pipeline and safety
```powershell
Get-ChildItem *.txt | Update-MatchInFile -Contains "old" -Replacement "new"
Get-ChildItem *.cs | Update-MatchInFile -Pattern "TODO" -Replacement "DONE" -Backup -WhatIf
```

Important:
- -Contains (literal) and -Pattern (regex) are mutually exclusive
- -Backup creates timestamped .bak files
- -WhatIf previews without changing
- Pipeline: accepts FileInfo via PSPath property

## PARAMETERS

### -Backup
Creates a backup file before modifying. Recommended for important files.

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

### -Contains
Specifies a literal string to match in lines. Only lines containing this substring will be processed for replacement. Unlike -Pattern (which uses regex), -Contains performs simple substring matching without interpreting special characters. This is useful when filtering lines that contain regex metacharacters like '[', ']', '(', ')', '.', '*', '+', '?', ' ' without needing to escape them. When used with -Replacement but without -Pattern, the entire line containing the string is replaced.

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
Specifies the character encoding. If omitted, encoding is auto-detected and preserved. Supports: utf8, utf8bom, sjis, eucjp, jis, ascii, utf16, utf32, etc.

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
Limits replacement to specific lines. Accepts 1 or 2 values: single line (e.g., 5) or range (e.g., 5,10). Only matches within this range are replaced.

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
Specifies the path to the text file(s) to modify. Supports wildcards for processing multiple files.

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
Regular expression pattern to search for. Use with -Replacement parameter. Supports capture groups ($1, $2, etc.).

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

### -Replacement
The replacement string for regex replacement. Used with -Pattern parameter. Supports backreferences ($1, $2, etc.) to captured groups. Note: In PowerShell strings, escape the dollar sign with a backtick (` $1`) to prevent variable expansion.

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

BEST PRACTICE - Verify before and after:

1. Check current content: Show-TextFile config.js -Pattern "localhost"

2. Make the replacement: Update-MatchInFile config.js -Contains "localhost" -Replacement "production"

3. Verify the change: Show-TextFile config.js -Pattern "production"

CRITICAL - Handling special characters (dollar, braces, quotes):

When replacing code with special characters, ALWAYS use here-strings.
Example: $old = @'...'@ and $new = @'...'@ then Update-MatchInFile file.cs -Contains $old -Replacement $new

Here-strings (@'...'@) treat ALL characters literally.

When to use -LineRange:

- Limit to specific section (use Show-TextFile line numbers)
- Avoid unintended replacements elsewhere
- Essential when text appears multiple times

When to use -Backup:

- Files not under version control
- Critical configuration changes

Regular expression mode:

- Use for complex transformations
- Test first: Show-TextFile -Pattern "regex"
- Escape special regex chars: . * + ? [ ] ( ) { } ^ $ |
- -Contains and -Pattern are mutually exclusive (cannot be used together)

## RELATED LINKS
