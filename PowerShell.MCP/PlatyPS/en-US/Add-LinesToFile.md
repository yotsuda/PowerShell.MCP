---
external help file: PowerShell.MCP.dll-Help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Add-LinesToFile

## SYNOPSIS
Insert lines into a text file at a specific position or at the end

## SYNTAX

```
Add-LinesToFile [-Path] <String[]> [[-Content] <Object[]>] [-LineNumber <Int32>] [-Encoding <String>] [-Backup] [-WhatIf]
```

## EXAMPLES

### Example 1: Basic usage
```powershell
Add-LinesToFile file.txt -Content "new line"                    # append to end
Add-LinesToFile file.txt -LineNumber 5 -Content "inserted"      # insert at line 5 (existing lines shift down)
Add-LinesToFile file.txt -Content @("line1", "line2")           # add multiple lines
Add-LinesToFile new.txt -Content "first line"                   # create new file
```

## NOTES
- Omitting `-LineNumber` appends to end
- `-LineNumber 1` inserts at beginning
- For new files, `-LineNumber` only accepts 1
- Wildcards cannot create new files