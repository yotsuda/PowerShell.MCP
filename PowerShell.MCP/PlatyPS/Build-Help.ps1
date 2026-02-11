<#
.SYNOPSIS
    Build PowerShell help files from PlatyPS markdown files

.DESCRIPTION
    This script generates MAML help files from markdown documentation using PlatyPS.
    It builds help for all supported .NET target frameworks and copies the output
    to both Debug and Release build directories.

.EXAMPLE
    .\Build-Help.ps1
    Builds help files for all target frameworks

.NOTES
    Requires: platyPS module (Install-Module -Name platyPS)
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

Write-Host "=== PowerShell.MCP Help Build Script ===" -ForegroundColor Cyan
Write-Host ""

# Check for platyPS module
if (-not (Get-Module -Name platyPS -ListAvailable)) {
    Write-Error "platyPS module not found. Install it with: Install-Module -Name platyPS"
    exit 1
}

Import-Module platyPS -ErrorAction Stop
Write-Host "[OK] platyPS module loaded" -ForegroundColor Green

# Verify markdown files exist
if (-not (Test-Path $markdownPath)) {
    Write-Error "Markdown path not found: $markdownPath"
    exit 1
}

$mdFiles = Get-ChildItem -Path $markdownPath -Filter "*.md"
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

# Build help to temporary directory first
# NOTE: New-ExternalHelp emits a harmless .NET stderr "already exists" when generating
# multiple help files (DLL + script module) because it calls mkdir twice internally.
# This cannot be suppressed from PowerShell and does not affect the output.
$tempOutputPath = Join-Path $env:TEMP "PowerShell.MCP-Help-Build"
if (Test-Path $tempOutputPath) {
    Remove-Item -Path $tempOutputPath -Recurse -Force
}

Write-Host "Building help file..." -ForegroundColor Cyan
try {
    $result = New-ExternalHelp -Path $markdownPath -OutputPath $tempOutputPath -Force -ErrorAction Stop
    Write-Host "[OK] Help file generated: $($result.Name)" -ForegroundColor Green
    Write-Host "     Size: $([math]::Round($result.Length / 1KB, 2)) KB" -ForegroundColor Gray
} catch {
    Write-Error "Failed to build help file: $_"
    exit 1
}

Write-Host ""

# Help files to copy (DLL binary cmdlets + script module cmdlets)
$helpFiles = Get-ChildItem -Path $tempOutputPath -Filter "*.xml"
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
Write-Host "Help file copied to $copiedCount locations" -ForegroundColor Green
Write-Host ""
Write-Host "To test the help:" -ForegroundColor Yellow
Write-Host "  Import-Module .\bin\Debug\$primaryTfm\PowerShell.MCP.dll -Force" -ForegroundColor Gray
Write-Host "  Get-Help Show-TextFile -Full" -ForegroundColor Gray

# Cleanup temporary directory
Remove-Item -Path $tempOutputPath -Recurse -Force -ErrorAction SilentlyContinue
