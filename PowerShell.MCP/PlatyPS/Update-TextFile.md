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

### Literal
````
Update-TextFile [-Path] <String[]> -OldValue <String> -NewValue <String> [-LineRange <Int32[]>]
 [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
````

### Regex
````
Update-TextFile [-Path] <String[]> -Pattern <String> -Replacement <String> [-LineRange <Int32[]>]
 [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
````

## DESCRIPTION
Replaces text in files using either literal string matching (-OldValue/-NewValue) or regular expression patterns (-Pattern/-Replacement). Supports line range limiting, automatic metadata preservation (encoding, newlines), and optional backups. Optimized for LLM text editing workflows.

## EXAMPLES

### Example 1: Simple string replacement
````powershell
PS C:$> Update-TextFile config.js -OldValue "localhost" -NewValue "production.example.com"
Updated config.js: 1 replacement(s) made
````

Replaces all occurrences of "localhost" with "production.example.com" using literal string matching.

### Example 2: Regular expression replacement with capture groups
````powershell
PS C:$> Update-TextFile config.js -Pattern "const ($w+) = ($d+)" -Replacement "let `$1 = `$2"
Updated config.js: 2 replacement(s) made
````

Uses regex to change "const" declarations to "let" while preserving variable names and values.

### Example 3: Replace within a specific line range
````powershell
PS C:$> Update-TextFile data.txt -LineRange 10,20 -OldValue "old" -NewValue "new"
Updated data.txt: 3 replacement(s) made
````

Only replaces matches within lines 10-20 (inclusive). Lines outside this range are unchanged.

### Example 4: Create backup before modifying
````powershell
PS C:$> Update-TextFile important.conf -OldValue "debug=true" -NewValue "debug=false" -Backup
Updated important.conf: 1 replacement(s) made
PS C:$> Get-ChildItem important.conf*

Name
----
important.conf
important.conf.20251001120000.bak
````

Creates a timestamped backup file before making changes. Useful for critical files not under version control.

### Example 5: No matches found
````powershell
PS C:$> Update-TextFile config.js -OldValue "NotExist" -NewValue "Something"
WARNING: No matches found. File not modified.
````

If the search string is not found, a warning is displayed and the file remains unchanged.

### Example 6: Preview changes with -WhatIf
````powershell
PS C:$> Update-TextFile config.js -OldValue "staging" -NewValue "production" -WhatIf
What if: Performing the operation "Update text in file" on target "C:$Temp$config.js".
````

Shows what would happen without actually modifying the file. Useful for verifying changes before applying them.

## PARAMETERS

### -Backup
Creates a timestamped backup file (filename.yyyyMMddHHmmss.bak) before modifying the file. Recommended for important files not under version control.

````yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -Confirm
Prompts you for confirmation before running the cmdlet.

````yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: cf

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -Encoding
Specifies the character encoding. If omitted, encoding is auto-detected and preserved. Supports: utf8, utf8bom, sjis, eucjp, jis, ascii, utf16, utf32, etc.

````yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -LineRange
Limits replacement to specific lines. Accepts 1 or 2 values: single line (e.g., 5) or range (e.g., 5,10). Only matches within this range are replaced.

````yaml
Type: Int32[]
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -NewValue
The replacement string for literal string replacement. Used with -OldValue parameter. Special characters like $ are treated literally.

````yaml
Type: String
Parameter Sets: Literal
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -OldValue
The string to search for (literal matching). All occurrences are replaced. Use -Pattern for regex matching instead.

````yaml
Type: String
Parameter Sets: Literal
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -Path
Specifies the path to the text file(s) to modify. Supports wildcards for processing multiple files.

````yaml
Type: String[]
Parameter Sets: (All)
Aliases: FullName

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: True
````

### -Pattern
Regular expression pattern to search for. Use with -Replacement parameter. Supports capture groups (, , etc.).

````yaml
Type: String
Parameter Sets: Regex
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -Replacement
The replacement string for regex replacement. Used with -Pattern parameter. Supports backreferences (, , etc.) to captured groups.

````yaml
Type: String
Parameter Sets: Regex
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -WhatIf
Shows what would happen if the cmdlet runs.
The cmdlet is not run.

````yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases: wi

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### -ProgressAction
{{ Fill ProgressAction Description }}

````yaml
Type: ActionPreference
Parameter Sets: (All)
Aliases: proga

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
````

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### System.String[]

## OUTPUTS

### System.Object
## NOTES

**BEST PRACTICE - Verify before and after:**
```powershell
# 1. Check current content
Show-TextFile config.js -Pattern "localhost"

# 2. Make the replacement
Update-TextFile config.js -OldValue "localhost" -NewValue "production"

# 3. Verify the change
Show-TextFile config.js -Pattern "production"
```

**CRITICAL - Handling special characters (dollar, braces, quotes):**
When replacing code with special characters, ALWAYS use here-strings:

```powershell
# WRONG - Complex escaping on command line
Update-TextFile file.cs -OldValue "if (test) {}" -NewValue "..."  # Difficult!

# CORRECT - Use here-strings (no escaping)
$old = @'
if (ShouldProcess(path, $"Update"))
'@
$new = @'
if (ShouldProcess(path, $"Update {details}"))
'@
Update-TextFile file.cs -OldValue $old -NewValue $new
```

Here-strings (@'...'@) treat ALL characters literally.

**When to use -LineRange:**
- Limit to specific section (use Show-TextFile line numbers)
- Avoid unintended replacements elsewhere
- Essential when text appears multiple times

**When to use -Backup:**
- Files not under version control
- Critical configuration changes

**Regular expression mode:**
- Use for complex transformations
- Test first: Show-TextFile -Pattern "regex"
- Escape special regex chars: . * + ? [ ] ( ) { } ^ $ |
## RELATED LINKS
