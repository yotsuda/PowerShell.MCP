# Enhanced Text File Operations with Automatic Encoding Detection

This release focuses on improving cmdlet naming clarity and automatic encoding detection.

## ✨ What's New

### **🔄 Renamed Cmdlets for Better Clarity**

Text file cmdlets have been renamed to better reflect their functionality:

```powershell
# Pattern-based text replacement (formerly Update-TextFile)
Update-MatchInFile config.txt -Contains "old" -Replacement "new"
Update-MatchInFile code.cs -Pattern "var (\w+)" -Replacement "string $1"

# Line-based replacement (formerly Set-LinesToFile)
Update-LinesInFile file.txt 10,20 -Content $newCode
Update-LinesInFile output.txt -Content "Line 1", "Line 2"
```

**Migration Guide:**
- `Update-TextFile` → `Update-MatchInFile`
- `Set-LinesToFile` → `Update-LinesInFile`

---

## 🔧 Enhancements

### **🌐 Automatic Encoding Detection**

All text file cmdlets now automatically detect and preserve file encoding, ensuring data integrity across different character sets:

```powershell
# Automatically detects UTF-8, UTF-16, Shift-JIS, etc.
Add-LinesToFile "日本語ファイル.txt" -Content "新しい行"
Update-MatchInFile "config.ini" -Contains "設定" -Replacement "新設定"

# Encoding is preserved when modifying files
Show-TextFile "legacy_sjis.txt"  # Detects Shift-JIS
Update-LinesInFile "legacy_sjis.txt" 5 -Content "更新"  # Maintains Shift-JIS
```

**Key Benefits:**
- ✅ No manual encoding specification needed
- ✅ Preserves original file encoding automatically
- ✅ Handles multi-byte characters correctly (Japanese, Chinese, etc.)
- ✅ Supports UTF-8, UTF-16, Shift-JIS, and other encodings

### **🐛 Bug Fixes**

**UTF-8 Encoding Preservation**
- Fixed issue where UTF-8 files without BOM were incorrectly getting BOM added during file modifications
- Now correctly preserves the original BOM/no-BOM attribute of UTF-8 files
- Ensures file encoding remains unchanged when editing UTF-8 content

### **📚 Dependencies**

**Added Ude.NetStandard (Universal Charset Detector)**
- Integrated Ude.NetStandard 1.2.0 for accurate encoding detection
- Based on Mozilla's Universal Charset Detector
- Enables automatic detection of UTF-8, UTF-16, Shift-JIS, and other encodings
- License: Mozilla Public License 2.0 (MPL-2.0)
- Package: [Ude.NetStandard on NuGet](https://www.nuget.org/packages/Ude.NetStandard/)



### **Improved Positional Parameters**

**Update-LinesInFile** - LineRange is now positional for cleaner syntax:
```powershell
# Before
Update-LinesInFile file.txt -LineRange 10,20 -Content $newLines

# After  
Update-LinesInFile file.txt 10,20 -Content $newLines
```

**Important**: When replacing multiple lines, pass an array with matching number of elements:
```powershell
# Replace lines 5-7 with three new lines (preserves line count)
Update-LinesInFile file.txt 5,7 -Content @("New Line 5", "New Line 6", "New Line 7")

# Replace lines 5-7 with single line (reduces file to fewer lines)
Update-LinesInFile file.txt 5,7 -Content "Single replacement line"
```

### **Backup Feature**

All modification cmdlets support the `-Backup` parameter:
```powershell
Update-LinesInFile file.txt 5 -Content "New" -Backup
# Creates: file.txt.YYYYMMDDHHMMSS.bak
```

Backup files are named with timestamp suffix, allowing multiple backup versions.

---

## 📋 Complete Feature Set

### All Text File Cmdlets
- Show-TextFile - Display with line numbers
- Test-TextFileContains - Boolean text check
- Add-LinesToFile - Insert lines
- Update-LinesInFile - Line-based replacement (renamed)
- Update-MatchInFile - Pattern-based replacement (renamed)
- Remove-LinesFromFile - Delete line ranges

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

2. Update claude_desktop_config.json:
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "C:\\Path\\To\\PowerShell.MCP\\1.2.6\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

3. Restart Claude Desktop

📖 **Full Guide**: https://github.com/yotsuda/PowerShell.MCP#quick-start

---

## 💡 Usage Examples

```powershell
# Pattern-based replacement
Update-MatchInFile config.ini -Pattern "Port\s*=\s*(\d+)" -Replacement "Port = 9000"

# Line-based replacement (cleaner syntax)
# Replace multiple lines with array (preserves line count)
Update-LinesInFile app.config 5,7 -Content @("Line 5", "Line 6", "Line 7")

# Create/replace entire file
Update-LinesInFile notes.txt -Content "Task 1", "Task 2", "Task 3"

# Multi-byte character handling (automatic encoding detection)
Add-LinesToFile "日本語.txt" -Content "🎉 絵文字も対応"
```

---

**Full Documentation**: https://github.com/yotsuda/PowerShell.MCP

**Questions?** [GitHub Discussions](https://github.com/yotsuda/PowerShell.MCP/discussions)

---

⚠ **Security Notice**: Provides complete PowerShell access. Use in trusted environments only.
