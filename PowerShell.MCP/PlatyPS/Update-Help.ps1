<#
.SYNOPSIS
    Update PlatyPS markdown help files for PowerShell.MCP module

.DESCRIPTION
    Updates markdown documentation from the currently loaded PowerShell.MCP module.
    Assumes the module is already imported.

.EXAMPLE
    .\Update-Help.ps1
    Updates all markdown help files

.NOTES
    Requires: PlatyPS module (Install-Module -Name PlatyPS)
#>

$ErrorActionPreference = 'Stop'

$markdownPath = Join-Path $PSScriptRoot "en-US"

Write-Host "=== Updating PowerShell.MCP Help ===" -ForegroundColor Cyan

# Import PlatyPS
Import-Module PlatyPS -ErrorAction Stop

# Update markdown help
Write-Host "Updating markdown files in: $markdownPath" -ForegroundColor Yellow
Update-MarkdownHelpModule -Path $markdownPath -RefreshModulePage -ErrorAction Stop

# Show updated files
Write-Host "`n[OK] Updated files:" -ForegroundColor Green
Get-ChildItem $markdownPath\*.md | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Gray
}

Write-Host "`n=== Update Complete ===" -ForegroundColor Green
