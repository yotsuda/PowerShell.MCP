# Build-AllPlatforms.ps1
# Builds PowerShell.MCP module and PowerShell.MCP.Proxy for all supported platforms

[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputBase
)

$ErrorActionPreference = 'Stop'

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
$helpSourcePath = Join-Path $PSScriptRoot 'PowerShell.MCP\PlatyPS\en-US'

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "PowerShell.MCP Build Script" -ForegroundColor Cyan
Write-Host "Output: $OutputBase" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# =============================================================================
# Step 1: Build PowerShell.MCP.dll
# =============================================================================
Write-Host "[1/4] Building PowerShell.MCP.dll..." -ForegroundColor Yellow

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
Write-Host ""

# =============================================================================
# Step 2: Copy required files to OutputBase
# =============================================================================
Write-Host "[2/4] Copying module files..." -ForegroundColor Yellow

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

# Copy help file
$helpDestPath = Join-Path $OutputBase 'en-US'
if (-not (Test-Path $helpDestPath)) {
    New-Item -Path $helpDestPath -ItemType Directory -Force | Out-Null
}
$helpFile = Join-Path $helpSourcePath 'PowerShell.MCP.dll-Help.xml'
if (Test-Path $helpFile) {
    Copy-Item $helpFile -Destination $helpDestPath -Force
    Write-Host "  Copied: en-US\PowerShell.MCP.dll-Help.xml" -ForegroundColor Green
} else {
    Write-Warning "  Help file not found: $helpFile"
}

Write-Host ""

# =============================================================================
# Step 3: Build PowerShell.MCP.Proxy for all platforms
# =============================================================================
Write-Host "[3/4] Building PowerShell.MCP.Proxy for all platforms..." -ForegroundColor Yellow

$rids = @(
    'win-x64',
    'linux-x64',
    'osx-x64',
    'osx-arm64'
)

$binBase = Join-Path $OutputBase 'bin'

foreach ($rid in $rids) {
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

# =============================================================================
# Step 4: Summary
# =============================================================================
Write-Host "[4/4] Verifying output..." -ForegroundColor Yellow

$expectedFiles = @(
    'PowerShell.MCP.dll',
    'PowerShell.MCP.psd1',
    'PowerShell.MCP.psm1',
    'Ude.NetStandard.dll',
    'en-US\PowerShell.MCP.dll-Help.xml',
    'bin\win-x64\PowerShell.MCP.Proxy.exe',
    'bin\linux-x64\PowerShell.MCP.Proxy',
    'bin\osx-x64\PowerShell.MCP.Proxy',
    'bin\osx-arm64\PowerShell.MCP.Proxy'
)

$allPresent = $true
foreach ($file in $expectedFiles) {
    $path = Join-Path $OutputBase $file
    if (-not (Test-Path $path)) {
        Write-Warning "  Missing: $file"
        $allPresent = $false
    }
}

if ($allPresent) {
    Write-Host "  All required files present" -ForegroundColor Green
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Verify with: Get-MCPProxyPath" -ForegroundColor Gray