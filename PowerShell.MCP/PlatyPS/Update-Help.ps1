<#
.SYNOPSIS
    Update PlatyPS v2 markdown help files for PowerShell.MCP module

.DESCRIPTION
    Updates markdown documentation from the currently loaded PowerShell.MCP module
    using Microsoft.PowerShell.PlatyPS (v2).

.EXAMPLE
    .\Update-Help.ps1
    Updates all markdown help files

.NOTES
    Requires: Microsoft.PowerShell.PlatyPS module
#>

$ErrorActionPreference = 'Stop'

$markdownPath = Join-Path $PSScriptRoot "en-US"

Write-Host "=== Updating PowerShell.MCP Help (PlatyPS v2) ===" -ForegroundColor Cyan

# Import PlatyPS v2
Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop

# Update markdown help
Write-Host "Updating markdown files in: $markdownPath" -ForegroundColor Yellow

$mdFiles = Get-ChildItem "$markdownPath\*.md" -Exclude "PowerShell.MCP.md"
foreach ($md in $mdFiles) {
    $cmdHelp = Import-MarkdownCommandHelp -Path $md.FullName
    $cmdName = $cmdHelp.Title
    $cmdInfo = Get-Command $cmdName -Module PowerShell.MCP -ErrorAction SilentlyContinue
    if ($cmdInfo) {
        Update-CommandHelp -Path $md.FullName
        Write-Host "  Updated: $($md.Name)" -ForegroundColor Gray
    } else {
        Write-Warning "  Skipped: $($md.Name) (command not found)"
    }
}

# Update module page
$modulePage = Join-Path $markdownPath "PowerShell.MCP.md"
if (Test-Path $modulePage) {
    $commandHelps = $mdFiles | ForEach-Object { Import-MarkdownCommandHelp -Path $_.FullName }
    Update-MarkdownModuleFile -Path $modulePage -CommandHelp $commandHelps -Force
    Write-Host "  Updated: PowerShell.MCP.md" -ForegroundColor Gray
}

Write-Host "`n=== Update Complete ===" -ForegroundColor Green
