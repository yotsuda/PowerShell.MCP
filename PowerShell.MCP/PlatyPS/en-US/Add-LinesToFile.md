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

### LineNumber
```
Add-LinesToFile [-Path] <String[]> [-Content] <Object[]> -LineNumber <Int32> [-Encoding <String>] [-Backup]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

### AtEnd
```
Add-LinesToFile [-Path] <String[]> [-Content] <Object[]> [-AtEnd] [-Encoding <String>] [-Backup]
 [-ProgressAction <ActionPreference>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION
Inserts one or more lines into a text file at a specified line number or at the end. The inserted content becomes the specified line number, shifting existing lines down. Handles empty files gracefully. Preserves file metadata (encoding, newlines).


## EXAMPLES

### Example 1: Insert lines at specific positions
```powershell
PS C:\> Add-LinesToFile Program.cs -LineNumber 1 -Content "using System.Linq;"
Added 1 line(s) to Program.cs at line 1

PS C:\> Add-LinesToFile script.ps1 -LineNumber 10 -Content "# TODO: Implement this feature"
Added 1 line(s) to script.ps1 at line 10
```

Inserts content at line 1 (beginning) or line 10 (middle). The original content shifts down.

### Example 2: Append to the end of the file
```powershell
PS C:\> Add-LinesToFile log.txt -AtEnd -Content "Process completed at $(Get-Date)"
Added 1 line(s) to log.txt at end
```

Appends a timestamped message to the end of the file.

### Example 3: Insert multiple lines
```powershell
PS C:\> $headers = @("using System;", "using System.Collections.Generic;", "using System.Linq;")
PS C:\> Add-LinesToFile Program.cs -LineNumber 1 -Content $headers
Added 3 line(s) to Program.cs at line 1
```

Inserts multiple using statements as a block at the beginning.

### Example 4: Insert into an empty file
```powershell
PS C:\> Add-LinesToFile empty.txt -LineNumber 1 -Content "First line"
Added 1 line(s) to empty.txt at line 1
```

Works correctly with empty files. The content becomes the first line.

### Example 5: With backup
```powershell
PS C:\> Add-LinesToFile config.txt -LineNumber 5 -Content "NewSetting=Value" -Backup
Added 1 line(s) to config.txt at line 5
PS C:\> Get-ChildItem config.txt*

Name
----
config.txt
config.txt.20251001120000.bak
```

Creates a backup before modifying, useful for important configuration files.

## PARAMETERS

### -AtEnd
Appends the content at the end of the file. Cannot be used with -LineNumber.

```yaml
Type: SwitchParameter
Parameter Sets: AtEnd
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
Parameter Sets: LineNumber
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Path
Specifies the path to the text file. Supports wildcards for processing multiple files.

```yaml
Type: String[]
Parameter Sets: (All)
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
