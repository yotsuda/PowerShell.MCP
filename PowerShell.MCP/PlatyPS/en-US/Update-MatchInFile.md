---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Update-MatchInFile

## SYNOPSIS
Replace text in a file using literal string or regex pattern

## SYNTAX

```
Update-MatchInFile [-Path] <String[]> [-OldText <String>] [-Pattern <String>] [-Replacement <String>] [-LineRange <Int32[]>] [-Encoding <String>] [-Backup] [-WhatIf]
```

## EXAMPLES

### Example 1: Basic usage
```powershell
Update-MatchInFile file.txt -OldText "foo" -Replacement "bar"          # literal replacement
Update-MatchInFile file.txt -Pattern "v\d+" -Replacement "v2"          # regex replacement
Update-MatchInFile file.cs -Pattern "(\w+)\.Log" -Replacement '$1.Debug'  # with capture group
Update-MatchInFile file.txt -LineRange 10,20 -OldText "old" -Replacement "new"  # within range
```

## NOTES
- `-OldText` (literal) and `-Pattern` (regex) are mutually exclusive
- `-Pattern` supports capture groups (`$1`, `$2`) in `-Replacement`
- Use here-strings (@'...'@) for code with special characters ($, {}, quotes)
- `-WhatIf` shows detailed preview with highlighting
- Newlines in `-Replacement` are normalized to match file's newline style