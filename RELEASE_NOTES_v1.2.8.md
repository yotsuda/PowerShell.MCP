# Quality & Encoding Enhancement

This release fixes the v1.2.7 performance regression, adds automatic console startup, extends automatic encoding upgrade to all cmdlets, adds comprehensive encoding aliases, and establishes a complete test suite.

## ✨ What's New

### **🔧 Performance Fix**

Fixed v1.2.7 performance regression:
- File metadata detection is now **19-22x faster** through partial reading
- Restores performance to pre-v1.2.7 levels

### **⚡ Automatic PowerShell Console Start**

`get_current_location` and `invoke_expression` now automatically start the PowerShell console if not running:

- **get_current_location**: Starts console and returns location
- **invoke_expression**: Starts console but skips execution (AI verifies location first)

If console is already running, both tools execute immediately.

### **🔄 Automatic Encoding Upgrade - Now Complete**

Automatic ASCII to UTF-8 encoding upgrade, previously available in `Add-LinesToFile` and `Update-LinesToFile` (v1.2.7), is now also supported in `Update-MatchInFile`:

```powershell
# Start with ASCII file
"Server=localhost" | Set-Content config.txt -Encoding ASCII

# Replace with non-ASCII - automatically upgrades to UTF-8
Update-MatchInFile config.txt -Pattern 'localhost' -Replacement '日本サーバー'
# ℹ Info: Content contains non-ASCII characters. Upgrading encoding to UTF-8.
```

All text file cmdlets now support automatic encoding upgrade when adding non-ASCII content.

### **🌍 Comprehensive Encoding Aliases**

Added **100+ encoding aliases** for better compatibility:

**New Aliases:**
- **East Asian**: `cp932` (Shift-JIS), `cp936` (GB2312), `cp949` (EUC-KR), `cp950` (Big5)
- **BOM-explicit**: `utf8-sig`, `utf16-sig`, `utf32-sig` (Python-standard)
- **Regional**: `sjis`, `shift-jis`, `gbk`, `euc-kr`, `windows-1252`, and many more

```powershell
# All these work now
Show-TextFile file.txt -Encoding sjis
Show-TextFile file.txt -Encoding cp932
Show-TextFile file.txt -Encoding utf8-sig
```
### **📊 Comprehensive Test Coverage**

Established complete test suite:
- **268 tests** with 100% pass rate
- **87 unit tests** (C#/xUnit) - 100% method coverage
- **169 integration tests** (PowerShell/Pester)
- Covers all cmdlets, encodings, edge cases, and error handling

### **🔧 Code Quality Improvements**

Major refactoring for maintainability:
- **-35.2%** code reduction (614 → 398 lines in TextFileUtility)
- Extracted specialized helpers: `EncodingHelper`, `FileMetadataHelper`, `FileOperationHelper`
- Fixed resource leaks
- Improved error handling

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
      "command": "C:\\Path\\To\\PowerShell.MCP\\1.2.8\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

3. Restart Claude Desktop

📖 **Full Guide**: https://github.com/yotsuda/PowerShell.MCP#quick-start

---

## 💡 Usage Examples

### Extended Encoding Aliases

```powershell
# Japanese Shift-JIS - multiple aliases supported
Show-TextFile legacy.txt -Encoding sjis
Show-TextFile legacy.txt -Encoding cp932
Show-TextFile legacy.txt -Encoding shift-jis

# Python-standard BOM handling
Show-TextFile data.csv -Encoding utf8-sig

# Chinese encodings
Show-TextFile chinese.txt -Encoding gbk
Show-TextFile chinese.txt -Encoding cp936
```

### Automatic Encoding Upgrade (All Cmdlets)

```powershell
# Add-LinesToFile / Update-LinesInFile (v1.2.7~)
"Settings:" | Set-Content config.txt -Encoding ASCII
Add-LinesToFile config.txt -Content "名前: テスト"

# Update-MatchInFile (NEW in v1.2.8)
"Server=localhost" | Set-Content server.txt -Encoding ASCII
Update-MatchInFile server.txt -Pattern 'localhost' -Replacement '日本サーバー'

# All automatically upgrade to UTF-8 when needed
```
---

## 📊 What's Changed Since v1.2.7

### Fixed
- ✅ Performance regression in file metadata detection (19-22x faster)
- ✅ Resource leaks in start_powershell_console
- ✅ Various test failures and edge cases

### Added
- ✅ Automatic encoding upgrade in `Update-MatchInFile`
- ✅ 100+ encoding aliases (East Asian, BOM-explicit, regional)
- ✅ Comprehensive test suite (268 tests, 100% pass rate)
- ✅ Automatic console start in `get_current_location` and `invoke_expression`
- ✅ Always show file path in `Show-TextFile` output

### Improved
- ✅ Code maintainability (-35.2% code reduction)
- ✅ Error handling and messages
- ✅ Pipeline performance with lazy evaluation
- ✅ Resource management

### Breaking Changes
- ❌ **None** - Full backward compatibility maintained

---

## 🎯 Quality Assurance

- **268 tests** with 100% pass rate
- **87 unit tests** (C#/xUnit)
- **169 integration tests** (PowerShell/Pester)
- **~87% code coverage**
- All cmdlets, encodings, edge cases, and error handling validated

---

## 📚 Resources

- **Repository**: https://github.com/yotsuda/PowerShell.MCP
- **Questions?** [GitHub Discussions](https://github.com/yotsuda/PowerShell.MCP/discussions)
- **Report Issues**: [GitHub Issues](https://github.com/yotsuda/PowerShell.MCP/issues)

---

## ⚠ Security Notice

PowerShell.MCP provides complete PowerShell access to Claude Desktop. Use only in trusted environments.

**Best Practices:**
- Review commands before execution
- Use `-WhatIf` for testing
- Enable `-Backup` for important files

---

**Full Changelog**: https://github.com/yotsuda/PowerShell.MCP/compare/v1.2.7...v1.2.8

