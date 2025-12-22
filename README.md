# PowerShell.MCP

[![PowerShell](https://img.shields.io/badge/PowerShell-7.2+-blue.svg)](https://github.com/PowerShell/PowerShell)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-brightgreen.svg)](#prerequisites)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/v/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Security Warning
**This module provides complete PowerShell access to your system.**  
Malicious use could result in severe damage. Use responsibly and only in trusted environments.

## Overview

PowerShell.MCP is a tool that enables AI assistants (such as Claude Desktop) to execute any PowerShell commands and CLI tools within a PowerShell console. Users can also execute cmdlets/.ps1/.bat/CLI tools in the same console, allowing AI and users to work collaboratively. It operates at high speed without needing to launch a new console each time, while preserving the state of imported modules, functions and variables.

Despite its powerful capabilities, PowerShell.MCP is built with just three carefully designed tools:
- **start_powershell_console:** launching a persistent console
- **get_current_location:** retrieving the current working directory
- **invoke_expression:** executing any cmdlets/.ps1/.bat/CLI tools (chainable with pipes) in the PS console

This minimalist architecture provides maximum flexibility while maintaining simplicity.

### What Makes It Powerful

**🤝 Shared Console Experience**
- You and AI collaborate in the same PowerShell session
- Every command the AI executes appears in your console in real-time
- PowerShell cmdlets display colorful output
- You can respond to input requests from AI-executed commands directly in the console
- You can run your own commands between AI operations
- AI-executed commands are saved to history, allowing you to recall and modify parameters for re-execution
- Complete transparency - see exactly what's happening

**🔄 Persistent Session State**
- Current directory persists across all commands and interactions
- Imported modules and authenticated sessions remain active throughout the entire session
- Variables, functions, and mounted PSDrives stay available throughout the session
- No need to re-initialize or re-authenticate between commands
- True model context protocol implementation preserves your entire working state

**⚡ Instant Response, Zero Overhead**
- Commands execute immediately without launching new PowerShell processes
- Eliminates the typical 1-5 second startup delay per cmdlet
- Fast initial feedback to users with instant acknowledgment before full results
- Real-time streaming of output as commands run

**🔍 Comprehensive Output Stream Capture**
- Command output is captured and returned to the AI assistant, with PowerShell's critical streams (error, warning, success, information) completely separated
- Verbose and debug streams display naturally in the console under user control, and can be shared manually when needed
- Clear execution statistics for every command: duration, error count, warning count, and info count

**🌐 Universal Modules & CLI Integration**
- PowerShell.MCP acts as a universal bridge, instantly making any PowerShell modules or CLI tools available as fully functional MCP servers
- Access the vast ecosystem of PowerShell Gallery with over 3,000 pre-built modules, instantly integrating with everything from cloud services like [Azure](https://www.powershellgallery.com/packages/Az), [AWS](https://www.powershellgallery.com/packages/AWSPowerShell.NetCore), [Google Cloud](https://www.powershellgallery.com/packages/GoogleCloud) or [UiPath Orchestrator](https://www.powershellgallery.com/packages/UiPathOrch) to enterprise tools like [Active Directory](https://learn.microsoft.com/powershell/module/activedirectory/), [Exchange](https://www.powershellgallery.com/packages/ExchangeOnlineManagement) or [SQL Server](https://www.powershellgallery.com/packages/SqlServer)
- Uses `Get-Help` to automatically learn each cmdlet's syntax, parameters, and usage patterns for immediate productive use
- AI effectively leverages well-known command-line tools like [Git](https://git-scm.com/) or [Docker](https://www.docker.com/)
- PowerShell.MCP fundamentally transforms the MCP ecosystem by making virtually any command-line tool AI-accessible without custom development

**🔗 PowerShell Pipeline Composability**
- PowerShell naturally chains commands together, passing rich data between them
- AI assistants leverage this composability to build sophisticated workflows from simple building blocks
- Example: "Show me the top 5 largest log files" becomes `Get-ChildItem *.log | sort Length -Descending | select -First 5`
- Unlike approaches that expose each cmdlet/CLI tool as individual MCP tools, PowerShell.MCP enables AI to freely combine any commands into flexible pipelines
- You describe what you want in natural language - AI constructs the optimal pipeline automatically
- No need to understand pipeline syntax yourself - just tell AI what you need

**📝 LLM-Optimized Text File Operations**
- Traditional Get/Set-Content cmdlets frequently fail for LLMs due to line number confusion and poor performance
- To address this, PowerShell.MCP includes 5 specialized cmdlets designed specifically for AI assistants to handle text file operations reliably
- Single-pass processing architecture enables up to 100x faster performance than Get/Set-Content on large files
- 1-based line numbering eliminates array index confusion and matches compiler error messages
- Automatic encoding detection and preservation (UTF-8/16/32, Shift-JIS, line endings)
- Pattern matching with regex support and capture groups

**📚 No RAG or Context Grounding Required**
- Simply gather necessary documents and files in a folder
- Tell the AI assistant "Check this folder" in your prompt
- AI instantly accesses all the knowledge needed for the task
- Works with any content: documentation, project templates, code samples, configurations, and more
- No need for complex RAG systems or context grounding infrastructure
- Natural and intuitive way to provide domain-specific knowledge to AI

**🎯 Ready-to-Use Built-in Prompts**
- 7 specialized prompts for development, analysis, administration, and learning scenarios
- Intelligent automation with native language support and interactive guidance
- Built-in safety measures, progress tracking, and hands-on learning environments
- Accessible directly through MCP client prompts list - no command writing required

**🔐 Enterprise-Ready Security**
- Local-only communication through named pipes
- No network exposure or remote connections
- Every executed command is visible and auditable
- Compatible with strict corporate security policies

## Quick Start

### Prerequisites

| Platform | Requirements |
|----------|-------------|
| **Windows** | Windows 10/11 or Windows Server 2016+ |
| **Linux** | Ubuntu 22.04+, Debian 11+, RHEL 8+, or other distributions with GUI desktop |
| **macOS** | macOS 12 (Monterey) or later, Intel or Apple Silicon |

**All platforms require:**
- PowerShell 7.2 or higher ([installation guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell))
- Claude Desktop ([download](https://claude.ai/download)), Claude Code, or any MCP client

> **Note:** Claude Desktop is strongly recommended as other clients may not deliver optimal performance.

---

### Windows Setup

#### 1. Open PowerShell 7
Press `Win + R`, type `pwsh`, press `Enter`

#### 2. Install PowerShell.MCP
```powershell
Install-Module PowerShell.MCP
Import-Module PowerShell.MCP
```

#### 3. Get your Proxy path
```powershell
Get-MCPProxyPath
# Example: C:\Users\YourName\Documents\PowerShell\Modules\PowerShell.MCP\1.4.0\bin\win-x64\PowerShell.MCP.Proxy.exe
```

#### 4. Configure Claude Desktop
Add to `%APPDATA%\Claude\claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "C:\\Users\\YourName\\Documents\\PowerShell\\Modules\\PowerShell.MCP\\1.4.0\\bin\\win-x64\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

#### 5. Restart Claude Desktop

---

### Linux Setup

#### 1. Install PowerShell 7
```bash
# Ubuntu/Debian
sudo apt-get update
sudo apt-get install -y wget apt-transport-https software-properties-common
source /etc/os-release
wget -q https://packages.microsoft.com/config/ubuntu/$VERSION_ID/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y powershell
```

#### 2. Install PowerShell.MCP
```bash
pwsh -Command "Install-Module PowerShell.MCP -Scope CurrentUser"
```

#### 3. Get your Proxy path
```bash
pwsh -Command "Import-Module PowerShell.MCP; Get-MCPProxyPath"
# Example: /home/username/.local/share/powershell/Modules/PowerShell.MCP/1.4.0/bin/linux-x64/PowerShell.MCP.Proxy
```

#### 4. Set execute permission
```bash
chmod +x /path/to/PowerShell.MCP.Proxy
```

#### 5. Configure Claude Code
```bash
claude mcp add powershell-mcp -- /path/to/PowerShell.MCP.Proxy
```

Or edit `~/.claude.json` manually.

---

### macOS Setup

#### 1. Install PowerShell 7
```bash
# Using Homebrew
brew install powershell/tap/powershell

# Or download the official pkg installer from:
# https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-macos
```

#### 2. Install PowerShell.MCP
```bash
pwsh -Command "Install-Module PowerShell.MCP -Scope CurrentUser"
```

#### 3. Get your Proxy path
```bash
pwsh -Command "Import-Module PowerShell.MCP; Get-MCPProxyPath"
# Apple Silicon: ~/.local/share/powershell/Modules/PowerShell.MCP/1.4.0/bin/osx-arm64/PowerShell.MCP.Proxy
# Intel Mac: ~/.local/share/powershell/Modules/PowerShell.MCP/1.4.0/bin/osx-x64/PowerShell.MCP.Proxy
```

#### 4. Set execute permission
```bash
chmod +x /path/to/PowerShell.MCP.Proxy
```

#### 5. Configure Claude Desktop
Add to `~/Library/Application Support/Claude/claude_desktop_config.json`:
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "/Users/YourName/.local/share/powershell/Modules/PowerShell.MCP/1.4.0/bin/osx-arm64/PowerShell.MCP.Proxy"
    }
  }
}
```

#### 6. Restart Claude Desktop

---

## First-Time Demo
🎨 Experience PowerShell.MCP's capabilities with these engaging demonstrations:

- "Show what PowerShell.MCP can do in a colorful, dynamic, and fun demo"
- "Try out different styles of notifications using the BurntToast module"
- "Automate Notepad: type text and smoothly move the window in a circle"
- "How does it feel now that you have a tool like PowerShell.MCP?"

After trying these demos, explore the 7 built-in prompts below or ask AI to explain any command - learning by doing is the best approach.

## Built-in Prompts

PowerShell.MCP includes 7 specialized prompts accessible from your MCP client's prompts menu. Each prompt is designed for specific tasks and guides AI to provide optimal results.

<div align="center">
  <table cellspacing="0" cellpadding="0" border="0" width="100%">
    <tr>
      <td align="center" width="45%">
        <img src="images/builtin-prompt-1.png" alt="Built-in Prompts Menu" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>Built-in Prompts Menu</em>
      </td>
      <td width="3%"></td>
      <td align="center" width="45%">
        <img src="images/builtin-prompt-2.png" alt="Selecting a Prompt" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>Selecting a Prompt</em>
      </td>
    </tr>
  </table>
</div>

---

### 📋 Create Work Procedure + ⚙️ Execute Work Procedure
**Best for:** Complex, multi-step tasks you'll perform repeatedly

Work together as a powerful workflow system with automatic file management:

1. **Create Work Procedure** - AI analyzes the task and generates `work_procedure.md` as the reusable procedure and `work_progress.txt` as the execution plan.
2. **Execute Work Procedure** - AI follows and refines `work_procedure.md` during execution, and tracks and records outcomes in `work_progress.txt`.

**Key benefits:** self-refining procedures, resumable workflows, automatic progress tracking, consistent results

**Example:** "Write Get-Help markdown for multiple cmdlets through execution and verification"

<div align="center">
  <table cellspacing="0" cellpadding="0" border="0" width="100%">
    <tr>
      <td align="center" width="45%">
        <img src="images/work_procedure.png" alt="AI-generated work_procedure.md" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>AI-generated work_procedure.md</em>
      </td>
      <td width="3%"></td>
      <td align="center" width="45%">
        <img src="images/work_progress.png" alt="AI-generated work_progress.md" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>AI-generated work_progress.md</em>
      </td>
    </tr>
  </table>
</div>

---

### 📊 Analyze Content
**Best for:** Deep analysis of files, folders, or datasets

Generates comprehensive reports with insights and recommendations. Combine with **HTML Generation Guidelines for AI** for visual reports with charts.

**Examples:** "Analyze my project's log files" • "Create a report on this CSV dataset" • "Analyze this HAR file"

<div align="center">
  <table cellspacing="0" cellpadding="0" border="0" width="100%">
    <tr>
      <td align="center" width="45%">
        <img src="images/cs_analysis-1.png" alt="C# code quality analysis report" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>C# code quality analysis report</em>
      </td>
      <td width="3%"></td>
      <td align="center" width="45%">
        <img src="images/cs_analysis-2.png" alt="C# code quality analysis report (continued)" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>C# code quality analysis report (continued)</em>
      </td>
    </tr>
  </table>
</div>

---

### 🎨 HTML Generation Guidelines for AI
**Best for:** Professional HTML reports with charts and styling

Companion prompt that ensures AI generates high-quality HTML with Chart.js visualization, responsive design, and proper styling. Works with any prompt or task that needs HTML output.

**Examples:** Analyze Content + this = visual reports • Your custom prompt + this = professional HTML • Any data task + this = interactive output

<div align="center">
  <img src="images/combine_prompts.png" alt="Combined prompts generate visual HTML file" width="400"/>
  <br/>
  <em>Combined prompts generate visual HTML file</em>
</div>

---

### 📚 Learn Programming & CLI
**Best for:** Learning programming languages and command-line tools at any level

Provides personalized learning experiences with clear explanations, practical examples, and hands-on exercises. Specify your learning goals directly without worrying about experience levels.

**Examples:** "Learn Python basics for data analysis" • "Learn Git commands for version control" • "Learn PowerShell scripting"

---

### 🗣️ Foreign Language Dictation Training
**Best for:** Improving listening skills in foreign languages

Creates dictation exercises with automatic checking.

**Examples:** "English dictation at beginner level" • "Japanese conversation at the zoo"

---

### 🗺️ Create Interactive Map
**Best for:** Visualizing geographic data or locations

Generates interactive HTML maps with markers, descriptions, and optional 3D display using [PowerShell.Map](https://github.com/yotsuda/PowerShell.Map) module.

**Examples:** "Show major Roman battles in chronological order" • "Create a map of hot springs in Japan"

<div align="center">
  <table cellspacing="0" cellpadding="0" border="0" width="100%">
    <tr>
      <td align="center" width="45%">
        <img src="images/RomanWar.png" alt="Roman Battle Map" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>Roman Battle Locations</em>
      </td>
      <td width="3%"></td>
      <td align="center" width="45%">
        <img src="images/Colosseum.png" alt="Colosseum 3D View" style="max-width: 100%; height: auto;"/>
        <br/>
        <em>Colosseum 3D Visualization</em>
      </td>
    </tr>
  </table>
</div>

---

## Platform Notes

### Windows
- PSReadLine module is automatically loaded for enhanced console experience
- Full color support for PowerShell output

### Linux
- Requires a GUI desktop environment (GNOME, KDE, XFCE, etc.)
- Supported terminal emulators: gnome-terminal, konsole, xfce4-terminal, xterm, lxterminal, mate-terminal, terminator, tilix, alacritty, kitty
- PSReadLine is automatically removed (not supported on Linux)

### macOS
- Works with Terminal.app (default)
- PSReadLine is automatically removed (not supported on macOS)
- Both Apple Silicon (M1/M2/M3/M4) and Intel Macs are supported

---

## Limitations
- **AI Command Cancellation**: Commands executed by AI assistants cannot be cancelled with Ctrl+C. To cancel AI-executed commands, close the PowerShell console
- **User Command Privacy**: Commands executed by users are not visible to AI assistants
- **Verbose/Debug Streams**: Verbose and Debug output streams are not captured. Users can share this information with AI assistants via clipboard if needed
- **Standard Error (stderr)**: Standard error output from CLI programs is not displayed in the PowerShell console and is not visible to AI assistants. To capture stderr, explicitly redirect it to a variable (e.g., `$result = & command.exe 2>&1`)
- **External Command Colors**: Color output from external commands (e.g., git.exe) is lost and displayed without colors in the PowerShell console

## Disclaimer
This software is provided "AS IS" without warranty of any kind, either expressed or implied.  
The author assumes no responsibility for any damages arising from the use of this software.

## License
MIT License - see [LICENSE](LICENSE) for details.

## Author
Yoshifumi Tsuda

---
**For enterprise use, ensure compliance with your organization's security policies.**
