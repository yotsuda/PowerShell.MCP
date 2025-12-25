# Build-AllPlatforms.ps1
# Builds PowerShell.MCP module and PowerShell.MCP.Proxy for all supported platforms

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Dll', 'WinX64', 'LinuxX64', 'OsxX64', 'OsxArm64')]
    [string[]]$Target,
    [string]$Configuration = 'Release',
    [string]$OutputBase
)

$ErrorActionPreference = 'Stop'

# If no target specified, build all
$allTargets = @('Dll', 'WinX64', 'LinuxX64', 'OsxX64', 'OsxArm64')
if (-not $Target) {
    $Target = $allTargets
}

# Map target names to RIDs
$ridMap = @{
    'WinX64'   = 'win-x64'
    'LinuxX64' = 'linux-x64'
    'OsxX64'   = 'osx-x64'
    'OsxArm64' = 'osx-arm64'
}

# Determine output base from installed module if not specified
if (-not $OutputBase) {
    $module = Get-Module PowerShell.MCP -ListAvailable | Select-Object -First 1
    if (-not $module) {
        Write-Error "PowerShell.MCP module not found. Please install the module first or specify -OutputBase parameter."
        exit 1
    }
    $OutputBase = $module.ModuleBase
    Write-Host "Detected module path: $OutputBase" -ForegroundColor Gray
}

$moduleProjectPath = Join-Path $PSScriptRoot 'PowerShell.MCP'
$proxyProjectPath = Join-Path $PSScriptRoot 'PowerShell.MCP.Proxy'
$stagingPath = Join-Path $PSScriptRoot 'Staging'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "PowerShell.MCP Build Script" -ForegroundColor Cyan
Write-Host "Output: $OutputBase" -ForegroundColor Cyan
Write-Host "Target: $($Target -join ', ')" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# =============================================================================
# Build PowerShell.MCP.dll (if Dll target specified)
# =============================================================================
if ('Dll' -in $Target) {
    Write-Host "[Dll] Building PowerShell.MCP.dll..." -ForegroundColor Yellow

    $buildArgs = @(
        'build'
        $moduleProjectPath
        '-c', $Configuration
        '--no-incremental'
        '--source', 'https://api.nuget.org/v3/index.json'
    )

    & dotnet @buildArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Error "PowerShell.MCP.dll build failed!"
        exit 1
    }

    Write-Host "  Build completed" -ForegroundColor Green

    # Copy required files to OutputBase
    Write-Host "  Copying module files..." -ForegroundColor Gray

    # Source paths
    $buildOutputPath = Join-Path $moduleProjectPath "bin\$Configuration\net9.0"

    # Copy DLLs from build output
    Copy-Item (Join-Path $buildOutputPath 'PowerShell.MCP.dll') -Destination $OutputBase -Force
    Copy-Item (Join-Path $buildOutputPath 'Ude.NetStandard.dll') -Destination $OutputBase -Force
    Write-Host "  Copied: PowerShell.MCP.dll" -ForegroundColor Green
    Write-Host "  Copied: Ude.NetStandard.dll" -ForegroundColor Green

    # Copy manifest and script from Staging
    Copy-Item (Join-Path $stagingPath 'PowerShell.MCP.psd1') -Destination $OutputBase -Force
    Copy-Item (Join-Path $stagingPath 'PowerShell.MCP.psm1') -Destination $OutputBase -Force
    Write-Host "  Copied: PowerShell.MCP.psd1" -ForegroundColor Green
    Write-Host "  Copied: PowerShell.MCP.psm1" -ForegroundColor Green


    Write-Host ""
}

# =============================================================================
# Build PowerShell.MCP.Proxy (for specified platforms)
# =============================================================================
$proxyTargets = $Target | Where-Object { $_ -ne 'Dll' }

if ($proxyTargets) {
    Write-Host "[Proxy] Building PowerShell.MCP.Proxy..." -ForegroundColor Yellow

    $binBase = Join-Path $OutputBase 'bin'

    foreach ($t in $proxyTargets) {
        $rid = $ridMap[$t]
        Write-Host "  [$rid] Publishing..." -ForegroundColor Gray
        
        $outputDir = Join-Path $binBase $rid
        
        if (-not (Test-Path $outputDir)) {
            New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
        }
        
        $publishArgs = @(
            'publish'
            $proxyProjectPath
            '-c', $Configuration
            '-r', $rid
            '-o', $outputDir
            '--source', 'https://api.nuget.org/v3/index.json'
        )
        
        & dotnet @publishArgs
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "[$rid] Build failed!"
            exit 1
        }
        
        $exeName = if ($rid -like 'win-*') { 'PowerShell.MCP.Proxy.exe' } else { 'PowerShell.MCP.Proxy' }
        $exePath = Join-Path $outputDir $exeName
        
        if (Test-Path $exePath) {
            $size = [math]::Round((Get-Item $exePath).Length / 1MB, 2)
            Write-Host "  [$rid] Success ($size MB)" -ForegroundColor Green
        } else {
            Write-Warning "  [$rid] Executable not found at expected path"
        }
    }

    Write-Host ""
}

# =============================================================================
# Summary: Verify built files
# =============================================================================
Write-Host "[Summary] Verifying output..." -ForegroundColor Yellow

$expectedFiles = @()

if ('Dll' -in $Target) {
    $expectedFiles += @(
        'PowerShell.MCP.dll',
        'PowerShell.MCP.psd1',
        'PowerShell.MCP.psm1',
        'Ude.NetStandard.dll'
    )
}

foreach ($t in ($Target | Where-Object { $_ -ne 'Dll' })) {
    $rid = $ridMap[$t]
    $exeName = if ($rid -like 'win-*') { 'PowerShell.MCP.Proxy.exe' } else { 'PowerShell.MCP.Proxy' }
    $expectedFiles += "bin\$rid\$exeName"
}

$allPresent = $true
foreach ($file in $expectedFiles) {
    $path = Join-Path $OutputBase $file
    if (-not (Test-Path $path)) {
        Write-Warning "  Missing: $file"
        $allPresent = $false
    }
}

if ($allPresent -and $expectedFiles.Count -gt 0) {
    Write-Host "  All built files present" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan