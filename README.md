# PowerShell.MCP

[![PowerShell Gallery](https://img.shields.io/powershellgallery/v/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)

## Security Warning
**This module provides complete PowerShell access to your system.**  
Malicious use could result in severe damage. Use responsibly and only in trusted environments.

## Overview
PowerShell.MCP enables PowerShell 7 to function as an MCP server, allowing MCP clients like Claude Desktop to access the entire PowerShell ecosystem through secure named pipe communication.

## Key Features
- **🤖 Direct AI Command Execution** - Enable AI assistants to run any cmdlets or CLI tools directly in PowerShell console
- **👥 Shared Console Experience** - AI and user share the same PowerShell console with complete transparency
- **⚡ Real-time Command Execution** - Comprehensive output capture with immediate results
- **📚 Rich Prompts List** - Ready-to-use practical scenarios for common tasks
- **🔒 Secure Local Communication** - Local-only access, no remote connections
- **🏢 Enterprise-Ready** - Designed for safe corporate environments

## System Requirements
- Windows 10/11 or Windows Server 2016+
- PowerShell 7.2.15 or higher
- PSReadLine 2.3.4 or higher

## Installation

### Prerequisites
PowerShell.MCP requires PowerShell 7 or later on Windows. See the [official installation guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows?view=powershell-7.5) for installation instructions.

### Module Installation

**From PowerShell Gallery:**
```powershell
Install-Module PowerShell.MCP
```

**Import the module:**
```powershell
Import-Module PowerShell.MCP
```

**📝 Note**: PSReadLine is automatically loaded as a required dependency - no manual import needed.

**Verify installation:**
```powershell
Get-Module PowerShell.MCP
```


## Claude Desktop Configuration

Add PowerShell.MCP to your Claude Desktop configuration:

```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "[ModuleBase]\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

**Find your module base path:**
```powershell
(Get-Module PowerShell.MCP).ModuleBase
```

**Example result:**
```
C:\Users\YourName\Documents\PowerShell\Modules\PowerShell.MCP\1.2.0
```

**Complete configuration example:**
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "C:\\Users\\YourName\\Documents\\PowerShell\\Modules\\PowerShell.MCP\\1.2.0\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

> **💡 Tip**: After updating the configuration, restart Claude Desktop to activate the PowerShell.MCP integration.


## Architecture
1. **PowerShell Module** - Named pipe-based MCP server functionality
2. **Stdio Proxy Server** - Bridge between MCP clients and named pipe MCP server
3. **MCP Client** - Connects to proxy server via stdio for operations

## Security
- **Local Communication Only** - Named pipe prevents remote access
- **Enterprise-Grade** - Works with appropriate security policies
- **Full Transparency** - Open source for complete auditability


## Examples

PowerShell.MCP provides **8 carefully curated built-in prompts** accessible directly from MCP clients through the prompts list feature. Try them out!

Here are additional useful prompt examples you can try:

### 🔧 Basic System Information
- "Tell me the current date and time"
- "Check the PowerShell version"
- "Display system environment variables"
- "Show me disk usage"

### 📊 System Monitoring and Analysis
- "Show me all processes consuming more than 100MB of memory, sorted by CPU usage"
- "Display 5 running Windows services"
- "Show me the list of directories in the current folder"
- "Display top 5 processes by memory usage"

### 🧮 Practical Calculations and Data Processing
- "Calculate the date 30 days from today"
- "Generate a 12-character random password"
- "Calculate the total file size for a specific extension"

### 📁 File and Folder Operations
- "Compare the contents of two folders and show the differences"
- "Display the top 10 recently updated files"

### 🌐 Network and Connectivity
- "Check if a specific port is open"
- "Query DNS records and display results"

### 🚀 Advanced Integration and Report Generation
- "Generate system information as an HTML report and open it in browser"
- "Create an HTML report of system errors from the last 3 days and open it in browser"
- "Visualize process usage as an HTML dashboard and display in browser"
- "Create a colorful HTML chart of disk usage analysis and display automatically"
- "Visualize network connection history as HTML timeline"

### 🏢 System Administration Tasks
- "Export a list of installed programs to CSV and open in Excel"
- "Search for specific application settings in the registry"

### 📈 Data Analysis and Reporting
- "Extract warnings and errors from Windows event logs"
- "Create a system performance overview report"
- "Explain the steps for creating the current report"
- "Display folder structure hierarchically"

### 🤖 Automation and Efficiency
- "Auto-generate reports in specified formats"

### 🎨 Creative Tasks
- "Visualize error trends in log files with graphs in HTML"
- "Generate QR codes (encode strings)"
- "Display long-running processes with colorful progress bars"
- "Visualize folder size analysis in TreeMap-style HTML"

### 👨‍💻Developer Features
- "Run syntax checks on PowerShell scripts"
- "Generate documentation from comment-based help"
- "Analyze module dependencies"
- "Calculate code metrics and evaluate quality"
- "Please review the .cs files under c:\folder"
- "Create patch files for code changes and apply with git apply"

### ⚡ PowerShell-Specific Advanced Features
- "Check the cmdlets included in imported modules"
- "Check Get-Date cmdlet parameters with Get-Help cmdlet and try several examples with those parameters"
- "Send several complex commands that you know to the PowerShell console with explanations. Do not execute them."
- "Create processing examples combining multiple cmdlets using pipelines"
- "Use PowerShell's help system to display detailed information about specific commands"
- "How do you find using PowerShell.MCP? Please share your thoughts and experiences"

### 💡 Getting Started Tips
1. **Start Simple**: Try basic system information commands first
2. **Explore Categories**: Each category demonstrates different PowerShell capabilities
3. **Combine Commands**: Chain multiple operations for complex automation
4. **Safety First**: Test commands in non-production environments
5. **Learn PowerShell**: Use built-in help system with `Get-Help`

### 🎭 Interactive Experience
PowerShell.MCP transforms your AI assistant into a powerful system administrator. Simply describe what you want to accomplish, and watch as PowerShell commands are executed with detailed explanations.

**Example Conversation:**
```
You: "I need to see which processes are using the most memory"
Assistant: I'll show you the top memory-consuming processes...
[Executes: Get-Process | Sort-Object WorkingSet -Descending | Select-Object -First 10]
```

---

**⚠ Security Reminder**: These examples demonstrate powerful system access capabilities. Always ensure you're working in appropriate environments and understand the commands being executed.


## Disclaimer
This software is provided "AS IS" without warranty of any kind, either expressed or implied.  
The author assumes no responsibility for any damages arising from the use of this software.

## License
MIT License - see [LICENSE](LICENSE) for details.

## Author
Yoshifumi Tsuda

---
**For enterprise use, ensure compliance with your organization's security policies.**



