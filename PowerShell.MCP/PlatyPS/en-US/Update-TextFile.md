---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Update-TextFile

## SYNOPSIS
Update text file content using string literal or regex replacement

## SYNTAX

### Path
```
Update-TextFile [-Path] <String[]> [-Contains <String>] [-Pattern <String>] [-Replacement <String>]
 [-LineRange <Int32[]>] [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Update-TextFile -LiteralPath <String[]> [-Contains <String>] [-Pattern <String>] [-Replacement <String>]
 [-LineRange <Int32[]>] [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf]
 [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Replaces matching text (literal or regex) within optional line range. Supports automatic metadata preservation (encoding, newlines) and optional backups. Optimized for LLM text editing workflows.

## EXAMPLES

### Example 1: Basic text replacement - literal string and regex pattern
```powershell
# Literal string replacement (simple, no escaping needed)
PS C:\> Update-TextFile config.js -Contains "localhost" -Replacement "production.example.com"
Updated config.js: 1 replacement(s) made

# Regular expression with capture groups (powerful pattern matching)
PS C:\> Update-TextFile config.js -Pattern "const (\w+) = (\d+)" -Replacement "let `$1 = `$2"
Updated config.js: 2 replacement(s) made
```

Use -Contains/-Replacement for simple literal string replacement. Use -Pattern/-Replacement for advanced regex-based transformations with capture groups.

### Example 2: Targeted replacement - line range and multiple files
```powershell
# Replace only within specific line range (lines 10-20 inclusive)
PS C:\> Update-TextFile data.txt -LineRange 10,20 -Contains "old" -Replacement "new"
Updated data.txt: 5 replacement(s) made

# Process multiple files with wildcards
PS C:\> Update-TextFile *.js -Contains "var " -Replacement "let "
Updated app.js: 3 replacement(s) made
Updated config.js: 1 replacement(s) made
```

Combine -LineRange to limit changes to specific sections. Use wildcards to update multiple files at once.

### Example 3: Safe editing with backup and confirmation
```powershell
# Create timestamped backup before modifying
PS C:\> Update-TextFile important.conf -Contains "debug=true" -Replacement "debug=false" -Backup
Updated important.conf: 1 replacement(s) made

PS C:\> Get-ChildItem important.conf*
Name
----
important.conf
important.conf.20251004135500.bak

# Prompt for confirmation before making changes
PS C:\> Update-TextFile critical.ini -Pattern "timeout=\d+" -Replacement "timeout=300" -Confirm
Confirm
Are you sure you want to perform this action?
[Y] Yes  [A] Yes to All  [N] No  [L] No to All  [S] Suspend  [?] Help (default is "Y"):
```

Use -Backup for critical files not under version control. Use -Confirm for interactive validation before modifications.

### Example 4: Preview changes with -WhatIf
```powershell
PS C:\> Update-TextFile config.txt -Contains "setting" -Replacement "option" -WhatIf
What if: Performing the operation "Update text file" on target "config.txt".
```

Use -WhatIf to preview which files would be modified without actually changing them. Useful for testing patterns before execution.

## PARAMETERS

### -Backup
Creates a timestamped backup file (filename.yyyyMMddHHmmss.bak) before modifying the file. Recommended for important files not under version control.

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

### -Path
Specifies the path to the text file(s) to modify. Supports wildcards for processing multiple files.

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

### -Contains
Specifies a literal string to match in lines. Only lines containing this substring will be processed for replacement. Unlike -Pattern (which uses regex), -Contains performs simple substring matching without interpreting special characters. This is useful when filtering lines that contain regex metacharacters like '[', ']', '(', ')', '.', '*', '+', '?', ' without needing to escape them. When used with -Replacement but without -Pattern, the entire line containing the string is replaced.

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

BEST PRACTICE - Verify before and after:

1. Check current content: Show-TextFile config.js -Pattern "localhost"

2. Make the replacement: Update-TextFile config.js -OldValue "localhost" -NewValue "production"

3. Verify the change: Show-TextFile config.js -Pattern "production"

CRITICAL - Handling special characters (dollar, braces, quotes):

When replacing code with special characters, ALWAYS use here-strings.
Example: $old = @'...'@ and $new = @'...'@ then Update-TextFile file.cs -OldValue $old -NewValue $new

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

## RELATED LINKS
