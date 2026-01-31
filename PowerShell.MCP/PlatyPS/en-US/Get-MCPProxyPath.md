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
Get-MCPProxyPath [-Escape]
```

## EXAMPLES

### Example 1: Basic usage
```powershell
Get-MCPProxyPath                    # C:\...\PowerShell.MCP.Proxy.exe
Get-MCPProxyPath -Escape            # C:\\...\\PowerShell.MCP.Proxy.exe (for JSON)
```

## NOTES
- `-Escape` doubles backslashes for JSON config files