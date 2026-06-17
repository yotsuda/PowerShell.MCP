# Build-AllPlatforms.ps1
# Builds PowerShell.MCP module and PowerShell.MCP.Proxy for all supported platforms

[CmdletBinding(DefaultParameterSetName = 'Target')]
param(
    [Parameter(Position = 0, ParameterSetName = 'Target')]
    [ValidateSet('Dll', 'WinX64', 'LinuxX64', 'OsxX64', 'OsxArm64')]
    [string[]]$Target,
    # Shortcut for fast iteration on the in-pwsh module: equivalent to
    # -Target Dll (no proxy rebuild for any platform). Useful while
    # iterating on Cmdlets/ or Resources/MCPPollingEngine.ps1 because
    # the proxy.exe stays unchanged and the running MCP session keeps
    # working — only the DLL gets rebuilt and dropped into the module
    # folder.
    [Parameter(ParameterSetName = 'DllOnly')]
    [switch]$DllOnly,
    [string]$Configuration = 'Release',
    [string]$OutputBase,
    [switch]$Sign,
    # Local signing cert for -Sign. Set $env:POWERSHELLMCP_PFX_PATH to your
    # PFX location; kept out of this public repo so the cert's storage path
    # isn't disclosed. Override per-invocation with -PfxPath.
    [string]$PfxPath = $env:POWERSHELLMCP_PFX_PATH,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

# -DllOnly is a sugar shortcut equivalent to -Target Dll. Resolved
# here (before $Target's all-targets default kicks in) so the rest
# of the script just sees a normal $Target list.
if ($DllOnly) {
    $Target = @('Dll')
}

$ErrorActionPreference = 'Stop'

# File locks are released surgically, NOT by killing Claude / Claude Code:
#  - PowerShell.MCP.dll (in the installed module path) is loaded by the
#    MCP-spawned pwsh consoles. Those consoles SURVIVE the proxy's death
#    (they detach and keep running — see MCPPollingEngine's liveness
#    check), so nothing we kill releases this lock. It is swapped via the
#    move-then-copy below, which tolerates the still-loaded old DLL.
#  - bin\<rid>\PowerShell.MCP.Proxy.exe is image-locked by the running
#    MCP server; that single process is stopped just before the proxy
#    publish below. We never touch Claude, Claude Code, or other pwsh.

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

    # Copy DLLs from build output. We ALWAYS use a move-then-copy swap
    # (previously gated on -DllOnly) so a pwsh that still has the old DLL
    # loaded never blocks the deploy. The DLL is held by the MCP-spawned
    # pwsh consoles, which detach and keep running after the proxy dies —
    # so stopping the MCP server alone never frees it. Windows lets us
    # rename the locked file out of the way (the open handle keeps
    # tracking the renamed inode) and drop the new file in at the original
    # name; the orphaned pwsh keeps the old DLL until it exits. Stash
    # files are best-effort cleaned at the start of each run and again
    # right after the swap when the old DLL turned out not to be locked.
    $dllSrc = Join-Path $buildOutputPath 'PowerShell.MCP.dll'
    $dllDst = Join-Path $OutputBase 'PowerShell.MCP.dll'
    Get-ChildItem -LiteralPath $OutputBase -Filter 'PowerShell.MCP.dll.stash-*' -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
    $stashPath = $null
    if (Test-Path $dllDst) {
        $stashPath = Join-Path $OutputBase "PowerShell.MCP.dll.stash-$(Get-Random)"
        Move-Item -LiteralPath $dllDst -Destination $stashPath -Force
    }
    Copy-Item $dllSrc -Destination $OutputBase -Force
    # If the old DLL wasn't actually locked, drop its stash now so the
    # output dir stays clean and the unexpected-files check stays quiet;
    # if a live pwsh still holds it, this fails silently and it lingers.
    if ($stashPath -and (Test-Path $stashPath)) {
        Remove-Item -LiteralPath $stashPath -Force -ErrorAction SilentlyContinue
    }
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

    # Stop ONLY the MCP server (proxy) processes — never Claude, Claude
    # Code, or unrelated pwsh. A running PowerShell.MCP.Proxy image-locks
    # its own bin\<rid>\PowerShell.MCP.Proxy.exe, which `dotnet publish`
    # must overwrite. Stopping just these frees that lock and drops only
    # the MCP "pwsh" server connection; the client app keeps running and
    # respawns the server on next use. (The DLL lock is handled by the
    # move-then-copy swap above, so no pwsh needs stopping here.)
    $proxyProcs = @(Get-Process -Name 'PowerShell.MCP.Proxy' -ErrorAction SilentlyContinue)
    if ($proxyProcs.Count -gt 0) {
        Write-Host "  Stopping $($proxyProcs.Count) running MCP server process(es) to release proxy.exe lock..." -ForegroundColor Gray
        $proxyProcs | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500  # let the OS release the image lock before publish
    }

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

    if (-not $PfxPath -or -not (Test-Path $PfxPath)) {
        Write-Error "  PFX not found. Set `$env:POWERSHELLMCP_PFX_PATH to your signing cert or pass -PfxPath. (got: '$PfxPath')"
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
