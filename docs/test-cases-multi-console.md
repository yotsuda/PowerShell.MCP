# PowerShell.MCP Multi-Console Operation Test Cases

## Legend
- 🤖 Executed by Claude
- 👤 Performed by the user
- ✅ Expected result

---

## 1. Basic Behavior

### 1.1 Launch and execute in a single console
🤖 `execute_command('Write-Output "Hello"')`
✅ A new console launches and "Hello" is returned

### 1.2 Reuse an existing console
🤖 `execute_command('$x = 123; Write-Output $x')`
✅ Runs in the same console and "123" is returned

---

## 2. Long-Running Commands and Caching

### 2.1 Timeout and cache retrieval
🤖 `execute_command('Start-Sleep -Seconds 200; Write-Output "Completed"')`
✅ After about 3 minutes, a "Command is still running..." message

🤖 `wait_for_completion(timeout_seconds=120)`
✅ After the command completes, the cache containing "Completed" is returned

### 2.2 Stop button + wait_for_completion
🤖 `execute_command('Start-Sleep -Seconds 60; Write-Output "Done at $(Get-Date)"')`
👤 Press the stop button after about 10 seconds

🤖 `wait_for_completion(timeout_seconds=90)`
✅ After the command completes, the result is returned (contains "Done at ...")

### 2.3 Early return from wait_for_completion
🤖 `execute_command('Start-Sleep -Seconds 10; Write-Output "Quick"')`
👤 Press the stop button immediately

🤖 `wait_for_completion(timeout_seconds=60)`
✅ The result returns in about 10 seconds (does not wait the full 60)

### 2.4 When there is no busy console
🤖 `wait_for_completion(timeout_seconds=30)`
✅ "No busy consoles to wait for." is returned immediately

---

## 3. Concurrent Operation of Multiple Consoles

### 3.1 Concurrent execution and operating in another console
🤖 `execute_command('Start-Sleep -Seconds 60; Write-Output "Console1"')`
👤 Press the stop button after about 10 seconds

🤖 `execute_command('Write-Output "Console2"')`
✅ A new console launches and "Console2" is returned
✅ The response includes Console1's busy status

🤖 `wait_for_completion(timeout_seconds=90)`
✅ Console1's result "Console1" is returned

### 3.2 Multiple busy consoles
🤖 Console 1: `execute_command('Start-Sleep -Seconds 60; Write-Output "A"')`
👤 Press the stop button

🤖 In console 2: `execute_command('Start-Sleep -Seconds 30; Write-Output "B"')`
👤 Press the stop button

🤖 `wait_for_completion(timeout_seconds=90)`
✅ The shorter one (B) completes first and its result is returned

🤖 `wait_for_completion(timeout_seconds=90)`
✅ The remaining result (A) is returned

---

## 4. Console Switching

### 4.1 Switching to a user-launched console
👤 Run `Start-McpServer` in another pwsh window

🤖 `execute_command('Write-Output "Test"')`
✅ "Console switched. Pipeline NOT executed" message

🤖 `execute_command('Write-Output "Test"')`
✅ Executes normally

### 4.2 Switching away from a busy console
🤖 `execute_command('Start-Sleep -Seconds 120; Write-Output "Slow"')`
👤 Press the stop button after about 10 seconds

👤 Run `Start-McpServer` in another pwsh window

🤖 `execute_command('Write-Output "Fast"')`
✅ Switches to the user-launched console and executes

🤖 `wait_for_completion(timeout_seconds=150)`
✅ The original console's result "Slow" is returned

### 4.3 Cache aggregation when switching to a completed console
🤖 Console A: `execute_command('pause')`
👤 Press the stop button (console A is busy)

🤖 Console B: `execute_command('pause')`
👤 Press the stop button (console B is busy)

👤 Complete the pause in console A (press Enter)
(Console A is now completed, with a cached result)

🤖 `execute_command('Get-Date')`
✅ Switches to console A
✅ **The pause result (status line) is included in the response**
✅ "Console switched. Pipeline NOT executed" message
✅ Console B's busy status is shown

🤖 `execute_command('Get-Date')`
✅ Get-Date executes normally

---

## 5. Edge Cases

### 5.1 Force-closing a console
🤖 `execute_command('Start-Sleep -Seconds 60; Write-Output "Test"')`
👤 Press the stop button
👤 Close the PowerShell console window

🤖 `wait_for_completion(timeout_seconds=30)`
✅ No error (handled as a dead pipe)

### 5.2 Preventing double cache consumption
🤖 `execute_command('Start-Sleep -Seconds 30; Write-Output "Once"')`
👤 Press the stop button

🤖 `wait_for_completion(timeout_seconds=60)`
✅ "Once" is returned

🤖 `wait_for_completion(timeout_seconds=10)`
✅ "No busy consoles to wait for." is returned (not consumed twice)

### 5.3 Cache aggregation from multiple completed consoles
🤖 Console A: `execute_command('pause')`
👤 Press the stop button

🤖 Console B: `execute_command('pause')`
👤 Press the stop button

🤖 Console C: `execute_command('pause')`
👤 Press the stop button

👤 Complete the pause in all of consoles A, B, and C

🤖 `get_current_location()`
✅ The pause results from all three consoles are shown
✅ The current location information is returned

---

## 6. Integration with get_current_location

### 6.1 Cache retrieval via get_current_location
🤖 `execute_command('Start-Sleep -Seconds 20; Write-Output "BG"')`
👤 Press the stop button

🤖 After about 25 seconds, `get_current_location()`
✅ The "BG" result and the current location are returned

### 6.2 Retrieving a completed console's cache via get_current_location
🤖 Console A: `execute_command('pause')`
👤 Press the stop button

🤖 Work in console B (console B becomes active)

👤 Complete the pause in console A

🤖 `get_current_location()`
✅ Console A's pause result is shown (auto-aggregated by the DLL)
✅ The current location information is returned

---

## 7. Idle Auto-Close

Set the user environment variables `POWERSHELL_MCP_STANDBY_REAP_MINUTES=1` and `POWERSHELL_MCP_STANDBY_REAP_GRACE_SECONDS=10` for these (user or system scope — consoles do not inherit the proxy's environment), then restart the MCP client.

### 7.1 A stale standby console closes and the AI is told why
🤖 Console A: `execute_command('Write-Output "A"')`
🤖 `start_console(reason='second console')` → console B
🤖 In console B: `execute_command('Write-Output "B"')` (B is now the most recently used console)
👤 Wait about 70 seconds without touching either window
✅ Console A prints the idle warning after 1 minute and closes 10 seconds later; console B stays open

🤖 `execute_command('Get-Date')`
✅ Runs in B. The response starts with `⚠ Console PID #<A> <name> was closed: it closed itself after 1 min without use …` and mentions that its Start-Job jobs ended with it
✅ No later response repeats the line

### 7.2 A running background job keeps a standby console open
🤖 Console A: `execute_command('Start-Job { Start-Sleep 150 } | Out-Null; "job started"')`
🤖 `start_console(reason='second console')` → console B, then `execute_command('Write-Output "B"')` there
👤 Wait 2 minutes
✅ Console A is still open: no warning while the job runs
👤 Wait another 90 seconds (the job has finished)
✅ Console A now warns and closes

### 7.3 A console closed by hand is reported without a reason
🤖 Console A: `execute_command('Write-Output "A"')`, then console B as in 7.1
👤 Close console A's window
🤖 `execute_command('Get-Date')`
✅ Response starts with `⚠ Console PID #<A> <name> was closed: its window was closed or its process ended outside this server.`

### 7.4 Two sessions do not see each other's consoles
👤 Open a second MCP client session on the same `pwsh` server registration (e.g. a second Claude Code window)
🤖 Session 1: `execute_command('"one"')`; 🤖 Session 2: `execute_command('"two"')`
✅ Each session gets its own console; neither response mentions the other's console
👤 Let session 1's console idle past the threshold while session 2 keeps working
✅ Session 1's console is the only console of its session, so it is the keeper and stays open

---

## Recommended Execution Order

1. 2.4 → 1.1 → 1.2 (basic behavior)
2. 2.2 → 2.3 (wait_for_completion basics)
3. 3.1 (multiple consoles)
4. **4.3 (cache aggregation when switching to a completed console)** ← important
5. 5.2 (double-consumption prevention)
6. 5.3 → 6.2 (multi-cache aggregation)
7. Other cases as needed
