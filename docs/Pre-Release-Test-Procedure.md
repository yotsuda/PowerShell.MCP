# Pre-Release Test Procedure (manual, on a second machine)

Run this on a machine that is **not** the development machine, with the
build of the release commit installed the normal way, and an MCP client
(Claude Code) driving it. The unit suite and the dev-server checks cover the
logic; this covers what only a real install shows: the tool list the client
receives, consoles that outlive a conversation, timing, windows, and a
second machine for remote control.

Legend (as in `test-cases-multi-console.md`): 🤖 the AI does it (you ask for
it in the conversation), 👤 you do it, ✅ expected.

Record each step as pass / fail with the response text that mattered; a
failure blocks the tag.

---

## 0. Setup

1. On the test machine, in PowerShell 7, from a clone at the release commit:
   ```powershell
   .\Build-AllPlatforms.ps1          # builds all RIDs into the installed module path
   .\Tests\Run-AllTests.ps1          # must be green here too
   Get-Module PowerShell.MCP -ListAvailable | Select-Object Version, ModuleBase
   ```
   `Get-MCPProxyPath` must point into that module folder, and
   `(Get-Item (Get-MCPProxyPath)).VersionInfo.ProductVersion` must carry the
   release commit's hash.
2. Shorten the idle close so the auto-close tests take minutes, not hours.
   Consoles do not inherit the proxy's environment, so these go in the
   **user** scope, then the MCP client is restarted:
   ```powershell
   [Environment]::SetEnvironmentVariable('POWERSHELL_MCP_STANDBY_REAP_MINUTES', '1', 'User')
   [Environment]::SetEnvironmentVariable('POWERSHELL_MCP_STANDBY_REAP_GRACE_SECONDS', '10', 'User')
   ```
3. Close every PowerShell.MCP console window left from before. Restart the
   MCP client. Start a new conversation.
4. 🤖 Ask the AI which `pwsh` tools it has.
   ✅ Eleven: `start_console`, `get_current_location`, `execute_command`,
   `wait_for_completion`, `cancel`, `close_console`, `connect_remote_console`,
   `accept_remote_console`, `execute_remote_command`, `cancel_remote_command`,
   `disconnect_remote_console`. **No `wait_for_remote_completion`.**
5. 🤖 `execute_command('"hello"')` ✅ a console opens; the first call reports
   the routine location check, the re-issue runs. 🤖 `wait_for_completion`
   ✅ "No commands to wait for completion …".

---

## 1. Idle auto-close (changed: jobs keep a console; the close is reported)

### 1.1 A stale standby console closes, and the AI is told why
🤖 Console A: `execute_command('"A"')`
🤖 `start_console(reason='second console')` → console B; `execute_command('"B"')` runs there.
👤 Wait about 75 seconds without touching either window.
✅ Console A prints `⚠ This console has been idle and will close in 10s without use …`, then `Closing idle console.`, and its window closes. Console B stays open.
🤖 `execute_command('Get-Date')`
✅ Runs in B. The response **begins** with
`⚠ Console PID #<A> <name> was closed: it closed itself after 1 min without use (idle standby, not this session's most recently used console; a 10s warning was shown in its window). Any Start-Job jobs it was running ended with it; a process it started with Start-Process may still be running.`
🤖 `execute_command('Get-Date')` again ✅ the line is **not** repeated.

### 1.2 A running background job keeps a standby console open
🤖 Console A: `execute_command('Start-Job { Start-Sleep 150 } | Out-Null; "job started"')`
🤖 `start_console(reason='second console')` → console B; `execute_command('"B"')` there.
👤 Wait 2 minutes.
✅ Console A is still open and has shown **no** warning (the job is running).
👤 Wait another 90 seconds (the job has finished).
✅ Console A warns, then closes. 🤖 `execute_command('Get-Date')` ✅ the closed line appears once, as in 1.1 (the minutes are counted from the console's last use, the `Start-Job` command, so about "after 3 min").

### 1.3 A console closed by hand is reported without a reason
🤖 Console A: `execute_command('"A"')`; 🤖 `start_console(reason='second')`; `execute_command('"B"')`.
👤 Close console A's window with the mouse.
🤖 `execute_command('Get-Date')`
✅ The response begins with `⚠ Console PID #<A> <name> was closed: its window was closed or its process ended outside this server. Any Start-Job jobs …`.

### 1.4 The only console of a session never closes
👤 Make sure one console is left (close the others with `close_console` or by hand). Wait 3 minutes.
✅ It is still open, with no warning: the most recently used console is the keeper.

### 1.5 Typed text keeps a console open
Repeat 1.1, but after console B's command type `Get-` (no Enter) in console A's window.
✅ Console A shows no warning while the text sits there; press Esc, and it warns and closes about 70 seconds later.

---

## 2. A command right after a long one (fixed: stale heartbeat)

### 2.1 Two tool calls in one turn
🤖 Ask: *"Run these as two separate execute_command calls in the same response: first `Start-Sleep 12; "slow"`, then `"fast"`."*
✅ Both run in the **same** console. The second response has **no** `Started new console` and **no** `was busy`.
(Before this release the second call always started a new console: the first console's heartbeat was 12 s old and it answered "busy (user command)".)

### 2.2 Variation with a user command
👤 In the AI's console window, type `Start-Sleep 20` and press Enter.
🤖 `execute_command('"during user command"')`
✅ A new console starts (or an existing standby one is used), and the same line says why:
`ℹ️ Started new console PID #<B> <name> with PowerShell.MCP module imported. PID #<A> <name> was busy (user command, …s) and stays with this session.`
The seconds shown are a few, not 0 (the proxy looked twice, 150 ms apart, before giving up on it).

---

## 3. The console that was passed over is named (new)

### 3.1 After a timeout
🤖 `execute_command('Start-Sleep 25; "long one done"', timeout_seconds=5)`
✅ `⧗ Pipeline is running | … | Duration: 5.0…s` and the wait_for_completion hint.
🤖 `execute_command('"second"')`
✅ Response, in this order: the busy block for the first console marked `[other console]`; `ℹ️ Started new console PID #<B> <name> with PowerShell.MCP module imported. PID #<A> <name> was busy (Start-Sleep 25; "long one d..., …s) and stays with this session.`; then the result `second` from B.
🤖 `wait_for_completion(timeout_seconds=30)` ✅ `long one done` from A.

### 3.2 start_console and get_current_location say it too
With console A busy (`Start-Sleep 25`, timeout 5) and no other console:
🤖 `get_current_location()` ✅ the location JSON comes from a new console, preceded by `ℹ️ PID #<A> <name> was busy (Start-Sleep 25; …, …s) and stays with this session.`
Repeat with 🤖 `start_console()` (no reason) ✅ `PowerShell console started successfully …` followed by the same `ℹ️ … was busy … and stays with this session.` line.

---

## 4. One wait for local and remote consoles (changed) — needs a second machine

Second machine (a VM is fine) with PowerShell 7.4+ and this build installed
(`Install-Module PowerShell.MCP -RequiredVersion <version>` once published;
before publishing, copy the built module folder there). On it:
`Import-Module PowerShell.MCP; Enable-MCPRemoteControl -AllowFrom <test machine's IP>`,
confirm, and read the code to the AI. (For a NAT'd VM use
`accept_remote_console` and the `-ConnectTo` line instead; the tests are the same.)

### 4.1 The remote timeout points at wait_for_completion, and the result arrives there
🤖 `connect_remote_console(host, code)` ✅ Connected.
🤖 `execute_remote_command('Start-Sleep 20; "remote done"', timeout_seconds=5)`
✅ `[remote console: <host>]`, the running status line, and `Use wait_for_completion to wait for the result (it also arrives with your next tool call) …` — **never** the word `wait_for_remote_completion` anywhere in this test.
🤖 `execute_command('"local meanwhile"')`
✅ The local result, and a block `[remote console: <host>]` with the remote busy status and `Still running. wait_for_completion waits for it; cancel_remote_command interrupts it.`
🤖 `wait_for_completion(timeout_seconds=30)`
✅ Returns when the remote command ends (about 15 s): `[remote console: <host>]`, its status line, `remote done`.
🤖 `wait_for_completion` again ✅ "No commands to wait for completion …" (collected once).

### 4.2 A remote result arrives with the next local call, no wait
🤖 `execute_remote_command('Start-Sleep 8; "late remote"', timeout_seconds=3)` ✅ timeout.
👤 Wait 10 seconds.
🤖 `execute_command('"local"')`
✅ The response carries `[remote console: <host>]` … `late remote` **and** the local result. Nothing to collect afterwards.

### 4.3 Earlier remote result stays apart from a new remote command's output
🤖 `execute_remote_command('Start-Sleep 8; "first"', timeout_seconds=3)` ✅ timeout.
👤 Wait 10 seconds.
🤖 `execute_remote_command('"second"')`
✅ Two blocks: the collected `first` (its own status line) **before** the `second` block.

### 4.4 Cancel
🤖 `execute_remote_command('Start-Sleep 60', timeout_seconds=3)` ✅ timeout.
🤖 `cancel_remote_command()` ✅ `Cancel (Ctrl+C) sent … Use wait_for_completion to see where it stopped.`
🤖 `wait_for_completion()` ✅ `[remote console: <host>]` and the interrupted-pipeline output.

### 4.5 Local and remote at once: whichever finishes first
🤖 `execute_command('Start-Sleep 25; "local slow"', timeout_seconds=3)` ✅ timeout.
🤖 `execute_remote_command('Start-Sleep 8; "remote fast"', timeout_seconds=3)` ✅ timeout.
🤖 `wait_for_completion(timeout_seconds=60)`
✅ Returns after about 5 s with `remote fast`, and the local console still listed as busy.
🤖 `wait_for_completion(timeout_seconds=60)` ✅ `local slow` after the rest.

### 4.6 A sub-agent does not see remote results (optional; needs a sub-agent)
🤖 `execute_remote_command('Start-Sleep 8; "main only"', timeout_seconds=3)`; then, as a sub-agent (`is_subagent=true`), `wait_for_completion`.
✅ The sub-agent gets "No commands to wait for completion …"; the main agent's next call gets `main only`.

### 4.7 Reconnect message
👤 On the remote machine, disable and re-enable the network adapter (or pause the VM) while a remote command runs.
✅ Any error from the proxy about the dropped connection says `wait_for_completion`, not `wait_for_remote_completion`; once the console is back, `wait_for_completion` collects the output.

🤖 `disconnect_remote_console()` when done.

---

## 5. Regression smoke (unchanged behaviour)

Run, from `test-cases-multi-console.md`, in this order: 2.4 → 1.1 → 1.2 → 2.2 → 2.3 → 3.1 → 4.3 → 5.2 → 5.3 → 6.2. All must behave as written there.

---

## 6. Cleanup

```powershell
[Environment]::SetEnvironmentVariable('POWERSHELL_MCP_STANDBY_REAP_MINUTES', $null, 'User')
[Environment]::SetEnvironmentVariable('POWERSHELL_MCP_STANDBY_REAP_GRACE_SECONDS', $null, 'User')
```
Restart the MCP client. On the remote machine, `Disable-MCPRemoteControl` if it is still listening.

---

## What this release changed (for the tester's orientation)

| Area | Change | Tests |
|------|--------|-------|
| Idle auto-close | A console with an unfinished `Start-Job` is not idle; a console that closes itself leaves a note and the next tool call reports it with the reason; one closed by hand is reported without one | 1.x |
| Discovery | Completing a command counts as a heartbeat, so a call right after a long command stays in the same console; a console reporting "(user command)" is asked once more after 150 ms | 2.x |
| Notices | When the AI's console is passed over, the response names it and quotes its status at that moment | 3.x |
| Wait | `wait_for_remote_completion` removed; `wait_for_completion` waits for local and remote consoles; pending remote results arrive with any tool call; main agent only | 0.4, 4.x |
| Remote (from main) | Up to four remote consoles, `remote` parameter; shorter accept guidance, Hyper-V firewall advice | 4.x, docs |
