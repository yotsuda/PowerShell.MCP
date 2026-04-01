<#
.SYNOPSIS
    Build PowerShell help files from PlatyPS v2 markdown files

.DESCRIPTION
    This script generates MAML help files from markdown documentation using
    Microsoft.PowerShell.PlatyPS (v2). It builds help for all supported .NET
    target frameworks and copies the output to both Debug and Release build
    directories.

.EXAMPLE
    .\Build-Help.ps1
    Builds help files for all target frameworks

.NOTES
    Requires: Microsoft.PowerShell.PlatyPS module
    Run with Administrator privileges to deploy to PowerShell modules directory
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Script paths
$scriptRoot = $PSScriptRoot
$markdownPath = Join-Path $scriptRoot "en-US"
$projectRoot = Split-Path $scriptRoot -Parent
$binPath = Join-Path $projectRoot "bin"

Write-Host "=== PowerShell.MCP Help Build Script (PlatyPS v2) ===" -ForegroundColor Cyan
Write-Host ""

# Check for Microsoft.PowerShell.PlatyPS module
$platyPS = Get-Module -Name Microsoft.PowerShell.PlatyPS -ListAvailable
if (-not $platyPS) {
    # Fallback to legacy platyPS
    $platyPS = Get-Module -Name platyPS -ListAvailable
    if ($platyPS) {
        Write-Error "Only legacy platyPS $($platyPS.Version) found. Install Microsoft.PowerShell.PlatyPS: Install-Module Microsoft.PowerShell.PlatyPS -Scope CurrentUser"
    } else {
        Write-Error "PlatyPS not found. Install with: Install-Module Microsoft.PowerShell.PlatyPS -Scope CurrentUser"
    }
    exit 1
}

Import-Module Microsoft.PowerShell.PlatyPS -ErrorAction Stop
Write-Host "[OK] Microsoft.PowerShell.PlatyPS $($platyPS.Version) loaded" -ForegroundColor Green

# Verify markdown files exist
if (-not (Test-Path $markdownPath)) {
    Write-Error "Markdown path not found: $markdownPath"
    exit 1
}

$mdFiles = Get-ChildItem -Path "$markdownPath\*.md" -Exclude "PowerShell.MCP.md"
if ($mdFiles.Count -eq 0) {
    Write-Error "No markdown files found in: $markdownPath"
    exit 1
}

Write-Host "[OK] Found $($mdFiles.Count) markdown files" -ForegroundColor Green
Write-Host ""

# Detect target frameworks
$targetFrameworks = @()
if (Test-Path $binPath) {
    $debugPath = Join-Path $binPath "Debug"
    $releasePath = Join-Path $binPath "Release"

    foreach ($buildConfig in @($debugPath, $releasePath)) {
        if (Test-Path $buildConfig) {
            $tfms = Get-ChildItem -Path $buildConfig -Directory | Where-Object { $_.Name -match '^net\d' }
            foreach ($tfm in $tfms) {
                if ($tfm.Name -notin $targetFrameworks) {
                    $targetFrameworks += $tfm.Name
                }
            }
        }
    }
}

if ($targetFrameworks.Count -eq 0) {
    Write-Warning "No target frameworks detected. Using default: net9.0, net8.0"
    $targetFrameworks = @("net9.0", "net8.0")
}

Write-Host "Target frameworks: $($targetFrameworks -join ', ')" -ForegroundColor Yellow
Write-Host ""

# Build help: Import markdown → Export MAML XML
$tempOutputPath = Join-Path $env:TEMP "PowerShell.MCP-Help-Build"
if (Test-Path $tempOutputPath) {
    Remove-Item -Path $tempOutputPath -Recurse -Force
}

Write-Host "Building help files..." -ForegroundColor Cyan
try {
    # PlatyPS v2: import markdown into CommandHelp objects, then export to MAML
    $helpObjects = $mdFiles | ForEach-Object {
        Import-MarkdownCommandHelp -Path $_.FullName
    }
    Write-Host "  Imported $($helpObjects.Count) command help objects" -ForegroundColor Gray

    $result = Export-MamlCommandHelp -CommandHelp $helpObjects -OutputFolder $tempOutputPath -Force
    foreach ($f in $result) {
        Write-Host "[OK] Help file generated: $($f.Name) ($([math]::Round($f.Length / 1KB, 2)) KB)" -ForegroundColor Green
    }
} catch {
    Write-Error "Failed to build help file: $_"
    exit 1
}

Write-Host ""

# Help files to copy
$helpFiles = Get-ChildItem -Path $tempOutputPath -Filter "*.xml" -Recurse
$copiedCount = 0

foreach ($tfm in $targetFrameworks) {
    foreach ($config in @("Debug", "Release")) {
        $targetPath = Join-Path $binPath "$config\$tfm\en-US"

        if (-not (Test-Path $targetPath)) {
            New-Item -Path $targetPath -ItemType Directory -Force | Out-Null
        }

        foreach ($hf in $helpFiles) {
            Copy-Item -Path $hf.FullName -Destination (Join-Path $targetPath $hf.Name) -Force
        }
        $copiedCount++

        Write-Host "  [COPY] $config/$tfm/en-US/" -ForegroundColor Gray
    }
}

# Copy to PowerShell modules directory
$psModulePath = "C:\Program Files\PowerShell\7\Modules\PowerShell.MCP\en-US"
try {
    if (-not (Test-Path $psModulePath)) {
        New-Item -Path $psModulePath -ItemType Directory -Force | Out-Null
    }

    foreach ($hf in $helpFiles) {
        Copy-Item -Path $hf.FullName -Destination (Join-Path $psModulePath $hf.Name) -Force
    }
    $copiedCount++

    Write-Host "  [COPY] PowerShell\7\Modules\PowerShell.MCP\en-US\" -ForegroundColor Gray
} catch {
    Write-Warning "Could not copy to PowerShell modules directory: $_"
    Write-Warning "You may need to run as Administrator"
}

Write-Host ""
$primaryTfm = $targetFrameworks[0]
Write-Host "=== Build Complete ===" -ForegroundColor Green
Write-Host "Help files copied to $copiedCount locations" -ForegroundColor Green
Write-Host ""
Write-Host "To test the help:" -ForegroundColor Yellow
Write-Host "  Get-Help Invoke-Claude -Full" -ForegroundColor Gray
Write-Host "  Get-Help Show-TextFiles -Full" -ForegroundColor Gray

# Cleanup temporary directory
Remove-Item -Path $tempOutputPath -Recurse -Force -ErrorAction SilentlyContinue
