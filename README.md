# PowerShell.MCP

[![PowerShell](https://img.shields.io/badge/PowerShell-7.2+-blue.svg)](https://github.com/PowerShell/PowerShell)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/v/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)
[![PowerShell Gallery](https://img.shields.io/powershellgallery/dt/PowerShell.MCP)](https://www.powershellgallery.com/packages/PowerShell.MCP)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Security Warning
**This module provides complete PowerShell access to your system.**  
Malicious use could result in severe damage. Use responsibly and only in trusted environments.

## Overview

PowerShell.MCP is a tool that enables AI assistants (such as Claude Desktop) to execute any cmdlets/CLI tools within a PowerShell console. Users can also execute cmdlets/CLI tools in the same console, allowing AI and users to work collaboratively. It operates at high speed without needing to launch a new console each time, while preserving the state of imported modules, functions and variables.

Despite its powerful capabilities, PowerShell.MCP is built with just three carefully designed tools:
- **start_powershell_console:** launching a persistent console
- **get_current_location:** retrieving the current working directory
- **invoke_expression:** executing any cmdlets/CLI tools (chainable with pipes) in the PS console

This minimalist architecture provides maximum flexibility while maintaining simplicity.

### What Makes It Powerful

**🤝 Shared Console Experience**
- You and AI work together in the same PowerShell session
- Every command the AI executes appears in your console in real-time
- PowerShell cmdlets display colorful output
- You can respond to input requests from AI-executed commands directly in the console
- You can run your own commands between AI operations
- AI-executed commands are saved to history, allowing you to recall and modify parameters for re-execution
- Complete transparency - see exactly what's happening

**🔄 Living Workspace That Remembers Everything**
- Current directory persists across all commands and interactions
- Imported modules and authenticated sessions remain active throughout the entire session
- Variables, functions, and mounted drives stay available throughout the session
- No need to re-initialize or re-authenticate between commands
- True model context protocol implementation preserves your entire working state

**⚡ Instant Response, Zero Overhead**
- Commands execute immediately without launching new PowerShell processes
- Eliminates the typical 1-5 second startup delay per cmdlet
- Fast initial feedback to users with instant acknowledgment before full results
- Real-time streaming of output as commands run
- Complex multi-step operations flow naturally

**🔍 Comprehensive Output Stream Capture**
- Command output is captured and returned to the AI assistant, with PowerShell's critical streams (error, warning, success, information) completely separated
- Verbose and debug streams display naturally in the console under user control, and can be shared manually when needed
- Clear execution statistics for every command: duration, error count, warning count, and info count

**📝 LLM-Optimized Text File Operations**
- Traditional Get/Set-Content cmdlets frequently fail for LLMs due to line number confusion and poor performance
- To address this, PowerShell.MCP includes 6 specialized cmdlets designed specifically for AI assistants to handle text file operations reliably
- Single-pass processing architecture enables up to 100x faster performance than Get/Set-Content on large files
- 1-based line numbering eliminates array index confusion and matches compiler error messages
- Automatic encoding detection and preservation (UTF-8/16/32, Shift-JIS, line endings)
- Pattern matching with regex support and capture groups

**🔗 PowerShell Pipeline Composability**
- PowerShell naturally chains commands together, passing rich data between them
- AI assistants leverage this composability to build sophisticated workflows from simple building blocks
- Example: "Show me the top 5 largest log files" becomes `Get-ChildItem *.log | sort Length -Descending | select -First 5`
- Unlike approaches that expose each cmdlet/CLI tool as individual MCP tools, PowerShell.MCP enables AI to freely combine any commands into flexible pipelines
- You describe what you want in natural language - AI constructs the optimal pipeline automatically
- No need to understand pipeline syntax yourself - just tell AI what you need

**🌐 Universal Modules & CLI Tools Integration**
- PowerShell.MCP acts as a universal bridge, instantly making any PowerShell modules or CLI tools available as fully functional MCP servers
- Access the vast ecosystem of PowerShell Gallery with over 3,000 pre-built modules, instantly integrating with everything from cloud services like [Azure](https://www.powershellgallery.com/packages/Az), [AWS](https://www.powershellgallery.com/packages/AWSPowerShell.NetCore), [Google Cloud](https://www.powershellgallery.com/packages/GoogleCloud) and [UiPath Orchestrator](https://www.powershellgallery.com/packages/UiPathOrch) to enterprise tools like [Active Directory](https://learn.microsoft.com/powershell/module/activedirectory/), [Exchange](https://www.powershellgallery.com/packages/ExchangeOnlineManagement) and [SQL Server](https://www.powershellgallery.com/packages/SqlServer)
- Uses `Get-Help` to automatically learn each cmdlet's syntax, parameters, and usage patterns for immediate productive use
- AI effectively leverages well-known command-line tools like [Git](https://git-scm.com/) or [Docker](https://www.docker.com/)
- PowerShell.MCP fundamentally transforms the MCP ecosystem by making virtually any command-line tool AI-accessible without custom development

**📚 No RAG or Context Grounding Required**
- Simply gather necessary documents and files in a folder
- Tell the AI assistant "Check this folder" in your prompt
- AI instantly accesses all the knowledge needed for the task
- Works with any content: documentation, project templates, code samples, configurations, and more
- No need for complex RAG systems or context grounding infrastructure
- Natural and intuitive way to provide domain-specific knowledge to AI

**🎯 Ready-to-Use Built-in Prompts**
- 9 specialized prompts for development, analysis, administration, and learning scenarios
- Intelligent automation with native language support and interactive guidance
- Built-in safety measures, progress tracking, and hands-on learning environments
- Accessible directly through MCP client prompts list - no command writing required

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

# PowerShell pipeline example - objects flow through each stage
PS C:\MyProject\WebApp> Get-ChildItem *.log | 
    where Length -gt 1MB | 
    sort LastWriteTime -Descending | 
    select Name, Length, LastWriteTime
```

Transform natural language requests into PowerShell automation - from simple file operations to complex system administration tasks, all while maintaining complete visibility and control.

## Quick Start

### Prerequisites
- Windows 10/11 or Windows Server 2016+
- Claude Desktop ([download](https://claude.ai/download)) or any MCP clients
  - **Note: Claude Desktop is strongly recommended** as other clients may not deliver optimal performance
- PowerShell 7.2 or higher ([installation guide](https://learn.microsoft.com/powershell/scripting/install/installing-powershell-on-windows?view=powershell-7.5))
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
# Example output: C:\Users\YourName\Documents\PowerShell\Modules\PowerShell.MCP\1.3.1
```

### 4. Configure Claude Desktop
Add to your Claude Desktop configuration:
```json
{
  "mcpServers": {
    "PowerShell": {
      "command": "C:\\Users\\YourName\\Documents\\PowerShell\\Modules\\PowerShell.MCP\\1.3.1\\bin\\PowerShell.MCP.Proxy.exe"
    }
  }
}
```

### 5. Restart Claude Desktop and test
- Restart Claude Desktop to activate the integration
- See the **Examples** section below for your first demo!

## Limitations
- **AI Command Cancellation**: Commands executed by AI assistants cannot be cancelled with Ctrl+C. To cancel AI-executed commands, close the PowerShell console
- **User Command Privacy**: Commands executed by users are not visible to AI assistants
- **Verbose/Debug Streams**: Verbose and Debug output streams are not captured. Users can share this information with AI assistants via clipboard if needed
- **External Command Colors**: Color output from external commands (e.g., git.exe) is lost and displayed without colors in the PowerShell console

## Examples

### 🎨 First-Time Demo
Experience PowerShell.MCP's capabilities with these engaging demonstrations:

- "Show what PowerShell.MCP can do in a colorful, dynamic, and fun demo"
- "Try out different styles of notifications using the BurntToast module"
- "Automate Notepad: type text and smoothly move the window in a circle"
- "Tell me how to use Git in PowerShell"
- "How does it feel now that you have a tool like PowerShell.MCP?"

After trying these demos, explore the 8 built-in prompts in your MCP client's prompts list, or ask AI to explain any command - learning by doing is the best approach.

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
- "Find and fix TODOs in my code automatically"
- "Check for outdated npm packages"

### ⚡ PowerShell Learning
- "Explain Get-Process cmdlet with examples"
- "Show me how to use the pipeline effectively"
- "Demonstrate advanced filtering with Where-Object"
- "Create a custom PowerShell function"

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
