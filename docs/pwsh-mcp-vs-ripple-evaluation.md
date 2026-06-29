# PowerShell.MCP vs ripple+pwsh: Comparative Evaluation and Improvement Candidates

Date: 2026-04-24
Evaluator: Claude (opus-4-7, 1M context session)
Background: Compared by actually using both MCP servers within the same session (build loop + refactor implementation + live testing)

This memo is an unvarnished evaluation. It has no intent to flatter either server; it dispassionately records facts observed from actual use and improvement candidates.

---

## Scope

A comparison limited to "the use case of executing pwsh (PowerShell)."
Session-specific and externally-dependent factors are excluded — e.g. the `Build.ps1` self-kill problem, mcp-sitter's auto-respawn, and the shared-visible-console human+AI common-platform philosophy.

---

## Areas Where PowerShell.MCP (pwsh MCP) Wins

### 1. Response speed for one-off commands
At the `"hello"` level it's around 0.3s, significantly faster than ripple+pwsh's 0.8–1.1s.
ripple's prompt fn chain (OSC emission + exit-code resolver + $Error delta + ...) incurs a warm-up cost. pwsh MCP is lighter because its chain is thinner.

### 2. Output envelope stability
The status line is always in full form: `| Errors: 0 | Warnings: 0 | Info: 0 | Location [FileSystem]: ...`.
The AI parser can expect the same shape without conditional branching. With ripple the presence of `LastExit: N` / `--- errors ---` varies depending on state, so the parser gains more conditional branches.

### 3. Simplicity (implementation / mental model)
A thin wrapper that returns pwsh's Error / Warning / Information / Verbose streams straightforwardly as-is.
ripple's $LASTEXITCODE snapshot and OSC R/T machinery is "clever" at the cost of higher complexity.

---

## Areas Where ripple+pwsh Wins

### 1. Semantic fidelity
For a pipeline like `cmd /c exit 7; "after"`:
- pwsh MCP: only reports "success." The native non-zero exit is completely invisible to the AI.
- ripple: surfaces it with a `LastExit: 7` tag.

This is not a matter of "information that can't be obtained" but "information it doesn't try to obtain." A structural gap.

### 2. $LASTEXITCODE leak prevention
pwsh's $LASTEXITCODE is a global variable updated only by native exes. If you run `2+3` right after `cmd /c exit 7`, exit 7 lingers even though nothing produced it.
- pwsh MCP: may report the lingering value as-is.
- ripple: takes a snapshot in PreCommandLookupAction, compares it with the post-command value, and prevents the leak via a this-pipeline-changed-it determination.

### 3. Multi-line delivery speed
With `encoded_scriptblock` (base64 1-line invocation, equivalent to dot-source) there is zero disk I/O.
- pwsh MCP: multi-line 0.4s
- ripple: 0.1s (about 4×)

Notable with complex if / foreach / function definitions.

### 4. Preservation of cmdlet provenance
The error from `Get-Item /nope` comes back as `Get-Item: Cannot find path '...'` (with the cmdlet prefix). Matches PowerShell ConciseView inline display.
- pwsh MCP: only the message in the `=== ERRORS ===` section, with the cmdlet name missing.

### 5. Output choice (`strip_ansi`)
You can choose ANSI strip / retain per-call.
- pwsh MCP: strips uniformly. You can't use pwsh MCP for cases where you want to see color.

---

## Even / Trade-offs

### Error structuring
- pwsh MCP: `=== ERRORS === / === WARNINGS === / === INFO ===` pre-separated, intuitive
- ripple: `--- errors (N) ---` structured list; Warnings/Info are not tracked

**Trade-off**: pwsh MCP carries more information, but `Write-Warning` bypasses the user proxy when invoked via a cmdlet — so pwsh MCP's count is not always accurate (depends on the cmdlet implementation). ripple deliberately does not track for this reason.

### SGR preservation
- pwsh MCP: strips uniformly, cheap on tokens
- ripple: preserves uniformly (default), heavy on tokens, opt-in control via `strip_ansi: true`

### Output spill
- pwsh MCP: at 30000 characters, temp file + `Show-TextFiles -Pattern` guidance
- ripple: at 15000 characters, spill + `search_files` / `read_file` guidance

Only the threshold and the tools differ; the philosophy is the same (overflow → drill down with an external tool).

---

## Conclusion

**Viewed as a tool for executing pwsh:**

- **pwsh MCP is "fast, light, simple."** A thin API wrapper that returns PowerShell's native semantics as-is.
- **ripple+pwsh is "deep, slow, smart."** It actively applies corrections for pwsh's pitfalls ($LASTEXITCODE leak, a pipeline-ok hiding a native exit, opacity of the cmdlet proxy).

For a one-off "run it and give me the result," pwsh MCP is enough. For complex build / CI / script diagnostics where you want an accurate picture of pwsh's behavior, ripple.

**In terms of engineering depth**: ripple, which both faithfully reflects pwsh native semantics AND corrects the pitfalls, is superior. pwsh MCP's strength is the simplicity of "showing PowerShell straightforwardly," but its weakness is being "so straightforward that it passes pwsh's own traps right through."

If you want to "run the AI fast like a secretary," pwsh MCP; if you want to "capture the current state accurately like a diagnostician," ripple.

---

# Improvement Candidates

## Improvement Candidates for pwsh MCP

### 🔴 Priority HIGH

#### 1. The per-call noise of `SCOPE WARNING: Use $script:`

**Current**: Every local variable assignment like `$r = ...` emits `⚠️ SCOPE WARNING: Local variable assignment(s) detected: $r → Consider using $script:r to preserve across calls`. Seen 20+ times in one session.

**Problem**:
- The warning itself is useful (local variables vanish between pwsh MCP sessions)
- But it appears too frequently, re-teaching every time something you can learn once and remember
- A lot of visual noise

**Proposed fix**:
- Teach it once via a banner at session start
- Or detune it to "after warning N times for the same variable name, go silent thereafter"
- Or make it opt-in via a verbose mode flag

**Implementation cost**: Small (modify the emission logic in one place)

---

#### 2. Native exe non-zero exit is silent

**Current**: Running `cmd /c exit 7; "after"` makes pwsh MCP report only "success." The AI cannot notice that cmd returned exit 7.

**Problem**: In CI / build script diagnostics you miss "a native tool returned a warning-like non-zero." In practice: if `npm install` → `npm audit` ends non-zero but you `&& next-command` into an overall success, pwsh MCP reports "OK."

**Proposed fix**: ripple's implementation approach is portable:
1. Snapshot `$global:__pm_lec_at_start = $LASTEXITCODE` in PreCommandLookupAction
2. In the prompt fn, `$lecChanged = $lec -ne $lecAtStart`
3. When `$? true && $lecChanged && $lec != 0`, add `| NativeExit: N` to the status line

**Implementation cost**: Medium (additional logic equivalent to a prompt hook; ~40 lines if there's an integration script)

---

### 🟡 Priority MEDIUM

#### 3. Always displaying zero-tags in the status line

**Current**: Always outputs `| Errors: 0 | Warnings: 0 | Info: 0 |`, even on the happy path.

**Problem**: Wastes tokens. ripple emits these only when N > 0; the happy path is clean.

**Proposed fix**: Omit zero-valued tags (render only when N > 0).

**Trade-off**: The AI parser needs to assume "tag absent = 0." The consistency of the envelope shape degrades slightly. Since previously-explicit values disappear, watch out for backwards compat.

**Implementation cost**: Small (conditional branch in the equivalent of FormatStatusLine)

---

#### 4. The provider prefix in `Location [FileSystem]: ...` is verbose

**Current**: The provider name is shown explicitly, as in `Location [FileSystem]: C:\MyProj\ripple`.

**Problem**: 99% of cases are `FileSystem`; it's verbose. Other providers (Env:, HKCU:, Variable:) are rare.

**Proposed fix**: Omit the prefix for FileSystem; attach `[Env]:` etc. only for non-FileSystem.

**Implementation cost**: Small

---

### 🟢 Priority LOW

#### 5. First command latency

**Current**: The first command in a session is 1.11s; from the second onward it's around 0.3s.

**Problem**: Poor feel right after session start.

**Proposed fix**: Make the PowerShell.MCP module init lazy, or background pre-warm. Root-cause analysis needed.

**Implementation cost**: Requires profiling

---

#### 6. Learning curve of the `Show-TextFiles` integration

**Current**: Large output is saved to a temp file and you're prompted to use the `Show-TextFiles -Pattern` cmdlet. Being a custom cmdlet, the AI is unlikely to notice it on first encounter.

**Proposed fix**: Also note the standard `Get-Content -Raw | Select-String` fallback hint, or strengthen the tool description on the prompt side.

**Implementation cost**: Small (change the prompt wording)

---

## Improvement Candidates for ripple (for reference)

### 🔴 Priority HIGH

#### A. Session-state non-determinism in $LASTEXITCODE leak detection

With `cmd /c exit 7; "after"`, LastExit sometimes appears and sometimes doesn't. If the state just before is `$LASTEXITCODE = 7`, then `$lecChanged = False` and detection is missed.

A complete fix is difficult due to PS runtime constraints. For now the shortest path is to state the limitation explicitly in the docstring.

### 🟡 Priority MEDIUM

#### B. Renderer speed for large plain-text output
`Get-ChildItem` over 2000 files: 2.2s vs pwsh MCP's 0.85s. Unverified, but the renderer's per-char cell allocation is suspect. Consider a deterministic fast-path (run-based).

#### C. Decomposing the 504-line `HandleExecuteAsync` method
It coordinates async/TCS/lock, so the risk when touching it is high. Do it phased the next time a feature change touches it.

### 🟢 Priority LOW

#### D. Deterministic tracking of the Warning / Info stream
pwsh's `WARNING: ` / `INFORMATION: ` prefixes are a deterministic contract, not a heuristic. Simply counting by line prefix would yield accuracy comparable to pwsh MCP.

#### E. Schematizing the SGR default
Default preserve is reasonable under the shared-console philosophy, but in an AI-only worker pattern it wastes tokens. There's room to switch the default based on the console's visible attribute.

---

## Order of Work (pwsh MCP improvements)

The order to tackle after this memo:

1. **#1 Detune SCOPE WARNING** — biggest UX improvement / lowest risk. per-call → session-banner.
2. **#2 Surface native exe non-zero exit** — port ripple's approach; large practical gain for CI diagnostics.
3. **#3 Zero-tag omission** — small implementation, requires backwards-compat care.
4. **#4 Location provider prefix** — cosmetic, if there's spare capacity.

#5 and #6 are separate work (profiling / prompt wording), out of scope for this session.
