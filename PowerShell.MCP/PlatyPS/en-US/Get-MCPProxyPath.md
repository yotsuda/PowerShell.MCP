---
external help file: PowerShell.MCP.dll-help.xml
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
Use this path in your MCP client configuration.

## EXAMPLES

### EXAMPLE 1
```
Get-MCPProxyPath
Returns: C:\Program Files\PowerShell\7\Modules\PowerShell.MCP\bin\win-x64\PowerShell.MCP.Proxy.exe
```

### EXAMPLE 2
```
Get-MCPProxyPath -Escape
Returns: C:\\Program Files\\PowerShell\\7\\Modules\\PowerShell.MCP\\bin\\win-x64\\PowerShell.MCP.Proxy.exe
```

## PARAMETERS

### -Escape
If specified, escapes backslashes for use in JSON configuration files.

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
Common parameter for controlling progress output. See about_CommonParameters for details.

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

### System.String
## NOTES

## RELATED LINKS
