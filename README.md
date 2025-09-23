# PowerShell.MCP

[![PowerShell Gallery](https://img.shields.io/powershellgallery/v/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)

## Security Warning
**This module provides complete PowerShell access to your system.**  
Malicious use could result in severe damage. Use responsibly and only in trusted environments.

## Overview

PowerShell.MCP is a tool that enables AI assistants (such as Claude Desktop) to execute any cmdlets/CLI tools within a PowerShell console. Users can also execute cmdlets/CLI tools in the same console, allowing AI and users to work collaboratively. It operates at high speed without needing to launch a new console each time, while preserving the state of imported modules, functions and variables.

### What Makes It Powerful

**🤝 Shared Console Experience**
- You and AI work together in the same PowerShell session
- Every command the AI executes appears in your console in real-time
- Run your own commands between AI operations
- Complete transparency - see exactly what's happening

**🔄 Living Workspace That Remembers Everything**
- Current directory persists across all commands and interactions
- Imported modules and authenticated sessions remain active throughout the entire session
- Variables, functions, and mounted drives stay available throughout the session
- No need to re-initialize or re-authenticate between commands
- True model context protocol implementation preserves your entire working state

**⚡ Instant Response, Zero Overhead**
- Commands execute immediately without launching new PowerShell processes
- Eliminates the typical 1-5 second startup delay per command
- Fast initial feedback to users with instant acknowledgment before full results
- Real-time streaming of output as commands run
- Complex multi-step operations flow naturally

**🔐 Enterprise-Ready Security**
- Local-only communication through named pipes
- No network exposure or remote connections
- Every executed command is visible and auditable
- Compatible with strict corporate security policies

### See It In Action
```text
# AI navigates to your project
PS C:\> cd C:\MyProject\WebApp

# All subsequent commands inherit this context automatically
PS C:\MyProject\WebApp> dir *.js               # Lists JS files
PS C:\MyProject\WebApp> git status             # Shows git status  
PS C:\MyProject\WebApp> dotnet build           # Builds the project
PS C:\MyProject\WebApp> $env:NODE_ENV = "dev"  # Sets variable
PS C:\MyProject\WebApp> ./scripts/deploy.ps1   # Runs scripts with env vars intact
```

Transform natural language requests into PowerShell automation - from simple file operations to complex system administration tasks, all while maintaining complete visibility and control.

## Quick Start

### Prerequisites
- Windows 10/11 or Windows Server 2016+
- Claude Desktop ([download](https://claude.ai/download)) or any MCP clients
- PowerShell 7.2.15 or higher ([installation guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows?view=powershell-7.5))
- PSReadLine 2.3.4 or higher ([auto-installed](https://www.powershellgallery.com/packages/PSReadLine))

### 1. Open PowerShell 7
- Press `Win + R`, type `pwsh`, press `Enter`
- Verify PowerShell 7.x is running (not Windows PowerShell 5.x)

### 2. Install PowerShell.MCP
```powershell
Install-Module PowerShell.MCP
Import-Module PowerShell.MCP
```

### 3. Get your module path
```powershell
(Get-Module PowerShell.MCP).ModuleBase
# Example output: C:\Users\YourName\Documents\PowerShell\Modules\PowerShell.MCP\1.2.0
```

### 4. Configure Claude Desktop
Add to your Claude Desktop configuration:
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "C:\\Users\\YourName\\Documents\\PowerShell\\Modules\\PowerShell.MCP\\1.2.0\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

### 5. Restart Claude Desktop and test
- Restart Claude Desktop to activate the integration
- Try: "Show me the PowerShell version"

**💡 First-time tip**: Start with simple commands to familiarize yourself with the shared console experience.

## Limitations
- **AI Command Cancellation**: Commands executed by AI assistants cannot be cancelled with Ctrl+C. To cancel AI-executed commands, close the PowerShell console
- **User Command Privacy**: Commands executed by users are not visible to AI assistants

## Examples

PowerShell.MCP provides **8 carefully curated built-in prompts** accessible directly from MCP clients through the prompts list feature. Try them out!

Here are additional useful prompt examples you can try:

### 🔧 Basic System Information
- "Tell me the current date and time"
- "Check the PowerShell version"
- "Display system environment variables"
- "Show me disk usage"

### 📊 System Monitoring and Analysis
- "Show me all processes consuming more than 100MB of memory"
- "Display top 5 processes by CPU usage"
- "Show me running Windows services"
- "List recently modified files in current directory"

### 🧮 Practical Calculations and Data Processing
- "Calculate the date 30 days from today"
- "Generate a 12-character random password"
- "Calculate total size of all PDF files"

### 📁 File and Folder Operations
- "Compare two folders and show differences"
- "Find duplicate files in a directory"
- "Create a backup of configuration files"

### 🌐 Network and Connectivity
- "Check if port 443 is open on a server"
- "Display network adapter information"
- "Test connectivity to multiple servers"

### 🚀 Advanced Integration and Reporting
- "Generate an HTML system report and open in browser"
- "Create a dashboard of system performance metrics"
- "Export event log errors to CSV"
- "Visualize disk usage as an interactive chart"

### 👨‍💻 Developer Features
- "Review git changes and suggest a commit message"
- "Analyze code metrics in the current project"
- "Find TODO comments in source files"
- "Check for outdated npm packages"

### ⚡ PowerShell Learning
- "Explain Get-Process cmdlet with examples"
- "Show me how to use the pipeline effectively"
- "Demonstrate advanced filtering with Where-Object"
- "Create a custom PowerShell function"

### 💡 Getting Started Tips
1. **Start Simple**: Try basic system information commands first
2. **Explore Built-in Prompts**: Use the 8 curated prompts in your MCP client
3. **Build Complexity**: Progress from single commands to pipelines
4. **Learn by Doing**: Ask AI to explain what each command does
5. **Stay Safe**: Test in non-production environments first

### 🎭 Interactive Experience
PowerShell.MCP transforms your AI assistant into a powerful system administrator. Simply describe what you want to accomplish in natural language:

**Example Conversation:**
```
You: "I need to see which processes are using the most memory"
Assistant: I'll show you the top memory-consuming processes...
[Executes: Get-Process | Sort-Object WorkingSet -Descending | Select-Object -First 10]
[Results appear in real-time with explanation]
```

## Disclaimer
This software is provided "AS IS" without warranty of any kind, either expressed or implied.  
The author assumes no responsibility for any damages arising from the use of this software.

## License
MIT License - see [LICENSE](LICENSE) for details.

## Author
Yoshifumi Tsuda

---
**For enterprise use, ensure compliance with your organization's security policies.**
