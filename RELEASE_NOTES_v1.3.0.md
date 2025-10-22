# Context-Aware Search with Visual Highlights

This release transforms `Show-TextFile` into a powerful context-aware search tool with visual highlighting and flexible range specification.

## ✨ What's New

### **🔍 Smart Context Display**

When searching with `-Contains` or `-Pattern`, matching lines now automatically display with **3 lines of context** before and after:

```powershell
# See TODO items with surrounding code
Show-TextFile Program.cs -Contains "TODO"

# Find errors with context
Show-TextFile error.log -Pattern "ERROR|FATAL"
```

**Key Benefits:**
- Understand code context without opening editors
- Multiple nearby matches automatically merged into single blocks
- Perfect for AI code analysis and human code review

---

### **✨ Visual Search Highlighting**

Matched text is now **highlighted with reverse video** using ANSI escape sequences for instant visibility in modern terminals.

```powershell
# Highlights every occurrence
Show-TextFile source.cs -Contains "TODO"
Show-TextFile app.log -Pattern "ERROR|FATAL"
```

---

### **📏 Flexible End-of-File Range**

New syntax for reading to the end of large files without counting lines:

```powershell
# From line 100 to end of file
Show-TextFile log.txt -LineRange 100,-1

# Skip first 10000 lines, show rest with errors
Show-TextFile system.log -LineRange 10000,-1 -Pattern "ERROR"
```

**Why this matters:**
- **Log files**: Skip startup messages, read recent entries
- **Large files**: Tail without loading everything first
- **Growing files**: Same command works regardless of file size

---

## 📊 What's Changed Since v1.2.9

**Major Enhancements:**
- Context display (3 lines before/after) for search results
- ANSI reverse video highlighting for matched text
- Negative LineRange values for "to end of file"
- Smart range merging for nearby matches

**Testing:**
- 280 lines of advanced feature tests
- 165 lines of edge case tests
- Integration test coverage expanded

**No Breaking Changes:**
- Full backward compatibility maintained
- All v1.2.9 scripts work unchanged

---

## 🔄 Installation & Upgrade

```powershell
# New installation
Install-Module PowerShell.MCP -Force

# Upgrade existing
Update-Module PowerShell.MCP
```

### Update MCP Configuration

1. Get new module path:
```powershell
(Get-Module PowerShell.MCP).ModuleBase
```

2. Update claude_desktop_config.json with the path from step 1:
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "C:\\Users\\YourName\\Documents\\PowerShell\\Modules\\PowerShell.MCP\\1.3.0\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

*Note: Adjust path as needed for your environment.*

3. Restart Claude Desktop

📖 **Full Guide**: https://github.com/yotsuda/PowerShell.MCP#quick-start

---

**Full Documentation**: https://github.com/yotsuda/PowerShell.MCP

**Questions?** [GitHub Discussions](https://github.com/yotsuda/PowerShell.MCP/discussions)

**Report Issues**: [GitHub Issues](https://github.com/yotsuda/PowerShell.MCP/issues)

---

⚠ **Security Notice**: Provides complete PowerShell access. Use in trusted environments only.
