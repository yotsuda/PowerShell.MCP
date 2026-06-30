# Changelog

All notable changes to **PowerShell.MCP** are documented here. Each release is
grouped under a `# Version: X.Y.Z` heading, newest first. Releases earlier than
1.7.7 are listed on the
[GitHub Releases page](https://github.com/yotsuda/PowerShell.MCP/releases).

<!--
Format contract (load-bearing — do not change the heading shape):
  # Version: X.Y.Z
  ## Highlights / New Features / Improvements / Bug Fixes / Internal
  - Bullet point

The release.yml workflow extracts the section whose heading matches the pushed
tag (v1.9.0 -> "# Version: 1.9.0") to build the GitHub Release body. Tagging
without a matching section here fails the workflow on purpose. Add new version
sections at the TOP of the file; keep older sections for history.
-->

# Version: 1.12.0

## New Features
- **`cancel` tool** — interrupts the command running in the agent's active console: it stops a runaway / long-running PowerShell pipeline (`Start-Sleep`, a loop, a slow cmdlet) by stopping the home runspace's running pipeline, and sends Ctrl+C to break a native CLI that is looping or waiting on stdin (git/npm/ssh/ping). A command stuck in a non-cooperative blocking call (e.g. `[Threading.Thread]::Sleep`, a blocking socket/read) may not stop — use `close_console`. A PowerShell host prompt (`Read-Host`, a missing mandatory parameter, `Get-Credential`) is likewise not cancellable and surfaces as `awaiting_input` — answer it at the console or `close_console`. The engine keeps running through the cancel.
- **`close_console` tool** — terminates a session-owned console by PID; use it to abandon a console stuck on a host prompt that cannot be answered programmatically.
- **`awaiting_input` status** — when a command hits an interactive PowerShell host prompt (`Read-Host`, a missing mandatory parameter, `Get-Credential`, a confirmation), the call now returns control immediately instead of wedging the console until the timeout. The command stays blocked so a human can still answer at the terminal.

## Behavior Changes
- **The `invoke_expression` MCP tool is renamed to `execute_command`.** The old name implied `Invoke-Expression` (which the tool never used — it runs `[scriptblock]::Create` + `& $sb`) and tripped AMSI / security heuristics. Behavior and the `pipeline` parameter are unchanged. **Migration:** if you pinned the tool in an MCP permission allowlist (e.g. `mcp__PowerShell__invoke_expression`), update it to `execute_command`.

## Improvements
- **Proxy-exit detection latency cut from ~5s to ~100ms.** The console now learns its owning proxy has exited via a `Process.Exited` event instead of a slow liveness poll, so the "session disconnected" notice and the `#PID ____` title update almost immediately.
- **Atomic file edits preserve volume and ACLs (#51).** `Add-LinesToFile`, `Update-LinesInFile`, `Update-MatchInFile`, and `Remove-LinesFromFile` now stage their temp file in the target file's own directory, keeping the swap a same-volume rename (eliminating the cross-volume "not same device" failure) and letting a newly created file inherit the destination directory's ACEs.

## Internal
- Completed the `execute_command` rename through the Named Pipe wire protocol, DTOs, and handlers (previously only the MCP tool surface was renamed).
- Translated the remaining Japanese documentation and test names/comments to English.

# Version: 1.11.0

## New Features
- **`Restart-MCPServer` command** to retry starting the console engine in the affected console — no need to restart PowerShell.

## Behavior Changes
- **Resume-safe console handling (new server session).** On the proxy's first console attach after a (re)start — the resume / cold-start boundary, where the runtime is fresh but the AI still carries cwd/variable/module assumptions from earlier in the conversation — `invoke_expression` normalizes the working directory to `$HOME` and announces a **new server session** (with a one-line restore hint for a reclaimed console's prior cwd), while `get_current_location` / `start_console` preserve the console's current cwd and only announce. This stops a resumed session from silently running at an unexpected cwd or relying on state that didn't carry over.
- **Reuse-first console acquisition.** Without a `reason`, `start_console` now reclaims any available console — an owned standby **or** an unowned one (a prior session's released console, or a user-started one) — and only launches a new window when nothing is available. Previously an unowned console was reclaimed only when `start_location` was given; that guard is gone (the new-session normalization above handles the cwd concern), so the desktop no longer fills with console windows.

## Bug Fixes
- **Reduced an AMSI / antivirus false positive that could block startup (#50).** The embedded polling engine no longer uses `Invoke-Expression`. Its three call sites only wrapped literal strings (cmdlet/type resolution is already deferred to runtime, and each was inside `try/catch`), so they added nothing but the single highest-weighted AMSI heuristic token — which helped the engine script get flagged as malicious at `Import-Module`. They are now direct calls; the engine contains zero `Invoke-Expression`.
- **No more spurious "cwd changed" warnings caused by the AI's own work.** A directory change left by an AI command (including one harvested in the background via `wait_for_completion`) is now recorded, so the next command no longer misreads it as a user-typed `cd`. Paths that differ only by a trailing separator (`C:\proj` vs `C:\proj\`) no longer trip a false drift warning either.
- **A disconnected AI session no longer leaks its output to the next one.** When the owning AI goes away, the console discards its undrained cached output and returns to a clean `standby` state, so a freshly-connecting AI can't drain the previous session's results.
- **Fixed a possible `NullReferenceException`** when resolving a console's display name while no pipe was active.

## Improvements
- **Graceful degradation when the engine is blocked at startup.** If an antivirus/AMSI scan blocks the embedded engine during `Import-Module` (often transient), the module now stays loaded and emits one actionable warning instead of failing with a bare error. While the engine is down, commands fast-fail with guidance (run `Restart-MCPServer`) instead of hanging until the request times out.
- **A green `AI session connected.` line** is now shown when the AI claims or spawns a console — the visible counterpart to the yellow `AI session disconnected` notice.
- **`Get-MCPOwner` and `Restart-MCPServer` share one `PowerShell.MCP.Status` output type** with identical columns (EngineReady / Owned / ProxyPid / AgentId / ClientName / LastError).

## Internal
- **Hardened console launching:** the Windows init command is built from the shared helper (escaping the agent id like the other platforms), console-readiness checks go through a single `PipeStatus.IsReady` definition, and dead launcher code was removed.
- **Release safety gates:** tagged releases now run the full unit suite (net8.0 + net9.0) and an `Import-Module` smoke test of the assembled package before publishing to PSGallery; a missing CHANGELOG section fails fast; PRs are gated and the polling engine is checked to stay free of `Invoke-Expression` (the #50 regression guard).
- **Expanded automated tests:** multi-console identity/routing, per-agent isolation, cross-AI console visibility (via real named pipes), cwd-drift detection and normalization, the resume / first-attach new-session treatment, and engine graceful-degradation.

# Version: 1.10.0

## New Features
- **`--no-profile` flag for lean interactive consoles (#49, thanks [@sharpninja](https://github.com/sharpninja)).** Pass `--no-profile` in the MCP server's `args` and the interactive launchers (Windows / macOS / Linux) start pwsh with `-NoProfile`, skipping the user's `$PROFILE` (prompt, aliases, PSReadLine, theme). Default off — those consoles are real human-facing shells, so the profile loads unless the operator opts out. One line in the client config makes every console the server launches lean.

## Behavior Changes
- **The headless / CI launcher always starts pwsh with `-NoProfile`,** independent of the `--no-profile` flag. It only runs when no terminal emulator is available — no window, stdout redirected — so there is no interactive experience to preserve, and a profile there only adds nondeterminism, startup latency, and the risk of blocking on input (e.g. `Read-Host`) in a process with no console.

## Internal
- Launcher command-line construction is centralized in shared per-platform `Build*` helpers (#49); `-NoProfile` is emitted from a single `noProfile`-gated point per interactive platform, covered by unit tests for both flag states plus the always-on headless path.


# Version: 1.9.0

## Highlights

**AI working-directory tracking now follows `Set-Location` correctly, and the elevation consent prompt is gone.** Pre-1.9 the DLL tracked the OS *process* cwd, but PowerShell's `Set-Location` moves only `$PWD` / the PSDrive — so once the AI `cd`'d anywhere, cwd tracking was silently pinned to the startup directory, and busy-route / auto-start spawned new consoles at `$HOME` instead of resuming the AI's workspace. That tracking is now correct. Separately, the `sudo` / `runas` / `gsudo` Y/N consent prompt has been removed (#48) — it blocked unattended use, was never a real security boundary, and PowerShell.MCP's safety model is the visible, human-watched console.

## Behavior Changes
- **Removed the `sudo` / `runas` / `gsudo` elevation consent prompt (#48).** The `Read-Host` Y/N gate hung with no human to answer under unattended / SSH-admin use, and was never a real security boundary: any startup-read flag is settable by the agent itself and inherited by a freshly spawned console, so the gate never actually contained a misaligned agent. PowerShell.MCP's real safety model is the visible, human-watched console; singling out elevation was arbitrary, and the matching regex also misfired on incidental mentions of `sudo`.

## Improvements
- **PSDrive-aware working-directory tracking.** The DLL now captures `$PWD` on the polling engine's home thread (instead of the OS process cwd, which `Set-Location` never updates) and uses it for every cwd-emitting response — busy, status, and post-execution success / timeout / completed. Busy-route and auto-start now resume the AI's actual workspace instead of `$HOME`. User-`cd` drift between AI calls is handled safety-first: the proxy returns a `Pipeline NOT executed` notice carrying prev → new cwd and a single-quote-escaped `Set-Location` revert hint, rather than silently auto-`cd`'ing.
## Bug Fixes
- **Tab-completion menus no longer mojibake on CJK Windows.** Consoles created via `CREATE_NEW_CONSOLE` inherited the system code page (932 / 936 / 949 on JP / CN / KR Windows). A shared encoding prelude now runs `chcp 65001` plus the `[Console]::*Encoding` sets *before* PSReadLine loads, so e.g. Japanese asset names render cleanly in a `Get-OrchAsset <Tab>` menu.
- **Sub-agents no longer lose their `🔑 agent_id` notice.** The notice — a freshly allocated sub-agent's only way to learn its own ID — was emitted on just a few return paths; a sub-agent whose first call landed on a timeout / cached / error / drift-bail branch could lose its ID forever. Every return now routes through one helper that prepends the notice exactly when the ID was newly allocated.
- **`Remove-LinesFromFile` preserves the trailing newline when the last line is removed.** Deleting the final line of a file with a CRLF tail previously dropped the tail.
- **`-Encoding gb18030` no longer collapses to GB2312.** GB18030 (CP 54936) is a 4-byte Unicode superset; it was aliased to CP 936 (GBK), which silently substituted out-of-GBK 4-byte CJK characters with `?`. It now resolves to CP 54936.
- **Regex display and combined `-Contains -Pattern` matching corrected.** `Update-MatchInFile` regex mode now expands `$1` / `$2` capture-group references in the AI-visible display (the written file and `-WhatIf` path were already correct). `Show-TextFiles` and `Remove-LinesFromFile` now wrap each side of the combined `Contains | Pattern` regex in a non-capturing group, so a Pattern with top-level alternation or a `{0,3}`-style quantifier no longer matches every line.
- **Proxy-liveness poll no longer leaks process handles.** `GetProcessById` returns a `Process` holding an OS handle; it is now disposed on every ~5 s poll instead of waiting on the GC finalizer.

## Internal
- Full-codebase review cleanup: removed an unreachable prompt-localization overload (`WithLocalizedPromptsFromAssembly` + `LocalizedParameterNameAttribute`), dead helpers, stale comments, a doc-comment segment-count error, and a literal typo — no behavior change.
- Test / CI reliability: each test instance now allocates a unique sub-agent id to deflake parallel xunit runs; the Linux Named-Pipe integration test was updated for the same-call switch-and-execute shape; the CwdDrift revert-hint assertion was made platform-agnostic.
- Dropped the orphan `dist/PowerShell.MCP.psm1` snapshot and added an explicit `dist/` gitignore rule — `Staging/` is the only module source.


# Version: 1.8.0

## Highlights

**Verbose / Debug / native exe stderr are now visible to the AI.** Pre-1.8 the tool description explicitly told you "Verbose and Debug streams are NOT visible to you" and native exe stderr (e.g. `cmd /c '... 1>&2'`) was effectively swallowed. They now appear in the AI response in the same time-ordered position as everything else — closing the AI's biggest documented blind spot in the Output→Error→Warning→Verbose→Debug capture surface.

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

## Bug Fixes
- `get_current_location` now sets the window title when it claims an unowned console. Pre-fix, an AI whose first tool call was `get_current_location` (instead of `invoke_expression` or `start_console`, both of which already handled this) left the user's pre-existing `Import-Module PowerShell.MCP` console with the placeholder title `#PID ____` until some later tool call redrew it. Symptom appeared intermittently depending on which tool the AI happened to call first.

## Improvements
- Real-time streaming preserved through the new capture wiring — items render to the visible console as they arrive, not collected and rendered after the pipeline finishes.
- Color preserved on the visible console for every stream type: red `Write-Error`, yellow `WARNING:`, yellow `VERBOSE:` / `DEBUG:` prefixes, and `Write-Host`'s user-chosen `ForegroundColor`.
- `Write-Progress` keeps rendering on the visible console for AI-initiated commands (`Compress-Archive`, `Invoke-WebRequest`, etc.) so the user can watch progress. Each redraw of pwsh 7's "Minimal" Progress view also writes the bar text to `Console.Out`; the polling engine recognizes those overlay blocks by their reverse-video bracketed-status framing — an ANSI SGR escape, then `[`, then a reverse-video toggle (`ESC[7m` … `ESC[27m`), then `]` and reset (`ESC[0m`) — and strips them from the captured `=== CONSOLE.OUT (direct) ===` buffer before surfacing to the AI, so the response stays clean.

## Internal
- New `TeeTextWriter` for `[Console]::Out` / `[Console]::Error` tee, written to in parallel with the original streams so visible-console output is unaffected.
- New `TeePSHostUserInterface` decorator wrapping `$Host.UI` (reflected swap on `_externalUI`) for host-UI-level capture of `Write/WriteLine` paths that bypass both PowerShell streams and `[Console]::Out`.
- Stream merge map widened to `2>&1 3>&1 4>&1 5>&1`. Stream 6 (Information) remains unmerged so `Write-Host`'s user-chosen `ForegroundColor` survives to the visible console.
- Single shared `BuildInitCommand` now drives the PowerShell init script for every non-Windows launcher (macOS tempFile, Linux Base64-encoded terminal launch, Linux headless ArgumentList path). Each platform keeps its own delivery mechanism for documented reasons — AppleScript echo on macOS, multi-shell quoting on Linux — but the script body and its single-quote escaping are now built in one place. xUnit pins the escaping for every platform that calls the helper.

---

# Version: 1.7.7
## New Features
- Authenticode-signed Windows binaries (`PowerShell.MCP.dll`, `bin/win-x64/PowerShell.MCP.Proxy.exe`) so the module is accepted on WDAC / Device Guard machines with a one-time trust of the signer. Closes #46.

## Improvements
- Bundle LGPL-2.1 license texts for Ude.NetStandard (compliance obligation).
- Build-AllPlatforms.ps1 gains `-Sign` switch with interactive PFX passphrase for local release builds.

## Bug Fixes
- Status line truncation was broken for pipelines beginning with leading whitespace.
