# PlatyPS Help Files

This directory contains PowerShell help documentation for the PowerShell.MCP module.

## Structure

```
PlatyPS/
├── en-US/              # English help markdown files
│   ├── Show-TextFile.md
│   ├── Update-TextFile.md
│   ├── Set-LineToFile.md
│   ├── Add-LineToFile.md
│   └── Remove-LineFromFile.md
├── Build-Help.ps1      # Help build script
└── README.md           # This file
```

## Building Help

To build the MAML help files from markdown (requires Administrator privileges):

```powershell
# Run as Administrator
.\Build-Help.ps1
```

This will:
1. Generate `PowerShell.MCP.dll-Help.xml` from markdown files
2. Copy the help file to all target framework directories:
   - `bin/Debug/net8.0/en-US/`
   - `bin/Debug/net9.0/en-US/`
   - `bin/Release/net8.0/en-US/`
   - `bin/Release/net9.0/en-US/`
3. Copy to PowerShell modules directory:
   - `C:\Program Files\PowerShell\7\Modules\PowerShell.MCP\en-US\`
   - `bin/Release/net9.0/en-US/`

## Requirements

- [platyPS](https://github.com/PowerShell/platyPS) module

Install with:
```powershell
Install-Module -Name platyPS -Scope CurrentUser
```

## Editing Help

1. Edit the markdown files in `en-US/`
2. Run `.\Build-Help.ps1` to regenerate help
3. Test with `Get-Help <CommandName> -Full`

## Notes

- `Build-Help.ps1` is excluded from git (local-only build script)
- Help files are automatically built and copied to all target frameworks
- The markdown files follow PlatyPS format requirements
