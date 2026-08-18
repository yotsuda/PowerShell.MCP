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

# Version: 1.14.0

## Behavior Changes
- **A blocking call is now capped to what the connected MCP client will actually wait for.** The `timeout_seconds` you pass to `execute_command` (and `wait_for_completion`) is silently reduced to a ceiling picked from the client's identity in the MCP handshake: **170s for Claude Code**, which was measured not to abort a long call in its default configuration, and **50s for every other or unidentified client**. 50 sits below the 60s default request timeout the MCP SDK ships with, so a client that has not overridden it cannot give up before the module answers. The ceiling is a **deadline measured from the start of the call**, not a budget for the pipeline alone — console discovery, claiming, and above all a cold console spawn are charged against it, so a command dispatched after a 15s spawn gets the remaining 35s (never less than 5s). Nothing is lost when the cap bites: the command keeps running and its result is cached for the next tool call, exactly as with a `timeout_seconds` you set yourself. If you know your client waits longer, override with `POWERSHELL_MCP_TIMEOUT_CEILING` (seconds, still capped at 170). Unlike the standby-reap variables, this one is read by the proxy, so your MCP client's `env` block is the right place for it.

## Bug Fixes
- **A finished command's output is no longer lost when the MCP client gives up on the call.** If the client timed out — or you cancelled — a tool call, and the pipeline then finished normally before the module's own timeout, the module had already emptied the console's cache into a response that no longer had anywhere to land. The result was gone for good, even though the command had succeeded and its output was sitting visible in the console; only the AI's copy was destroyed, and no later tool call could recover it. The proxy now notices the cancellation and pushes that response back into the console's cache — or onto another of the agent's live consoles, if the original one is gone by then — so the next tool call delivers it, led by a note that explains why it arrives late and warns that anything it says about waiting, cancelling, or closing a console is a snapshot from that moment which may no longer apply. This covers every client that sends `notifications/cancelled`, which Claude Code does — including when its own tool timeout fires, not just on an explicit stop. It is a net rather than the primary defence: for a client that abandons a call silently, keeping the module's timeout below the client's (see the ceiling above) is what actually guarantees delivery.

## Internal
- The Named Pipe protocol gains a `cache_output` request, used by the proxy to hand a console back the response it could not deliver. It is handled before the busy gate, since by the time a pushback arrives the console is often already running the next command. `execute_command` and `wait_for_completion` are each split into a thin `[McpServerTool]` shell and a core handler, so the busy auto-route recursion re-enters the core and a nested call cannot stash its own slice of a response the outer call is about to stash whole.

## Security
- **The bundled .NET runtime is pinned to a patched 9.0.19 build.** The proxy binaries shipped in `bin/*` are self-contained, so they carry their own copy of the .NET runtime rather than using the machine's. That copy moves from 9.0.18 to **9.0.19**, picking up fixes for three .NET advisories flagged against this repo (CVE-2026-62899, CVE-2026-62901, CVE-2026-62909). None of the three is reachable in normal use of this module. The first two are HTTP stack issues (request smuggling and a network denial of service), and the proxy speaks stdio and local named pipes only, with no HTTP client or listener anywhere in it. The third is a local elevation of privilege caused by an improper ACL on .NET's diagnostics channel, which every .NET process creates by default; exploiting it requires an attacker who can already sign in to the same machine. No configuration change is needed, updating the module replaces the bundled binaries.

# Version: 1.13.0

## New Features
- **Idle standby consoles now clean themselves up (#53).** When several consoles pile up — e.g. after the AI switches to a new console and the old one is left on standby — an owned console that neither the AI nor you have used for 10 minutes prints a warning (`This console will close in 60s without use…`) and then closes itself, so the desktop no longer fills with abandoned windows. The most-recently-used console of a session is always kept, so the next command still has a console ready — as is any console you have left text half-typed at the prompt, so a window you are in the middle of using is never closed out from under you. (Text at the prompt only vetoes that console's own close; it deliberately does not count as "most recently used", so a forgotten half-typed line cannot make itself the permanent survivor and reap the console the AI is actually working in.) Typing anything at the prompt, or any AI command, cancels a pending close. A console that is busy, waiting at a prompt, or still holding undrained output is never closed. Coordination is entirely local — each console publishes a small marker file under `%LOCALAPPDATA%\PowerShell.MCP` (per-user, no admin; falls back to temp only if unavailable) and elects the survivor by reading them, with no extra traffic to the AI. Tune with `POWERSHELL_MCP_STANDBY_REAP_MINUTES` (default 10; `0` disables) and `POWERSHELL_MCP_STANDBY_REAP_GRACE_SECONDS` (default 60). **Set these where the console itself will see them — a user or system environment variable (Windows: System Properties → Environment Variables, or `setx`; macOS / Linux: your login-shell profile) — not in your MCP client's `env` block.** Each console is a new terminal window rather than a child of the proxy, so a variable set only on the proxy process generally will not reach it: on Windows the console's environment is rebuilt from your user profile, and on macOS the window is created by Terminal.app. (The Linux headless path — no terminal emulator found — is the one case that inherits the proxy's environment directly.)

## Behavior Changes
- **`close_console` now refuses the first attempt when someone is typing in the target.** If the console has unsubmitted text at its prompt — a human is composing a command in it right now — the first `close_console` call closes nothing, leaves the console's pending output untouched, and reports what it saw. Calling `close_console` for the same PID again closes it: **the retry is the confirmation**, and there is deliberately no `force` parameter to bypass the check in one step. A console that is merely `awaiting_input` (a program blocked on `Read-Host`, with no human mid-keystroke) still closes on the first call — that is the tool's main use case and is unaffected. The check fails **open**: an unreachable console, a failed probe, or a stale sample all allow the close, because `close_console` is the escape hatch for a console that is stuck or wedged and a detection failure must never take it away. The pending confirmation expires after 60s, and is dropped if the AI runs a command in that console in the meantime.
- **A command that cannot parse is now reported as `NOT executed (syntax error)` (Discussion #52).** The whole command is parsed up front, before the execution machinery spins up, and the response leads with a `SYNTAX ERRORS` section naming every error with its line, column, and `ErrorId`. Such a command never ran a single statement before either — `[scriptblock]::Create` compiled the full string up front — but it surfaced as a generic terminating exception under `executed with errors`, leaving the AI unsure whether earlier statements had taken effect. The console still mimics an interactive session: the command is echoed and its parse errors follow in red.

## Bug Fixes
- **File edits no longer fail — or stay failed — when the target is briefly locked (#55).** `Add-LinesToFile`, `Update-LinesInFile`, `Update-MatchInFile`, and `Remove-LinesFromFile` could print their preview and then fail the final swap with `Unable to move the replacement file to the file to be replaced`, leaving the file untouched. Three causes, all fixed. The swap now **retries up to 3 times with a short backoff**, so it rides out a transient holder of the target — an antivirus real-time scan, the search indexer, a cloud-sync client, or an editor mid-save — instead of failing on the first attempt. The scratch **backup file is now uniquely named and always removed**; previously it used a fixed `<filename>.tmp` next to the target, which was left behind when the swap failed and then made every later edit of that file fail the same way, so a one-off error became permanent. And when the swap does fail, the error now names **all three paths involved plus the Win32 error code**, rather than only the .NET sentence that identifies no file at all. Temp-file cleanup can also no longer throw over the original error and hide it.
- **`close_console` no longer discards a finished command's output.** When you close a console, any output it still holds from an already-finished command is now harvested over the still-live pipe and returned in the `close_console` response before the console is killed. This includes a **busy** console that is running a new command while an earlier result sits uncollected in its cache — draining pulls only the already-cached completed result, never the running command's partial output, so it is safe in every state. Previously the kill threw the cached result away silently — so following the `awaiting_input` / timeout guidance to abandon a console with `close_console` could lose the output the command had already produced. Draining is best-effort: a console mid-teardown or an unreachable pipe never blocks the close.

## Security
- **The bundled .NET runtime is pinned to a patched 9.0.18 build.** The proxy binaries shipped in `bin/*` are self-contained, so they carry their own copy of the .NET runtime rather than using the machine's. That copy previously came from whatever runtime pack the build resolved by default; it is now pinned to **9.0.18**, picking up fixes for the .NET advisories flagged against this repo (CVE-2026-47302, CVE-2026-50524, CVE-2026-50528, CVE-2026-57108). No configuration change is needed — updating the module replaces the bundled binaries.

# Version: 1.12.0

## New Features
- **`cancel` tool** — stops whatever command is currently running in the agent's active console. It ends a runaway or long-running PowerShell command (`Start-Sleep`, a loop, a slow cmdlet), and sends Ctrl+C to break out of a native CLI that is looping or waiting for input (git/npm/ssh/ping). A command that is stuck in an operation that can't be interrupted (e.g. `[Threading.Thread]::Sleep`, a blocking socket or file read) may not stop — use `close_console`. A PowerShell prompt waiting for you to type something (`Read-Host`, a missing required parameter, `Get-Credential`) also can't be canceled — answer it at the console or use `close_console`. The console itself stays alive and ready for the next command.
- **`close_console` tool** — terminates a session-owned console by PID; use it to abandon a console stuck on a host prompt that cannot be answered programmatically.

## Behavior Changes
- **The `invoke_expression` MCP tool is renamed to `execute_command`.** The old name implied the `Invoke-Expression` cmdlet (which the tool never actually used) and tripped AMSI / antivirus security heuristics. Behavior and the `pipeline` parameter are unchanged. **Migration:** if you pinned the tool in an MCP permission allowlist (e.g. `mcp__PowerShell__invoke_expression`), update it to `execute_command`.
- **Interactive prompts no longer wedge the console.** When an AI-run command hits a PowerShell host prompt (`Read-Host`, a missing mandatory parameter, `Get-Credential`, a confirmation), control returns to the AI immediately instead of waiting out the full timeout, with guidance to answer at the console or close it. The command stays blocked so a human can still answer at the terminal.

## Improvements
- **The console reacts almost instantly when its AI session goes away.** When the AI process that owns a console exits (you close the client, it crashes, or the session ends), the console now shows the "AI session disconnected" notice and clears its `#PID ____` title within ~100ms instead of taking up to ~5s. It reacts to the process actually exiting rather than waiting for the next slow health-check poll.
- **File edits no longer fail across drives, and keep the right permissions (#51).** `Add-LinesToFile`, `Update-LinesInFile`, `Update-MatchInFile`, and `Remove-LinesFromFile` now write their temporary file in the same folder as the file being edited. This keeps the final swap a rename within one drive — fixing the "not same device" failure that happened when the temp file landed on a different drive — and lets a newly created file inherit its folder's permissions.

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
