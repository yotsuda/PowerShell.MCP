---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Remove-LinesFromFile

## SYNOPSIS
Remove lines from a text file by line range or pattern matching

## SYNTAX

```
Remove-LinesFromFile [-Path] <String[]> [-LineRange <Int32[]>] [-Contains <String>] [-Pattern <String>] [-Encoding <String>] [-Backup] [-WhatIf]
```

## EXAMPLES

### Example 1: Basic usage
```powershell
Remove-LinesFromFile file.txt -LineRange 5,10                   # remove lines 5-10
Remove-LinesFromFile file.txt -LineRange -10                    # remove last 10 lines
Remove-LinesFromFile file.txt -Pattern "^#"                     # remove all comment lines
Remove-LinesFromFile file.txt -Contains "DEBUG"                 # remove lines containing "DEBUG"
Remove-LinesFromFile file.txt -LineRange 1,100 -Pattern "TODO"  # remove TODO lines within range (AND)
```

## NOTES
- At least one of `-LineRange`, `-Contains`, or `-Pattern` required
- `-Contains` and `-Pattern` are mutually exclusive
- `-LineRange` + `-Pattern`/`-Contains` = AND condition
- Use `-WhatIf` to preview deletions