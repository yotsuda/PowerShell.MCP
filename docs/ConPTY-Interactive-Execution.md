# ConPTY Interactive Command Execution

## Overview

Add a ConPTY (Windows Pseudo Console) based interactive execution mode to PowerShell.MCP's `execute_command`.

## Background / Problem

In the current `Invoke-Expression` based implementation, when an external CLI tool (gcloud, az, npm, etc.) emits a prompt asking for user input:

1. The prompt is not shown to the MCP client (Claude)
2. The user cannot provide input
3. The command hangs

## Goals

| Requirement | Current | Goal |
|------|------|------|
| Output capture | ✅ | ✅ |
| Interactive input | ❌ | ✅ |
| Real-time display | ❌ | ✅ |
| ANSI escape support | ⚠️ Partial | ✅ |

## Technology Choice

### ConPTY (Windows Pseudo Console)

- Available on Windows 10 1809+ (Build 17763)
- Used by Windows Terminal and the VS Code integrated terminal
- Full terminal emulation
- Complete control over input and output

### Why Alternatives Were Rejected

| Approach | Reason for rejection |
|------|----------|
| `Start-Process -UseShellExecute` | Cannot capture output |
| `Process.Start + Redirect` | Cannot do interactive input |
| `cmd /c` | stdin redirection limitations |

## API Design

### Adding execute_command Parameters

```json
{
  "pipeline": "gcloud auth login",
  "interactive": true,
  "timeout_seconds": 300
}
```

### Operating Modes

```
interactive = false (default):
  Current Invoke-Expression based execution
  → Fast, for non-interactive commands

interactive = true:
  ConPTY based execution
  → For interactive CLI tools
  → Displays output on the user's console
  → Accepts input from the user
  → After completion, returns all output to MCP
```

## Implementation Plan

### Phase 1: ConPTY Wrapper Class

```csharp
// PseudoConsole.cs
public class PseudoConsole : IDisposable
{
    private IntPtr _handle;
    private SafeFileHandle _inputPipe;
    private SafeFileHandle _outputPipe;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    public PseudoConsole(short width, short height) { ... }
    public void Dispose() { ... }
}
```

### Phase 2: Process Launch

```csharp
// ConPtyProcess.cs
public class ConPtyProcess : IDisposable
{
    private Process _process;
    private PseudoConsole _console;
    private StreamWriter _input;
    private StringBuilder _outputBuffer;

    public async Task<string> RunAsync(string command, CancellationToken ct)
    {
        // 1. Create ConPTY
        // 2. Launch process with STARTUPINFOEX
        // 3. Start output reader task
        // 4. Start user input forwarding task
        // 5. Wait for completion
        // 6. Return buffer contents
    }
}
```

### Phase 3: MCP Integration

```csharp
// ExecuteCommandHandler.cs
public async Task<string> ExecuteAsync(ExecuteCommandParams p)
{
    if (p.Interactive)
    {
        using var conPty = new ConPtyProcess();
        return await conPty.RunAsync(p.Pipeline, cancellationToken);
    }
    else
    {
        // Existing scriptblock-based execution
        return await ExecuteWithCommand(p.Pipeline);
    }
}
```

### Phase 4: Console UI

```
┌─ PowerShell.MCP Interactive ─────────────────┐
│ > gcloud auth login                          │
│                                              │
│ Go to the following link in your browser:    │
│ https://accounts.google.com/o/oauth2/...     │
│                                              │
│ Enter authorization code: █                  │
│                                              │
│ [User input here...]                         │
└──────────────────────────────────────────────┘
```

## File Structure

```
PowerShell.MCP/
├── src/
│   └── PowerShell.MCP/
│       ├── ConPty/
│       │   ├── NativeMethods.cs      # P/Invoke definitions
│       │   ├── PseudoConsole.cs      # ConPTY wrapper
│       │   ├── ConPtyProcess.cs      # Process management
│       │   └── InteractiveRunner.cs  # MCP integration
│       └── Handlers/
│           └── ExecuteCommandHandler.cs  # Existing (modified)
```

## Dependencies

- Windows 10 1809+ (Build 17763)
- .NET 8.0+
- No external packages required (P/Invoke only)

## Test Plan

### Unit Tests

```csharp
[Fact]
public async Task ConPty_CapturesOutput()
{
    using var process = new ConPtyProcess();
    var output = await process.RunAsync("echo hello");
    Assert.Contains("hello", output);
}
```

### Integration Tests

```powershell
# 1. Simple output
execute_command -Interactive -Pipeline "dir"

# 2. Interactive input
execute_command -Interactive -Pipeline "Read-Host 'Name'"

# 3. External CLI
execute_command -Interactive -Pipeline "gcloud auth login"
```

## Risks / Issues

| Risk | Mitigation |
|--------|------|
| Windows only | Document clearly that Linux/macOS are unsupported |
| API complexity | Sufficient error handling and logging |
| Performance | Use the conventional approach for non-interactive cases |
| Timeout handling | Cancel appropriately with CancellationToken |

## References

- [Windows Pseudo Console (ConPTY)](https://devblogs.microsoft.com/commandline/windows-command-line-introducing-the-windows-pseudo-console-conpty/)
- [Creating a Pseudoconsole Session](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
- [Windows Terminal Source](https://github.com/microsoft/terminal)
- [ConPTY Sample](https://github.com/microsoft/terminal/tree/main/samples/ConPTY)

## Milestones

- [ ] Phase 1: Implement ConPTY wrapper (2-3 days)
- [ ] Phase 2: Implement process management (2-3 days)
- [ ] Phase 3: MCP integration (1-2 days)
- [ ] Phase 4: Test / debug (2-3 days)
- [ ] Update documentation (1 day)

**Estimated effort: 8-12 days**
