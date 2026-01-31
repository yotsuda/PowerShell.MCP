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

```
Update-LinesInFile [-Path] <String[]> [-LineRange <Int32[]>] [[-Content] <Object[]>] [-Encoding <String>] [-Backup] [-WhatIf]
```

## EXAMPLES

### Example 1: Basic usage
```powershell
Update-LinesInFile file.txt -LineRange 5 -Content "replaced"           # replace line 5
Update-LinesInFile file.txt -LineRange 5,10 -Content "single line"     # replace lines 5-10 with one line
Update-LinesInFile file.txt -LineRange 5,10 -Content @()               # delete lines 5-10
Update-LinesInFile file.txt -Content @("line1", "line2")               # replace entire file
```

## NOTES
- `-Content @()` deletes lines (empty array)
- Omitting `-LineRange` replaces entire file
- `-Content` is required when `-LineRange` is specified