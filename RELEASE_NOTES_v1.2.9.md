# Text Deletion and Architecture Improvements

This release introduces text deletion capabilities through empty replacement strings and improves the module architecture with IModuleAssemblyInitializer.

## ✨ What's New

### **🗑️ Text Deletion with Empty Replacement**

You can now delete text by using an empty string for the `-Replacement` parameter in `Update-MatchInFile`:

```powershell
# Remove port numbers from configuration
Update-MatchInFile config.txt -Contains ":8080" -Replacement ""

# Delete all digits from a string
Update-MatchInFile data.txt -Pattern '\d+' -Replacement ""

# Clean up debug statements
Update-MatchInFile app.log -Contains "DEBUG " -Replacement ""
```

**Key Benefits:**
- Simple text removal without complex regex
- Works with both `-Contains` (literal) and `-Pattern` (regex) modes
- Preserves file encoding and newlines

---

### **🏗️ Architecture Refactoring**

The module initialization has been modernized from `CmdletProvider` to `IModuleAssemblyInitializer`:

**Benefits:**
- Improved module loading performance
- Better PowerShell 7+ compatibility
- Cleaner initialization architecture
- Reduced memory footprint

*Note: This is an internal change with no impact on cmdlet usage.*

---

### **🗺️ Built-in Interactive Map Prompt**

New MCP custom prompt that automatically creates interactive maps - no PowerShell.Map knowledge required:

**Usage:**
Simply tell Claude what you want to see:
```
"Create a map of famous temples in Kyoto with PowerShell.Map module"
"Show me hot springs in Hakone area"
"Map popular ramen shops in Tokyo"
```

**That's it!** Claude automatically:
- Researches and validates locations
- Creates color-coded markers with emoji labels
- Displays the interactive map
- Offers an automated tour of all spots

**Example Themes:**
- Hot springs (♨️), Ramen shops (🍜), Tourist spots (temples⛩️, museums🏛️, parks🌳)

**Key Benefits:**
- No need to learn PowerShell.Map commands
- Automatic coordinate validation
- Beautiful emoji-enhanced maps
- Interactive tour feature
- Map opens automatically at http://localhost:8765/

---

## 💡 Usage Examples

### Text Deletion
```powershell
# Remove sensitive data
Update-MatchInFile secrets.txt -Pattern 'API_KEY=\S+' -Replacement ""

# Clean up log files
Update-MatchInFile app.log -Contains "[TRACE] " -Replacement ""

# Remove comments from code
Update-MatchInFile script.ps1 -Pattern '#.*$' -Replacement ""
```

### Combined Operations
```powershell
# Remove old entries and add new ones
Update-MatchInFile config.ini -Contains "old_setting=" -Replacement ""
Add-LinesToFile config.ini -Content "new_setting=value"
```

---

## 📊 What's Changed Since v1.2.8

**New Features:**
- Text deletion with empty `-Replacement` parameter
- Built-in interactive map generation prompt
- MCP custom prompts with HTML report generation guidelines

**Improvements:**
- Refactored to IModuleAssemblyInitializer architecture
- Updated NuGet dependencies
- Added comprehensive integration tests

**No Breaking Changes:**
- Full backward compatibility maintained
- All v1.2.8 scripts work unchanged

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
      "command": "C:\\Program Files\\PowerShell\\7\\Modules\\PowerShell.MCP\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```
*Note: Adjust PowerShell version path (7) as needed for your environment.*

3. Restart Claude Desktop

📖 **Full Guide**: https://github.com/yotsuda/PowerShell.MCP#quick-start

---

**Full Documentation**: https://github.com/yotsuda/PowerShell.MCP

**Questions?** [GitHub Discussions](https://github.com/yotsuda/PowerShell.MCP/discussions)

**Report Issues**: [GitHub Issues](https://github.com/yotsuda/PowerShell.MCP/issues)

---

⚠ **Security Notice**: Provides complete PowerShell access. Use in trusted environments only.
