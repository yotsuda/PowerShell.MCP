---
external help file: PowerShell.MCP-help.xml
Module Name: PowerShell.MCP
online version:
schema: 2.0.0
---

# Get-MCPProxyPath

## SYNOPSIS
Gets the path to the PowerShell.MCP.Proxy executable for the current platform.

## SYNTAX

```
Get-MCPProxyPath [-Escape] [-ProgressAction <ActionPreference>] [<CommonParameters>]
```

## DESCRIPTION
Returns the full path to the platform-specific PowerShell.MCP.Proxy executable.

    Get-MCPProxyPath                    # C:\...\PowerShell.MCP.Proxy.exe

    Get-MCPProxyPath -Escape            # C:\\...\\PowerShell.MCP.Proxy.exe (for JSON)

## EXAMPLES

### Example 1: Get proxy path
```powershell
Get-MCPProxyPath
Get-MCPProxyPath -Escape
```

## PARAMETERS

### -Escape
Escapes backslashes for JSON config files.

```yaml
Type: SwitchParameter
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: False
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProgressAction
{{ Fill ProgressAction Description }}

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

## OUTPUTS

## NOTES
- Use `-Escape` for JSON config files (doubles backslashes)


## RELATED LINKS
