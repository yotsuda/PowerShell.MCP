# Intelligent Encoding Management and Localized MCP Prompts

This release introduces intelligent encoding management that automatically upgrades ASCII files when adding non-ASCII content, along with localized MCP custom prompts for 17 languages.

## ✨ What's New

### **🔄 Automatic Encoding Upgrade**

PowerShell.MCP now intelligently manages file encodings by automatically upgrading from ASCII to UTF-8 when non-ASCII characters are added:

```powershell
# Start with an ASCII file
"Hello World" | Set-Content test.txt -Encoding ASCII

# Add Japanese text - automatically upgrades to UTF-8
Add-LinesToFile test.txt -Content "こんにちは世界"
# ℹ Info: Content contains non-ASCII characters. Upgrading encoding to UTF-8.
```

**Key Benefits:**
- Prevents data loss when adding multi-byte characters to ASCII files
- Original ASCII files remain ASCII until non-ASCII content is added
- UTF-8 files remain UTF-8 (no unnecessary conversions)

---

### **🌐 Localized MCP Custom Prompts**

MCP custom prompts are now localized for 17 languages (en-US, ja-JP, es-ES, fr-FR, de-DE, zh-CN, zh-TW, ko-KR, pt-BR, ru-RU, it-IT, nl-NL, pl-PL, tr-TR, ar-SA, hi-IN, sv-SE).

**Example:** "Software Development" → "ソフトウェア開発" (Japanese)

*Note: Prompts are displayed in your operating system's display language.*

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
      "command": "C:\\Path\\To\\PowerShell.MCP\\1.2.7\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

3. Restart Claude Desktop

📖 **Full Guide**: https://github.com/yotsuda/PowerShell.MCP#quick-start

---

## 💡 Usage Examples

### Automatic Encoding Upgrade
```powershell
# Create ASCII file and add multi-language content
"Project Notes" | Set-Content notes.txt -Encoding ASCII
Add-LinesToFile notes.txt -Content "Japanese: こんにちは世界", "Chinese: 你好世界", "Emoji: 🌍🎉"
```

### Line Range Operations
```powershell
Show-TextFile config.json -LineRange 10,50
Update-LinesInFile app.config 25,30 -Content $newConfig -Backup
Test-TextFileContains log.txt -LineRange 1000,2000 -Pattern "ERROR"
```

### Pattern-Based Updates
```powershell
Update-MatchInFile appsettings.json -Pattern '"Port":\s*\d+' -Replacement '"Port": 8080'
```

---

## 📊 What's Changed Since v1.2.6

**New Features:**
- Automatic ASCII to UTF-8 encoding upgrade
- Localized MCP custom prompts for 17 languages

**No Breaking Changes:**
- Full backward compatibility maintained
- All v1.2.6 scripts work unchanged

---

**Full Documentation**: https://github.com/yotsuda/PowerShell.MCP

**Questions?** [GitHub Discussions](https://github.com/yotsuda/PowerShell.MCP/discussions)

**Report Issues**: [GitHub Issues](https://github.com/yotsuda/PowerShell.MCP/issues)

---

⚠ **Security Notice**: Provides complete PowerShell access. Use in trusted environments only.
