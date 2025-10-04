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

### Path
```
Add-LinesToFile [-Path] <String[]> [-Content] <Object[]> [-LineNumber <Int32>] [-AtEnd] [-Encoding <String>]
 [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### LiteralPath
```
Add-LinesToFile -LiteralPath <String[]> [-Content] <Object[]> [-LineNumber <Int32>] [-AtEnd]
 [-Encoding <String>] [-Backup] [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Inserts lines at specified position or appends to end. Accepts arrays for multiple lines. Inserted content shifts existing lines down. Preserves file metadata (encoding, newlines).

## EXAMPLES

### Example 1: Insert at specific positions - beginning, middle, or end
```powershell
# Insert at beginning (line 1)
PS C:\> Add-LinesToFile Program.cs -LineNumber 1 -Content "using System.Linq;"
Added 1 line(s) to Program.cs at line 1

# Insert in the middle (line 10)
PS C:\> Add-LinesToFile script.ps1 -LineNumber 10 -Content "# TODO: Implement this feature"
Added 1 line(s) to script.ps1 at line 10

# Append to the end (simpler than counting lines)
PS C:\> Add-LinesToFile log.txt -AtEnd -Content "Process completed at $(Get-Date)"
Added 1 line(s) to log.txt at end
```

Use -LineNumber for precise insertion at any line. Use -AtEnd to append without knowing the file length. Original content shifts down when inserting.

### Example 2: Insert multiple lines at once
```powershell
# Insert block of lines (e.g., multiple using statements)
PS C:\> $headers = "using System;", "using System.Collections.Generic;", "using System.Linq;"
PS C:\> Add-LinesToFile Program.cs -LineNumber 1 -Content $headers
Added 3 line(s) to Program.cs at line 1

# Append multiple log entries
PS C:\> $logs = "Event 1: User login", "Event 2: Data processed", "Event 3: Task completed"
PS C:\> Add-LinesToFile app.log -AtEnd -Content $logs
Added 3 line(s) to app.log at end
```

Pass an array of strings to -Content to insert multiple lines as a block. All lines are inserted at the specified position.

### Example 3: Edge cases - empty files and multiple files
```powershell
# Insert into an empty file (creates first line)
PS C:\> Add-LinesToFile empty.txt -LineNumber 1 -Content "First line"
Added 1 line(s) to empty.txt at line 1

# Process multiple files with wildcards
PS C:\> Add-LinesToFile *.cs -LineNumber 1 -Content "// Auto-generated comment"
Added 1 line(s) to App.cs at line 1
Added 1 line(s) to Program.cs at line 1
```

Works correctly with empty files. Use wildcards to insert into multiple files at once.

### Example 4: Safe editing with backup
```powershell
PS C:\> Add-LinesToFile config.txt -LineNumber 5 -Content "NewSetting=Value" -Backup
Added 1 line(s) to config.txt at line 5

PS C:\> Get-ChildItem config.txt*
Name
----
config.txt
config.txt.20251004140500.bak
```

Use -Backup to create timestamped backups before modifying important files.

## PARAMETERS

### -AtEnd
Appends the content at the end of the file. Cannot be used with -LineNumber.

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

### -Backup
Creates a timestamped backup file before modifying. Recommended for important files.

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

### -Content
The line(s) to insert. Can be a single string or an array of strings for multiple lines.

```yaml
Type: Object[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Encoding
Specifies the character encoding. If omitted, encoding is auto-detected and preserved.

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

### -LineNumber
The line number where content will be inserted (1-based). The inserted content becomes this line number, shifting existing lines down. Cannot be used with -AtEnd.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Specifies the path to the text file. Supports wildcards for processing multiple files.

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

BEST PRACTICE - Verify insertion point:

1. Check the area where you'll insert:
   Show-TextFile Program.cs -LineRange 1,5

2. Insert the line:
   Add-LinesToFile Program.cs -LineNumber 3 -Content "using System.Linq;"

3. Verify the insertion:
   Show-TextFile Program.cs -LineRange 1,6

Understanding line numbers:

- The content becomes the specified line number
- Original lines at and after that position shift down
- Line number 1 means "insert at the beginning"
- -AtEnd always appends at the end, regardless of file size

Multiple line insertion:

- Pass an array to insert multiple lines at once
- All lines are inserted as a contiguous block
- They are inserted in the order provided

Empty file handling:

- Works correctly with empty files
- -LineNumber 1 is the only valid choice for empty files

## RELATED LINKS
