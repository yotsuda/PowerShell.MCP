# Console Input Starvation (Windows)

## Overview

A child process started by an AI command can block forever if it reads the
console's input at startup. This note records why, what was measured, and why
four candidate fixes were rejected — so the same ground is not covered twice.

Status: **not fixed.** The workaround is to launch such a program detached with
its standard input taken from `NUL`:

```powershell
cmd /d /s /c "<program> <args> < NUL > out.log 2>&1"
```

## Background / Problem

Three facts combine:

1. Windows serializes readers of a console input buffer. While one blocking
   read is outstanding, another process's read queues behind it.
2. PSReadLine holds such a read for as long as the prompt is active. It ends
   only when an input event actually arrives (see *Rejected 3* — cancelling it
   does not end it).
3. PowerShell.MCP runs an AI command on the runspace's home thread inside the
   polling engine's timer event — which is required, the command must run on
   pwsh's main thread and in the same runspace the user shares — so the prompt
   is still active, and PSReadLine's read is outstanding, for the whole command.

So a child that touches console input while starting up queues behind
PSReadLine and never returns. It is released only when a key finally arrives,
which is why the symptom looks random and why the run appears to finish "the
moment you touch the console".

This is structural, present since the polling engine's first commit
(2025-06-27), not a regression.

### How rare is it?

Rare. Under a deliberately created hazard (a second process blocked reading the
console), these all completed in 0.2–1.0s: `git --version`, `git status`,
`node`, `npm`, `python`, `dotnet`, `curl`, `tar`, `ssh -V`, `where`,
`cmd /c ver`. Only two things blocked: `cmd /c pause`, which genuinely waits for
a key, and LilyPond, which probes the console during Guile initialization.
(`lilypond --version` does *not* block — it never reaches that code.)

An ordinary terminal never shows this, because there the prompt's read has
already completed before the command runs: a command typed by a human ran
LilyPond in 1.5s, 3 times out of 3.

### Reproducing it

In any console, hold a blocking console read from a second process, then start
the program:

```powershell
$reader = Start-Process pwsh -ArgumentList '-NoProfile','-Command','[Console]::ReadKey($true)' -PassThru -NoNewWindow
Start-Sleep 2
& 'C:\bin\lilypond-2.26.0\bin\lilypond.exe' -o out score.ly   # hangs
```

The hung process has a recognisable fingerprint: CPU frozen at ~0.6s, a fixed
number of bytes written (177 for LilyPond 2.26), and several threads in
`EventPairLow`.

## Rejected approaches

| Approach | Rejected because |
|---|---|
| 1. Point `STD_INPUT` at `NUL` for the duration of a command | Kills the console. PSReadLine's key-reader thread throws an unhandled `InvalidOperationException` and pwsh dies. |
| 2. Same, but only for commands that do not declare `timeout_seconds: 0` | Same crash — the carve-out addresses a different objection, not this one. |
| 3. Cancel PSReadLine's `ReadLine` via its `CancellationToken` overload | Does not release the read, and does not even return from `ReadLine`. |
| 4. Write a harmless record into the console input buffer on a timer | Crashes the child process (~3–5%). |

### 1 and 2 — redirecting standard input

Swapping this process's `STD_INPUT_HANDLE` to `NUL` around the pipeline does
remove the hang: LilyPond rendered in 1.4–1.5s inside an MCP console, where it
had hung 7 times out of 7 before. Children genuinely receive `NUL` — verified
from the child's own side, where `GetConsoleMode` on its inherited stdin fails
with `ERROR_INVALID_HANDLE`, and this covers both `Start-Process` and
PowerShell's native-command path.

It is nevertheless unusable. The swap is process-wide (Windows offers no
per-child lever), and .NET's console input APIs re-evaluate the handle, so
PSReadLine's key-reader thread can observe input as "redirected":

```
System.InvalidOperationException: Cannot read keys when either application does
not have a console or when console input has been redirected.
   at Microsoft.PowerShell.PSConsoleReadLine.ReadOneOrMoreKeys()
   at Microsoft.PowerShell.PSConsoleReadLine.ReadKeyThreadProc()
→ The process was terminated due to an unhandled exception.
```

Nothing catches it, so the whole console dies. It is a race — many earlier runs
on long-lived consoles were clean — which makes it worse, not better: a rare
fatal crash is harder to diagnose than a reliable hang.

A carve-out was also built for a separate objection: that the swap takes away
something that works. It does. Measured with a real keypress, a human CAN answer
`cmd /c pause` at an MCP console today — it returned in 2.5s. (Synthetic
`WriteConsoleInput` does not model this: there PSReadLine took the keystroke and
`pause` kept waiting. Do not use injected keys to test who receives input.) The
carve-out keyed on `timeout_seconds: 0`, the tool's existing way of declaring a
CLI that waits on input, and it works — but it does not address the crash above.

### 3 — cancelling the prompt's read

PSReadLine 2.4.5 exposes
`ReadLine(Runspace, EngineIntrinsics, CancellationToken, Nullable<bool>)`, which
suggested a fix that keeps PSReadLine: cancel the read when an AI command
arrives, so nothing is outstanding while the command runs.

It does not work. With a blocked child waiting and the token cancelled, the
child was still blocked 25 seconds later, and `ReadLine` itself had not returned
187 seconds after the cancel. The token appears to be examined between keys, not
during the blocking read.

### 4 — poking the input buffer

Writing a single harmless record (a `FOCUS_EVENT`, or a key-up carrying no
character) into the console input buffer does release the starved reader, does
not satisfy a genuine one (`cmd /c pause` kept waiting through 40 of them), and
leaves PSReadLine's buffer untouched. It also corrupts the child:

| Record written every 150ms | Runs | Crashed with `0xC0000005`, no output |
|---|---|---|
| `FOCUS_EVENT` | 100 | 5 |
| key-up, no character | 100 | 3 |

It is the injected records that do this, not the block-and-release: releasing
the same blocked process by **ending the reader instead**, writing no record at
all, was clean 100 times out of 100 (2.72–2.92s, all exit 0).

## What would actually fix it

Stop holding a blocking console read while an AI command runs. Two shapes:

1. **Run AI commands through the REPL.** Deliver a short, fixed sentinel as
   keystrokes so PSReadLine's read completes normally and the REPL executes the
   command on the main thread, with the real command text passed out of band
   rather than through the console. The core of this is measured: when the
   prompt's read completed, a blocked child was released and exited 0. The
   delivery design is not. Note that arbitrary text cannot be sent this way —
   bracketed paste is not parsed by PSReadLine from a conhost input buffer with
   or without `ENABLE_VIRTUAL_TERMINAL_INPUT`, and raw injection is mangled by
   key bindings (a literal TAB triggers completion). Rework is substantial:
   stream capture, result notification, duration, cancel, awaiting-input, the
   silent probe paths, and a second execution path for platforms where the
   module already replaces PSReadLine.
2. **Use the polling `PSConsoleHostReadLine`** the module already ships for
   macOS/Linux, which polls `Console.KeyAvailable` and holds no blocking read.
   Small, but it costs Windows consoles PSReadLine's editing, history and
   completion, so it could only ever be opt-in.

Neither is worth doing for this hang alone. A residual gap survives both: a
child left running after its command returns can still block once the prompt
resumes.

## What to do instead

The harm here is not mainly the hang — it is that the hang misattributes itself
to the user's program. In the project where this surfaced, three months were
spent blaming Windows Defender, Mark of the Web and DNS, and those wrong
conclusions were written into permanent notes, while the correct workaround sat
in that project's own documentation the whole time. Nobody greps for the answer
they do not have.

So put the knowledge where it does not have to be looked up:

- One line in `execute_command`'s tool description, phrased symptom-first, so an
  AI matches it at the moment a native command produces no output and does not
  return.
- A diagnostic on the timeout path: if a child of this console is sitting at
  near-zero CPU with threads in `EventPairLow`, say so and name the
  `cmd /d /s /c "... < NUL"` form. This changes no behaviour and requires the
  reader to know nothing in advance.
