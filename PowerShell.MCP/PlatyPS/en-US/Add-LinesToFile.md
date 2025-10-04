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
Inserts lines at specified position or appends to end or creates new file. Accepts arrays for multiple lines. Inserted content shifts existing lines down. Preserves file metadata (encoding, newlines).

## EXAMPLES

### Example 1: Insert at specific positions
```powershell
Add-LinesToFile app.log -LineNumber 1 -Content "# Header"              # At beginning
Add-LinesToFile app.log -LineNumber 10 -Content "// Inserted line"     # Middle
Add-LinesToFile app.log -AtEnd -Content "# Footer"                     # At end
Add-LinesToFile new.txt -Content "First line" -LineNumber 1            # Creates file
```

### Example 2: Insert multiple lines
```powershell
Add-LinesToFile config.ini -LineNumber 5 -Content @("Line1", "Line2", "Line3")
Add-LinesToFile *.txt -AtEnd -Content "=== EOF ===" -Backup
```

### Example 3: Pipeline
```powershell
Get-ChildItem *.config | Add-LinesToFile -LineNumber 1 -Content "# Auto-generated"
Get-ChildItem *.cs | Where-Object { (Get-Content $_ -First 1) -notmatch "using" } | Add-LinesToFile -LineNumber 1 -Content "using System;"
```

Important:
- -LineNumber and -AtEnd are mutually exclusive
- Content accepts string or string array
- Creates new file if it doesn't exist
- Pipeline: accepts FileInfo via PSPath property

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
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName)
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

New file creation:

- Creates new files if they don't exist
- -LineNumber must be 1 for new files (or use -AtEnd)
- Wildcards cannot create new files (path must be literal)
- -Backup parameter is ignored when creating new files

## RELATED LINKS
