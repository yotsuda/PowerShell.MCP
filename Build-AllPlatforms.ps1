# Build-AllPlatforms.ps1
# Builds PowerShell.MCP module and PowerShell.MCP.Proxy for all supported platforms

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('Dll', 'WinX64', 'LinuxX64', 'OsxX64', 'OsxArm64')]
    [string[]]$Target,
    [string]$Configuration = 'Release',
    [string]$OutputBase,
    [switch]$Sign,
    [string]$PfxPath = 'C:\MyProj\vault\yotsuda.pfx',
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

# Kill all Claude.exe processes to release file locks
Get-Process -Name 'Claude' -ErrorAction SilentlyContinue | Stop-Process -Force

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

    # Source paths (deploy net8.0 — forward compatible with .NET 9)
    $buildOutputPath = Join-Path $moduleProjectPath "bin\$Configuration\net8.0"

    # Copy DLLs from build output
    Copy-Item (Join-Path $buildOutputPath 'PowerShell.MCP.dll') -Destination $OutputBase -Force
    # Ude.NetStandard.dll: try build output first, fallback to NuGet cache
    $udeDll = Join-Path $env:USERPROFILE '.nuget\packages\ude.netstandard\1.2.0\lib\netstandard2.0\Ude.NetStandard.dll'
    $udeBuildPath = Join-Path $buildOutputPath 'Ude.NetStandard.dll'
    if (Test-Path $udeBuildPath) {
        Copy-Item $udeBuildPath -Destination $OutputBase -Force
    } elseif (Test-Path $udeDll) {
        Copy-Item $udeDll -Destination $OutputBase -Force
    } else {
        Write-Warning "  Ude.NetStandard.dll not found!"
    }
    Write-Host "  Copied: PowerShell.MCP.dll" -ForegroundColor Green
    Write-Host "  Copied: Ude.NetStandard.dll" -ForegroundColor Green

    # Copy manifest, script, and format files from Staging
    Copy-Item (Join-Path $stagingPath 'PowerShell.MCP.psd1') -Destination $OutputBase -Force
    Copy-Item (Join-Path $stagingPath 'PowerShell.MCP.psm1') -Destination $OutputBase -Force
    Write-Host "  Copied: PowerShell.MCP.psd1" -ForegroundColor Green
    Write-Host "  Copied: PowerShell.MCP.psm1" -ForegroundColor Green

    # Copy third-party license notices (LGPL-2.1 obligation for Ude.NetStandard)
    Copy-Item (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.md') -Destination $OutputBase -Force
    $licensesSrc = Join-Path $PSScriptRoot 'licenses'
    $licensesDst = Join-Path $OutputBase 'licenses'
    if (Test-Path $licensesDst) { Remove-Item $licensesDst -Recurse -Force }
    Copy-Item $licensesSrc -Destination $licensesDst -Recurse -Force
    Write-Host "  Copied: THIRD_PARTY_NOTICES.md" -ForegroundColor Green
    Write-Host "  Copied: licenses\" -ForegroundColor Green

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
            '--self-contained'
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
# Authenticode-sign Windows binaries (PowerShell.MCP.dll + Proxy.exe)
# =============================================================================
# WDAC / Device Guard environments block unsigned binaries. Signing with our
# self-signed cert lets IT add the cert as a trusted publisher once instead of
# whitelisting per-version SHA-256 hashes (which break on every update).
# See issue #46.

$signTargets = @()
if ('Dll' -in $Target) {
    $signTargets += Join-Path $OutputBase 'PowerShell.MCP.dll'
}
if ('WinX64' -in $Target) {
    $signTargets += Join-Path $OutputBase 'bin\win-x64\PowerShell.MCP.Proxy.exe'
}

if ($signTargets -and $Sign) {
    Write-Host "[Sign] Authenticode-signing Windows binaries..." -ForegroundColor Yellow

    if (-not (Test-Path $PfxPath)) {
        Write-Error "  PFX not found at $PfxPath. Cannot sign."
        exit 1
    }

    $pfxPassword = Read-Host "  Enter PFX password" -AsSecureString
    $cert = Get-PfxCertificate -FilePath $PfxPath -Password $pfxPassword

    foreach ($file in $signTargets) {
        if (-not (Test-Path $file)) {
            Write-Warning "  Missing: $file — skipping"
            continue
        }
        $result = Set-AuthenticodeSignature `
            -FilePath $file `
            -Certificate $cert `
            -HashAlgorithm SHA256 `
            -TimestampServer $TimestampUrl `
            -IncludeChain NotRoot
        if ($result.Status -eq 'Valid') {
            Write-Host "  Signed: $(Split-Path $file -Leaf)" -ForegroundColor Green
        } else {
            Write-Error "  Sign failed for $file : $($result.StatusMessage)"
            exit 1
        }
    }
    Write-Host ""
} elseif ($signTargets) {
    Write-Host "[Sign] Skipping signing (pass -Sign to enable, e.g. for publish builds)." -ForegroundColor Gray
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
        'Ude.NetStandard.dll',
        'THIRD_PARTY_NOTICES.md',
        'licenses\Ude.NetStandard\COPYING',
        'licenses\Ude.NetStandard\MPL-1.1.txt',
        'licenses\Ude.NetStandard\gpl-2.0.txt',
        'licenses\Ude.NetStandard\lgpl-2.1.txt',
        'en-US\PowerShell.MCP-help.xml'
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

# =============================================================================
# Check for unexpected files in output directory
# =============================================================================
Write-Host "[Cleanup] Checking for unexpected files..." -ForegroundColor Yellow

$allowedFiles = @(
    'PowerShell.MCP.dll',
    'PowerShell.MCP.psd1',
    'PowerShell.MCP.psm1',
    'Ude.NetStandard.dll',
    'THIRD_PARTY_NOTICES.md'
)

$allowedDirs = @(
    'bin',
    'en-US',
    'licenses'
)

$unexpectedItems = @()

# Check root level files
Get-ChildItem $OutputBase -File | ForEach-Object {
    if ($_.Name -notin $allowedFiles) {
        $unexpectedItems += $_.Name
    }
}

# Check root level directories
Get-ChildItem $OutputBase -Directory | ForEach-Object {
    if ($_.Name -notin $allowedDirs) {
        $unexpectedItems += "$($_.Name)\"
    }
}

# Check bin directory structure
$binPath = Join-Path $OutputBase 'bin'
if (Test-Path $binPath) {
    $allowedPlatforms = @('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')

    Get-ChildItem $binPath -Directory | ForEach-Object {
        if ($_.Name -notin $allowedPlatforms) {
            $unexpectedItems += "bin\$($_.Name)\"
        }
    }

    # Check each platform directory
    foreach ($platform in $allowedPlatforms) {
        $platformPath = Join-Path $binPath $platform
        if (Test-Path $platformPath) {
            $expectedExe = if ($platform -like 'win-*') { 'PowerShell.MCP.Proxy.exe' } else { 'PowerShell.MCP.Proxy' }
            Get-ChildItem $platformPath -File | ForEach-Object {
                if ($_.Name -ne $expectedExe) {
                    $unexpectedItems += "bin\$platform\$($_.Name)"
                }
            }
        }
    }
}

# Check en-US directory
$enUSPath = Join-Path $OutputBase 'en-US'
if (Test-Path $enUSPath) {
    $allowedHelpFiles = @('PowerShell.MCP-help.xml', 'PowerShell.MCP.dll-Help.xml')
    Get-ChildItem $enUSPath -File | ForEach-Object {
        if ($_.Name -notin $allowedHelpFiles) {
            $unexpectedItems += "en-US\$($_.Name)"
        }
    }
}

if ($unexpectedItems.Count -gt 0) {
    Write-Warning "  Unexpected files found in output directory:"
    foreach ($item in $unexpectedItems) {
        Write-Warning "    - $item"
    }
    Write-Host "  Consider removing these before publishing." -ForegroundColor Yellow
} else {
    Write-Host "  No unexpected files found" -ForegroundColor Green
}
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build completed successfully!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
