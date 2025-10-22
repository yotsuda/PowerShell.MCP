## Enhanced Command Feedback & Major Stability Update

This release delivers comprehensive execution statistics for better command visibility and a critical stability fix that eliminates unpredictable behavior.

## ✨ Key Improvements

### **📊 Execution Statistics Dashboard**
Every command now displays real-time statistics:
- ⏱️ Execution duration (accurate to milliseconds)
- ❌ Error count (errors + exceptions combined)
- ⚠️ Warning count
- ℹ️ Information message count
- 📍 Current location and provider
- Visual status indicators (✓ for success, ✗ for errors)

**Example Output:**
```
✓ Pipeline executed successfully | Duration: 0.52s | Errors: 0 | Warnings: 0 | Info: 0 | Location: C:\MyProj [FileSystem]

Your command output here...
```

### **🔴 Error Stream Now Visible in Console**

**The Problem:**
- Prior versions: Error stream was not displayed in PowerShell console
- Root cause: `Invoke-Expression`'s `-ErrorVariable` parameter suppresses console display
- Users could only see errors in MCP clients, not in their own console

**The Solution:**
- Errors are captured for MCP transmission
- After execution, captured errors display in console with red formatting
- **Trade-off**: Errors appear at the end, not in chronological order (unavoidable with `-ErrorVariable`)

### **🎯 Critical Stability Fix: Preference Variables**

**What Was Wrong:**
- Previous versions modified `$VerbosePreference`, `$DebugPreference`, and `$InformationPreference` to capture streams
- Despite using `finally` blocks to restore values, restoration frequently failed
- This caused unpredictable behavior and side effects in user scripts

**What's Fixed:**
- Preference variables are never modified
- Commands execute with your original settings intact
- Improved stability and predictability
- Verbose/Debug streams display naturally in console (not sent to MCP clients - copy manually if needed)

### **📝 Better Output Formatting**
- Clear visual separation with consistent spacing
- Organized sections for different stream types (ERRORS, WARNINGS, SUCCESS, INFO)
- Enhanced readability for multi-stream outputs

## 📦 Installation & Upgrade

### 🆕 **New Installation**
```powershell
Install-Module PowerShell.MCP -Force
```

### 🔄 **Upgrading from Previous Version**
```powershell
Update-Module PowerShell.MCP
```

### ⚙ **Post-Installation Configuration**

**Important**: Update your MCP client configuration to use the new version path.

**Step 1**: Get your module path
```powershell
Import-Module PowerShell.MCP
(Get-Module PowerShell.MCP).ModuleBase
# Example: C:\Users\YourName\Documents\PowerShell\Modules\PowerShell.MCP\1.2.3
```

**Step 2**: Update Claude Desktop config (`%APPDATA%\Claude\claude_desktop_config.json`)
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "C:\\Users\\YourName\\Documents\\PowerShell\\Modules\\PowerShell.MCP\\1.2.3\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

**Step 3**: Restart Claude Desktop

📖 **Detailed instructions**: https://github.com/yotsuda/PowerShell.MCP#installation

## 📊 What Changed

| Feature | v1.2.2 | v1.2.3 | Impact |
|---------|--------|--------|--------|
| Execution Time | Not shown | ✓ Displayed | Performance insights |
| Error/Warning Counts | Not tracked | ✓ Tracked | Quick diagnostics |
| Console Error Display | ❌ Hidden | 🔴 Visible (red) | Better troubleshooting |
| Status Summary | None | ✓ Comprehensive | At-a-glance status |
| Preference Variables | ⚠️ Modified & unstable | ✓ Never touched | Stability & reliability |
| Verbose/Debug Streams | Captured with issues | Console-only | Simplified & stable |

## 💡 Usage Examples

### Command with Error
```powershell
Get-Item "C:\NonExistent.txt" -ErrorAction Continue
Write-Output "Continuing..."
```
**Result:**
- Regular output appears immediately
- After completion, error displays in red
- Status shows: `✗ Pipeline executed with errors | Duration: 0.03s | Errors: 1`
- **Note**: Error appears after "Continuing..." (timing trade-off)

### Multi-Stream Output
```powershell
Write-Warning "Check this"
Write-Information "Processing..." -InformationAction Continue
Get-Date
```
**Result:**
- Status shows: `Warnings: 1 | Info: 1`
- Organized sections: WARNINGS, SUCCESS, INFO

## 🔧 Technical Notes

### Error Stream Challenge
PowerShell's `-ErrorVariable` captures errors but suppresses console output. Our solution displays captured errors after execution completes, ensuring both MCP capture and console visibility.

### Stream Capture Strategy
- **Captured for MCP**: Error, Warning, Success, Information
- **Console-only**: Verbose, Debug (controlled by user preferences)
- **No preference modifications**: Commands run with your original settings

### Implementation
- Timing: `System.Diagnostics.Stopwatch` for precise measurements
- Capture: Native PowerShell variables (`-ErrorVariable`, `-WarningVariable`, `-InformationVariable`)
- Display: `Write-Host -ForegroundColor Red` for errors/exceptions

## 🙏 Acknowledgments

Thanks to our community for feedback on command visibility and stability that guided these improvements.

---

**Questions or Issues?** Please use our [GitHub Discussions](https://github.com/yotsuda/PowerShell.MCP/discussions) for support.
