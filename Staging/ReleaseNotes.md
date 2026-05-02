# Version: 1.8.0
## New Features
- **Hybrid stream capture: chronological pipeline replaces bucketed sections.** Output, Error, Warning, Verbose, and Debug records now interleave in a single time-ordered text block in the AI response — the AI sees each event in its actual position relative to surrounding output. Pre-1.8 the four separate `=== ERRORS ===` / `=== WARNINGS ===` etc. sections lost the "warning fired between step-A and step-B, then the error hit" context.
- **Five output channels that used to bypass the AI's view are now captured:**
  - `Write-Verbose` (was the AI's blind spot that the tool description explicitly called out as "Verbose and Debug streams are NOT visible to you").
  - `Write-Debug` (same).
  - `[Console]::WriteLine` / `[Console]::Error.WriteLine` direct writes (and any .NET interop that writes to `System.Console` without going through PowerShell streams) → new `=== CONSOLE.OUT (direct) ===` / `=== CONSOLE.ERR (direct) ===` sections.
  - Native exe stderr (`cmd /c "..."`-style) → ErrorRecord in the chronological pipeline, in emit order alongside surrounding output.
  - `What if:` text from `$PSCmdlet.ShouldProcess` and direct `$Host.UI.WriteLine` calls → new `=== HOST.UI (direct) ===` section.
- **Auto-route on busy console.** When `invoke_expression` finds the chosen PowerShell console busy with a user-typed or another AI command, the proxy now spawns a new console at the *source's cwd* and re-runs the pipeline there in the same tool call. Pre-1.8 the AI got `Pipeline NOT executed - verify location and re-execute` and had to re-send manually with whatever cwd they wanted, costing two MCP round-trips for every busy race.
- **`LastExit: N` status-line tag** surfaces the case where a pipeline overall succeeded (`$?` is true) but a native exe within it returned non-zero. The green ✓ badge no longer silently hides those signals.

## Improvements
- Real-time streaming preserved through the new capture wiring — items render to the visible console as they arrive, not collected and rendered after the pipeline finishes.
- Color preserved on the visible console for every stream type: red `Write-Error`, yellow `WARNING:`, yellow `VERBOSE:` / `DEBUG:` prefixes, and `Write-Host`'s user-chosen `ForegroundColor`.
- `Write-Progress` keeps rendering on the visible console for AI-initiated commands (`Compress-Archive`, `Invoke-WebRequest`, etc.) so the user can watch progress. Each redraw of pwsh 7's "Minimal" Progress view also writes the bar text to `Console.Out`; the polling engine recognizes those overlay blocks by their reverse-video bracketed-status framing (`\e[<sgr>m … [\e[7m … \e[27m … ]\e[0m`) and strips them from the captured `=== CONSOLE.OUT (direct) ===` buffer before surfacing to the AI, so the response stays clean.

## Internal
- New `TeeTextWriter` for `[Console]::Out` / `[Console]::Error` tee, written to in parallel with the original streams so visible-console output is unaffected.
- New `TeePSHostUserInterface` decorator wrapping `$Host.UI` (reflected swap on `_externalUI`) for host-UI-level capture of `Write/WriteLine` paths that bypass both PowerShell streams and `[Console]::Out`.
- Stream merge map widened to `2>&1 3>&1 4>&1 5>&1`. Stream 6 (Information) remains unmerged so `Write-Host`'s user-chosen `ForegroundColor` survives to the visible console.

---

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
