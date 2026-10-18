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

To build the MAML help files from markdown:

```powershell
.\Build-Help.ps1
```

This will:
1. Generate `PowerShell.MCP-Help.xml` and `PowerShell.MCP.dll-Help.xml` from the markdown files
2. Copy the help files to all target framework directories:
   - `bin/Debug/net8.0/en-US/`
   - `bin/Debug/net9.0/en-US/`
   - `bin/Release/net8.0/en-US/`
   - `bin/Release/net9.0/en-US/`
3. Copy them into the installed module's `en-US\` folder — the first copy
   `Get-Module PowerShell.MCP -ListAvailable` finds whose folder is writable
   (the same resolution `Build-AllPlatforms.ps1` uses for its default
   `-OutputBase`). Administrator privileges are needed only when the sole
   copy is under Program Files.

## Requirements

- [Microsoft.PowerShell.PlatyPS](https://github.com/PowerShell/platyPS) (v2; the legacy `platyPS` module is not enough)

Install with:
```powershell
Install-Module -Name Microsoft.PowerShell.PlatyPS -Scope CurrentUser
```

## Editing Help

1. Edit the markdown files in `en-US/`
2. Run `.\Build-Help.ps1` to regenerate help
3. Test with `Get-Help <CommandName> -Full`

## Notes

- Help files are automatically built and copied to all target frameworks and to the installed module
- The markdown files follow PlatyPS format requirements
