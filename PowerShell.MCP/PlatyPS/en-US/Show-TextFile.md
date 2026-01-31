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

```
Show-TextFile [-Path] <String[]> [-LineRange <Int32[]>] [-Pattern <String>] [-Contains <String>] [-Recurse] [-Encoding <String>]
Show-TextFile -LiteralPath <String[]> ...
```

## EXAMPLES

### Example 1: Basic usage
```powershell
Show-TextFile file.txt                          # entire file
Show-TextFile file.txt -LineRange 10,20         # lines 10-20
Show-TextFile file.txt -LineRange -10           # last 10 lines
Show-TextFile file.txt -Pattern "error"         # regex search with 2-line context
Show-TextFile file.txt -Contains "[Error]"      # literal search (no escaping needed)
Show-TextFile . -Recurse -Pattern "TODO"        # recursive directory search
```

## NOTES
- `-LiteralPath` for paths with `[`, `]`, `*`, `?` (all file cmdlets support this)
- `-LineRange -10` = last 10 lines (single negative value = tail)
- `-Contains` and `-Pattern` can be combined (OR condition)
- `-Recurse` requires `-Pattern` or `-Contains`