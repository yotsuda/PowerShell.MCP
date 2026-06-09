# Release Guide

This document describes how to cut a release of **PowerShell.MCP** and publish it to the [PowerShell Gallery](https://www.powershellgallery.com/packages/PowerShell.MCP).

> **Audience**: repo maintainers with PSGallery co-owner rights.
> Secrets (API keys, signing certificate password, vault paths) are **never** stored in this repo. They live only in GitHub Actions repo secrets and in the maintainer's local environment.

---

## Overview

A release of PowerShell.MCP consists of:

1. Version bump across **three** files (manifest + two `.csproj`)
2. New section added to `CHANGELOG.md`
3. Pre-flight local build / test
4. Commit + push to `main`
5. Tag `vX.Y.Z` + push tag → GitHub Actions handles everything downstream

The GitHub Actions workflow at `.github/workflows/release.yml` handles: multi-RID build, PlatyPS help XML generation, Authenticode signing (DLL + `win-x64` Proxy.exe only), signing-distribution assertion, PSGallery publish, and GitHub Release creation from `CHANGELOG.md`.

Manual fallback is documented at the bottom but should rarely be needed.

---

## Prerequisites

Each maintainer who can publish needs:

- **GitHub permissions**: Write or Admin on this repo
- **PSGallery account** listed as a co-owner of the `PowerShell.MCP` package
  - Request via the [Manage Owners](https://www.powershellgallery.com/packages/PowerShell.MCP/Manage) page
  - Each owner generates their own API key at [PSGallery API Keys](https://www.powershellgallery.com/account/apikeys)
- **Code signing certificate** (yotsuda's `yotsuda.pfx`) — required, not optional
  - Rationale: WDAC / Device Guard environments block unsigned native binaries. See [issue #46](https://github.com/yotsuda/PowerShell.MCP/issues/46).
  - The PFX and its password are distributed out-of-band and stored as GitHub Actions secrets
- Local toolchain:
  - PowerShell 7.4+
  - .NET SDK 8.0 and 9.0 (both — main DLL is net8.0, Proxy is net9.0)
  - Git
  - `gh` CLI (for tag push and release verification)

---

## Versioning

PowerShell.MCP follows [Semantic Versioning](https://semver.org/):

- **MAJOR** — breaking changes to cmdlet parameters, MCP tool contracts, or proxy protocol
- **MINOR** — new cmdlets, new MCP tools, new parameters (backward-compatible)
- **PATCH** — bug fixes, documentation, performance

The current version lives in **three** files that must be kept in sync. The release workflow's `Verify version consistency` step fails fast if they disagree.

| File | Format | Example |
| --- | --- | --- |
| `Staging/PowerShell.MCP.psd1` | 3-part | `ModuleVersion = '1.7.8'` |
| `PowerShell.MCP/PowerShell.MCP.csproj` | 4-part | `<Version>1.7.8.0</Version>` |
| `PowerShell.MCP.Proxy/PowerShell.MCP.Proxy.csproj` | 4-part | `<Version>1.7.8.0</Version>` |

---

## Release procedure

### 1. Bump version (three files)

Edit all three files to the same logical version (`1.7.8` / `1.7.8.0`):

```powershell
# Manual edits to:
#   Staging/PowerShell.MCP.psd1              — ModuleVersion = '1.7.8'
#   PowerShell.MCP/PowerShell.MCP.csproj     — <Version>1.7.8.0</Version>
#   PowerShell.MCP.Proxy/PowerShell.MCP.Proxy.csproj — <Version>1.7.8.0</Version>
```

### 2. Add a release-notes section

Prepend a new `# Version: X.Y.Z` section to `CHANGELOG.md`:

```markdown
# Version: 1.7.8
## New Features
- ...

## Improvements
- ...

## Bug Fixes
- ...


# Version: 1.7.7
... (previous sections kept for history) ...
```

The workflow extracts the section matching the pushed tag; tagging without a matching section fails the workflow before publish (this is intentional).

### 3. Local build smoke test

Verify the module builds cleanly and loads on the dev machine:

```powershell
.\Build-AllPlatforms.ps1              # Builds all 4 RIDs, writes to installed module path
Import-Module PowerShell.MCP -Force
Get-Module PowerShell.MCP             # Should show the new version
Get-Command -Module PowerShell.MCP    # All expected cmdlets present
```

Run the test suite:

```powershell
.\Tests\Run-AllTests.ps1
```

Optional (but recommended for signed releases): verify local signing still works end-to-end:

```powershell
.\Build-AllPlatforms.ps1 -Sign        # Prompts once for PFX password
```

The `-Sign` path is only needed to test the local signing flow. The **actual signing used by the release happens in CI** from the GHA secrets — the local signed copy is never published.

### 4. Commit and push to main

```powershell
git add Staging/PowerShell.MCP.psd1 `
        PowerShell.MCP/PowerShell.MCP.csproj `
        PowerShell.MCP.Proxy/PowerShell.MCP.Proxy.csproj `
        CHANGELOG.md
git commit -m "Bump version to 1.7.8"
git push origin main
```

(Use separate commits for actual code changes that comprise the release; the version-bump + release-notes commit goes last.)

### 5. Tag and push — this triggers the release

```powershell
git tag v1.7.8
git push origin v1.7.8
```

That tag push starts `release.yml`, which:

1. Verifies all 3 version sources match `v1.7.8`
2. Builds DLL (net8.0) + Proxy for all 4 RIDs (`win-x64`, `linux-x64`, `osx-x64`, `osx-arm64`, self-contained)
3. Generates Get-Help XML via PlatyPS
4. Assembles the module directory (DLL, psd1, psm1, Ude.NetStandard.dll, bin/\*/Proxy, en-US/\*.xml, licenses/\*, THIRD_PARTY_NOTICES.md)
5. **Signs exactly two files**: `PowerShell.MCP.dll` + `bin/win-x64/PowerShell.MCP.Proxy.exe` with timestamp from DigiCert
6. Asserts the signing distribution is correct (signed files signed, unsigned files unsigned) — fails publish if not
7. Publishes to PSGallery
8. Extracts the matching section from `CHANGELOG.md` and creates a GitHub Release with that body

Total runtime: ~3–5 minutes.

### 6. Post-release verification

```powershell
# PSGallery side
Find-Module PowerShell.MCP -RequiredVersion 1.7.8

# Signature distribution (Save-Module pulls the published copy — does NOT install)
$tmp = Join-Path $env:TEMP "verify-$(Get-Random)"
Save-Module PowerShell.MCP -RequiredVersion 1.7.8 -Path $tmp
$base = Join-Path $tmp 'PowerShell.MCP\1.7.8'
Get-ChildItem $base -Recurse -File |
    Where-Object { $_.Name -match '\.(dll|exe|psd1|psm1)$' } |
    ForEach-Object {
        $s = Get-AuthenticodeSignature $_.FullName
        [pscustomobject]@{
            File      = $_.Name
            Status    = $s.Status
            Signer    = $s.SignerCertificate.Thumbprint
            Timestamp = [bool]$s.TimeStamperCertificate
        }
    } | Format-Table -AutoSize
```

Expected:

- `PowerShell.MCP.dll` — `Valid`, timestamped
- `bin/win-x64/PowerShell.MCP.Proxy.exe` — `Valid`, timestamped
- `psd1` / `psm1` / `Ude.NetStandard.dll` — `NotSigned`
- Non-Windows Proxy binaries — `UnknownError` (Authenticode is Windows-only; this is expected and not a bug)

---

## Signing rules

These are **load-bearing** rules. The release workflow's `Assert signing distribution` step enforces them; violating them fails the release.

**Sign**:

- `PowerShell.MCP.dll`
- `bin/win-x64/PowerShell.MCP.Proxy.exe`

**Do NOT sign**:

- `PowerShell.MCP.psd1` — `Install-Module` verifies Authenticode signatures on script files and rejects self-signed ones on any machine where the cert is not pre-trusted. Signing the psd1 with a self-signed cert bricks `Install-Module` for most users. (Learned the hard way on UiPathOrch 0.9.16.5.)
- `PowerShell.MCP.psm1` — same reason
- `Ude.NetStandard.dll` — third-party binary, we do not re-sign vendor code
- `bin/linux-x64/PowerShell.MCP.Proxy`, `bin/osx-x64/PowerShell.MCP.Proxy`, `bin/osx-arm64/PowerShell.MCP.Proxy` — Authenticode is a Windows-only signature format; ELF and Mach-O binaries cannot carry it

### Why self-signed is acceptable

PowerShell.MCP uses a self-signed code-signing certificate (`yotsuda.pfx`). The public key is published at <https://github.com/yotsuda/code-signing>. Users in WDAC environments add it as a trusted publisher once, after which every future release passes policy without per-version hash exceptions. A commercial CA cert would remove the one-time trust step but costs hundreds of USD/year; the tradeoff is documented in [issue #46](https://github.com/yotsuda/PowerShell.MCP/issues/46).

### Timestamp

All signatures include a timestamp from `http://timestamp.digicert.com`. This means signatures remain verifiable after the signing cert itself expires, because the timestamp proves the signature existed while the cert was still valid.

---

## GitHub Actions secrets

The `release.yml` workflow requires all three of these. They are set via `gh secret set`.

| Secret | Purpose |
| --- | --- |
| `PSGALLERY_API_KEY` | API key with `Push` scope for the `PowerShell.MCP` package |
| `CODE_SIGNING_PFX_BASE64` | Base64-encoded `yotsuda.pfx` |
| `CODE_SIGNING_PFX_PASSWORD` | Password for `yotsuda.pfx` |

Setup commands (run once per fresh repo / rotation):

```powershell
# PFX base64 (no password prompt). Point $env:POWERSHELLMCP_PFX_PATH at your
# local signing cert — its storage location is intentionally kept out of this repo.
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($env:POWERSHELLMCP_PFX_PATH))
gh secret set CODE_SIGNING_PFX_BASE64 --repo yotsuda/PowerShell.MCP --body $b64

# PFX password (interactive, hidden input from gh)
gh secret set CODE_SIGNING_PFX_PASSWORD --repo yotsuda/PowerShell.MCP

# PSGallery API key (interactive, hidden input from gh)
gh secret set PSGALLERY_API_KEY --repo yotsuda/PowerShell.MCP
```

If any secret is missing, the workflow's `Guard — Windows binaries must be signed for a tagged release` step throws before publishing.

---

## Rollback

PSGallery does not allow deleting or overwriting a published version. If a release is broken:

1. Publish a patch version (`X.Y.Z+1`) with the fix via the normal release procedure
2. In the PSGallery UI, **unlist** the broken version (Manage Package → Unlist)
   - Unlisted versions remain installable by exact version but are hidden from `Find-Module`
3. Update `CHANGELOG.md` to note the pulled version

Never force-push a release tag — create a new tag instead.

---

## Manual publish (emergency fallback)

Use only if the GHA workflow is broken and a release cannot wait.

```powershell
# Build + sign locally (writes to installed module path by default)
.\Build-AllPlatforms.ps1 -Sign

$ver = '1.7.8'
$modulePath = 'C:\Program Files\PowerShell\7\Modules\PowerShell.MCP'

# Stage a CLEAN copy to publish. The installed path accumulates build
# leftovers (*.bak, *.stash-*, *.new-*) that Publish-Module would otherwise
# package into the .nupkg. The GHA path doesn't hit this because it assembles
# the module from scratch in a fresh RUNNER_TEMP dir.
$stage = Join-Path $env:TEMP 'PowerShell.MCP-publish'
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $modulePath $stage -Recurse
Get-ChildItem $stage -Recurse -File |
    Where-Object { $_.Name -like '*.bak' -or $_.Name -like '*.stash-*' -or $_.Name -like '*.new-*' } |
    Remove-Item -Force

# Verify signatures on the staged copy
Get-ChildItem $stage -Recurse -File |
    Where-Object { $_.Name -in @('PowerShell.MCP.dll','PowerShell.MCP.Proxy.exe') } |
    Get-AuthenticodeSignature | Format-Table Status, Path

# Publish — use your own PSGallery API key
Publish-Module -Path $stage -NuGetApiKey <your-api-key>

# Create the GitHub Release with ONLY this version's section (mirrors release.yml's
# extraction — do NOT pass the whole CHANGELOG.md, that dumps every past version).
$inSection = $false
$section = foreach ($line in Get-Content CHANGELOG.md) {
    if ($line -match '^# Version:\s*(\S+)') {
        if ($matches[1] -eq $ver) { $inSection = $true; continue }
        elseif ($inSection) { break }
    }
    if ($inSection) { $line }
}
$notes = ($section -join "`n").Trim()
if (-not $notes) { throw "No CHANGELOG.md section found for $ver" }
$notesFile = Join-Path $env:TEMP "release-notes-$ver.md"
Set-Content -Path $notesFile -Value $notes -Encoding UTF8
gh release create "v$ver" --title "v$ver" --notes-file $notesFile
```

Prefer fixing the workflow over using this path — manual publishes bypass the signing-distribution assertion and are easier to get wrong (e.g., leaving signed script files that would break `Install-Module` on user machines).

---

## Checklist (quick reference)

- [ ] Version bumped in all 3 files (psd1 / 2 csproj) to matching value
- [ ] `CHANGELOG.md` has a new `# Version: X.Y.Z` section at the top
- [ ] `.\Build-AllPlatforms.ps1` clean
- [ ] `.\Tests\Run-AllTests.ps1` green
- [ ] Bump commit pushed to `main`
- [ ] Tag `vX.Y.Z` pushed
- [ ] GHA `Release` workflow succeeded
- [ ] `Find-Module PowerShell.MCP -RequiredVersion X.Y.Z` returns the new version
- [ ] `Save-Module` verification shows `PowerShell.MCP.dll` + `win-x64/Proxy.exe` signed, nothing else
- [ ] GitHub Release page shows the notes extracted from `CHANGELOG.md`
