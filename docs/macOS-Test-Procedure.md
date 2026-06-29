# PowerShell.MCP macOS Test Procedure

## Overview

Use a Scaleway M1 Mac mini to verify PowerShell.MCP behavior on macOS.

## 1. Setting Up the Scaleway Mac mini M1

### 1.1 Create an Account / Launch an Instance

1. Go to https://console.scaleway.com
2. Create an account (credit card registration required)
3. Select **Bare Metal** > **Apple Silicon** > **Mac mini M1**
4. Region: `Paris 3` (PAR3)
5. OS: Select `macOS Sequoia` or the latest version
6. Register an SSH key
7. Create the instance (**minimum 24-hour billing: approx. €2.64**)

### 1.2 Check Connection Details

After creating the instance, check the following:
- **IP address**: Shown in the console
- **VNC password**: Generated / shown in the console

### 1.3 SSH Connection

```bash
ssh m1@<IP_ADDRESS>
```

### 1.4 VNC Connection (For GUI Testing)

Connect from macOS / Windows / Linux with a VNC client:
- **Address**: `<IP_ADDRESS>:5900`
- **Password**: The one shown in the console

---

## 2. Installing PowerShell 7

### 2.1 Install Homebrew (If Not Already Installed)

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

After installing, add it to PATH:
```bash
echo 'eval "$(/opt/homebrew/bin/brew shellenv)"' >> ~/.zprofile
eval "$(/opt/homebrew/bin/brew shellenv)"
```

### 2.2 Install PowerShell 7

```bash
brew install powershell/tap/powershell
```

### 2.3 Verify Installation

```bash
pwsh --version
# Example output: PowerShell 7.5.x
```

### 2.4 Check the pwsh Path

```bash
which pwsh
# Example output: /opt/homebrew/bin/pwsh
```

> ⚠️ **Important**: Homebrew installs to `/opt/homebrew/bin`, but
> the current `PwshLauncherMacOS` only sets `/usr/local/bin:/usr/bin:/bin` in PATH.
> This may need to be fixed.

---

## 3. Installing PowerShell.MCP

### 3.1 Option A: Install from PowerShell Gallery (Recommended)

```powershell
pwsh -Command "Install-Module PowerShell.MCP -Scope CurrentUser"
```

### 3.2 Option B: Build from Source

```bash
# Install .NET 9 SDK
brew install dotnet@9

# Clone the repository
git clone https://github.com/yosbits/PowerShell.MCP.git
cd PowerShell.MCP

# Build (for macOS)
dotnet publish PowerShell.MCP.Proxy -c Release -r osx-arm64 -o ./out/osx-arm64
dotnet build PowerShell.MCP -c Release
```

---

## 4. Test Items

### 4.1 Module Import Test

```powershell
pwsh
Import-Module PowerShell.MCP
```

**Checkpoints:**
- [ ] Imports without errors
- [ ] The `[PowerShell.MCP] MCP server started` Information message appears
- [ ] The Named Pipe server is running

### 4.2 Verify Named Pipe Existence

```bash
ls -la /tmp/CoreFxPipe_*
# Expected: /tmp/CoreFxPipe_PowerShell.MCP.Communication exists
```

### 4.3 Get-MCPProxyPath Test

```powershell
Get-MCPProxyPath
# Expected: /path/to/PowerShell.MCP/bin/osx-arm64/PowerShell.MCP.Proxy
```

### 4.4 Proxy Standalone Launch Test

In a separate terminal:
```bash
/path/to/PowerShell.MCP.Proxy
```

**Checkpoints:**
- [ ] The Proxy starts
- [ ] `[INFO]` logs are written to stderr

### 4.5 start_powershell_console Test (Most Important)

**Preparation:**
1. Exit pwsh once (close the Named Pipe)
2. Close Terminal.app

**Test steps:**

```bash
# Launch the Proxy
/path/to/PowerShell.MCP.Proxy
```

Call `start_powershell_console` from an MCP client (or manually via JSON-RPC).

**Checkpoints:**
- [ ] Terminal.app opens in a new window
- [ ] pwsh is running
- [ ] The PowerShell.MCP module is imported
- [ ] The Named Pipe connection is established

### 4.6 execute_command Test

```json
{
  "name": "execute_command",
  "pipeline": "Get-Process | Select-Object -First 5",
  "execute_immediately": true
}
```

**Checkpoints:**
- [ ] The command runs
- [ ] The result is returned
- [ ] The command and result are shown in Terminal.app

### 4.7 Verify PSReadLine Is Disabled

```powershell
Get-Module PSReadLine
# Expected: nothing returned (the module is not loaded)
```

---

## 5. Known Issues and Items to Check

### 5.1 PATH Issue

**Problem**: `PwshLauncherMacOS` only sets `/usr/local/bin:/usr/bin:/bin` in PATH

**Check**: Homebrew's pwsh may not be found
```csharp
// PowerShellProcessManager.cs Line 196
var path = "/usr/local/bin:/usr/bin:/bin";
```

**Candidate fix**:
```csharp
var path = "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin";
```

### 5.2 AppleScript Escaping Issue

**Problem**: When the HOME path contains `'`

**Check**: Behavior when the username contains special characters
```csharp
// Line 213
$"    do script \"env -i HOME='{home}' USER='{user}' ...
```

### 5.3 Terminals Other Than Terminal.app

**Check**: Behavior when another terminal such as iTerm2 is the default

---

## 6. Troubleshooting

### 6.1 pwsh Not Found

```bash
# Create a symbolic link
sudo ln -s /opt/homebrew/bin/pwsh /usr/local/bin/pwsh
```

### 6.2 Named Pipe Connection Timeout

```bash
# Verify the Named Pipe exists
ls -la /tmp/CoreFxPipe_*

# Check permissions
stat /tmp/CoreFxPipe_PowerShell.MCP.Communication
```

### 6.3 Terminal.app Does Not Open

```bash
# Test AppleScript manually
osascript -e 'tell application "Terminal" to activate'
osascript -e 'tell application "Terminal" to do script "echo test"'
```

### 6.4 Module Import Error

```powershell
# Check detailed errors
$ErrorActionPreference = 'Continue'
Import-Module PowerShell.MCP -Verbose
```

---

## 7. After Testing Is Complete

### 7.1 Record Results

Note the following:
- macOS version
- PowerShell version
- Pass/Fail for each test item
- Any error messages that occurred
- Screenshots (taken via VNC)

### 7.2 Delete the Instance

**Important**: After 24 hours, if no longer needed, delete the instance to stop billing

Scaleway console > Apple Silicon > select instance > Delete

---

## 8. Areas Likely to Need Fixing

In priority order:

1. **Add `/opt/homebrew/bin` to PATH** - Homebrew-installed pwsh is not found
2. **AppleScript escaping** - special character support
3. **Support terminals other than Terminal.app** - iTerm2, etc.

---

## Reference Links

- [Scaleway Apple Silicon](https://www.scaleway.com/en/apple-silicon/)
- [PowerShell on macOS](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-macos)
- [Homebrew](https://brew.sh/)
