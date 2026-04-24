# Version: 1.7.7
## New Features
- Authenticode-signed Windows binaries (`PowerShell.MCP.dll`, `bin/win-x64/PowerShell.MCP.Proxy.exe`) so the module is accepted on WDAC / Device Guard machines with a one-time trust of the signer. Closes #46.

## Improvements
- Bundle LGPL-2.1 license texts for Ude.NetStandard (compliance obligation).
- Build-AllPlatforms.ps1 gains `-Sign` switch with interactive PFX passphrase for local release builds.

## Bug Fixes
- Status line truncation was broken for pipelines beginning with leading whitespace.

---

<!--
Release notes format:
  # Version: X.Y.Z
  ## New Features / Improvements / Bug Fixes / Internal
  - Bullet point

The release.yml workflow extracts the section matching the pushed tag version
(v1.7.8 → finds "# Version: 1.7.8"). Releasing a tag without a matching section
here will fail the workflow on purpose.

Add new version sections at the TOP of the file. Keep older sections for history.
-->
