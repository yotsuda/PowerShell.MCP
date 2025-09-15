using ModelContextProtocol.Server;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace PowerShell.MCP.Proxy.Prompts;

[McpServerPromptType]
public static class PowerShellPrompts
{
    [McpServerPrompt]
    [Description("Demonstrate PowerShell commands with hands-on practice and interactive learning")]
    public static ChatMessage TryPowershell(
        [Description("file operations, process management, text processing, system information, network tools")]
    string? topic = null)
    {
        var basePrompt = string.IsNullOrEmpty(topic)
            ? "Demonstrate PowerShell's essential features with hands-on practice."
            : $"Demonstrate PowerShell features related to '{topic}' with hands-on practice.";

        var prompt = $@"{basePrompt}

First, assess the user's PowerShell experience level before starting.

Provide interactive demonstrations including:
- Live command execution with real-time results
- Step-by-step explanations of what each command does
- Common use cases and practical applications
- Troubleshooting tips and error handling
- Best practices and security considerations

Execute commands safely using PowerShell.MCP and show actual outputs. For potentially risky operations, use -WhatIf and explain the effects before execution.

Encourage hands-on learning by suggesting variations for users to try themselves.";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Analyze folder contents and provide development-aware insights with actionable recommendations")]
    public static ChatMessage AnalyzeFolder(
        [Description("Path to the folder to analyze")]
    string path)
    {
        var prompt = $@"Analyze the folder '{path}' using PowerShell.MCP and provide comprehensive insights with actionable recommendations.

First, confirm the analysis scope with the user before starting.

Perform analysis including:
- Basic folder information and file distribution
- Project type detection (Git, .NET, Node.js, Python, etc.)
- For development projects: Git status, code review suggestions, commit message recommendations
- Security and cleanup opportunities
- Performance optimization suggestions

Use -WhatIf for safety and confirm with user before making changes. For sensitive operations, provide commands for manual execution.

Create a detailed report with prioritized action items and present in user's preferred format (HTML or Markdown).";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Import a PowerShell module and explore its usage with comprehensive guidance")]
    public static ChatMessage ImportPowershellModule(
        [Description("Name of the PowerShell module to import")]
        string module_name)
    {
        var explorationSteps = @"1. Check if the module is already installed and available
2. If it has not been installed, install the module with user approval
3. Import the module
4. List all available cmdlets, functions, and aliases
5. Display detailed help for key cmdlets
6. Determine what the user wants to achieve with this module and provide assistance";
        
        var prompt = $@"Please import PowerShell module ''{module_name}'' and explore its usage:

{explorationSteps}

Proceed with detailed explanations at each step to make it beginner-friendly. Include troubleshooting guidance for potential errors such as module not found, version conflicts, or permission issues.

Focus on practical applications and how this module can improve daily PowerShell workflows.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Execute and automate system administration tasks with PowerShell")]
    public static ChatMessage SystemAdministration(
        [Description("log analysis, backup, user management, service management, network configuration, performance monitoring, security hardening")]
    string task_type)
    {
        var prompt = $@"Execute and automate {task_type} tasks using PowerShell.MCP.

First, confirm specific requirements with the user and create a work procedure document before starting. If there are useful PowerShell modules for the task execution, suggest installation to the user.

Execute operations according to the following requirements:
- Verify required permissions and execute setup
- Safe execution with error handling and logging
- Operations following security best practices
- Verification and confirmation of operation results
- Automatic troubleshooting when problems occur

When executing cmdlets, use -WhatIf and proceed with user confirmation. For critical operations, only send commands to the console and have the user execute them manually.

After completing the work, create a work report and show it to the user. Confirm the work report format with the user. Generally, HTML or Markdown format should be preferred.";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create learning environment and guide step-by-step programming language learning with hands-on practice")]
    public static ChatMessage LearnProgramming(
        [Description("Python, C#, JavaScript, Java, Go, Rust, etc.")]
    string language)
    {
        var prompt = $@"Create a complete learning environment for {language} programming and guide step-by-step learning with emphasis on hands-on practice.

First, confirm the user's experience level and learning goals before starting.

Set up the learning environment:
- Create a dedicated work folder for the project
- Initialize a minimal starter project with the smallest possible program file
- Check if Git is installed, suggest installation if needed
- Create a Git repository and make initial commit

Guide learning progression:
- Start with the simplest possible working program (Hello World)
- Create files using PowerShell.MCP and show their contents to the user
- **CRITICAL: After creating each file, open it in an editor (notepad/code) for the user to practice hands-on editing**
- Encourage the user to modify the code themselves before proceeding
- Wait for user confirmation that they have completed the editing exercise
- Run the modified program and explain the results
- Add features incrementally, step by step
- Explain each addition and its purpose before and after user practice
- Commit changes at each meaningful step with clear commit messages
- Provide exercises and variations for practice

Learning methodology:
- Always create → open in editor → let user edit → run → explain → commit
- Make the user an active participant, not a passive observer
- Provide clear, specific editing instructions
- Encourage experimentation and learning from mistakes
- Use the native language if user preference is indicated

Use PowerShell.MCP to execute all setup commands and file operations.
Maintain a learning progress report artifact tracking completed topics and next steps.

Remember: The goal is hands-on learning, not just demonstration. Each step should involve user interaction and practice.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create work procedure documents and progress tracking tables")]
    public static ChatMessage CreateWorkProcedure(
        [Description("Brief description of the work or project")]
    string work_description)
    {
        var prompt = $@"Create comprehensive work procedure documentation and progress tracking for: {work_description}

First, confirm detailed requirements and scope with the user before starting.

Create documentation including:
- Detailed work procedure document with step-by-step instructions
- Progress tracking table with one row per task item and status columns
- Use single-row-per-task format where each row is updated to reflect current status

Progress tracking requirements:
- One task per row with clear status indicators
- Update individual rows as progress is made (not append-style)
- Provide at-a-glance overview of overall progress
- Include columns for: Task, Status, Assigned, Due Date, Notes

Maintain documents using PowerShell.MCP:
- Create structured documents in user's preferred format
- Update progress table frequently as work advances
- Ensure documents remain organized and accessible

Present both documents to the user and establish a regular update schedule.";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Update and organize work procedure documents based on new learnings")]
    public static ChatMessage UpdateWorkProcedure(
        [Description("Path to the existing work procedure document")]
    string procedure_path,
        [Description("Description of new learnings or changes to incorporate")]
    string? learnings = null)
    {
        var prompt = $@"Update and reorganize the work procedure document at '{procedure_path}' with new learnings and improvements.

First, analyze the existing document structure and confirm update requirements with the user.

Update process:
- Review current document content and structure
- Incorporate new learnings logically without creating redundancy
- Reorganize content to maintain clarity and flow
- Eliminate duplications and contradictions
- Ensure important information remains prominent and accessible

For new learnings provided: {(string.IsNullOrEmpty(learnings) ? "Identify learnings through user consultation" : learnings)}

Maintain document quality:
- Keep structure logical and well-organized
- Preserve important existing content
- Integrate new information seamlessly
- Update related sections consistently

Use PowerShell.MCP to safely update the document with backup creation. Present the updated document to the user for review and approval.";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create comprehensive work reports with summary and detailed findings")]
    public static ChatMessage CreateWorkReport(
        [Description("Brief description of the completed work")]
    string? work_description = null)
    {
        var prompt = $@"Create a comprehensive work report for the completed work: {work_description}

First, confirm the report scope, target audience, and preferred format with the user.

Create a professional report including:
- Executive summary of work completed
- Detailed findings and results
- Methodology and processes used
- Challenges encountered and solutions applied
- Recommendations and next steps
- Supporting data and evidence

Report format options:
- HTML (opens in browser for immediate viewing)
- Markdown (for documentation systems)
- Word document (for formal business reports)
- PDF (for distribution and archival)

Use PowerShell.MCP to:
- Generate the report in the chosen format
- Include relevant screenshots, logs, or data
- Create professional formatting and structure
- Automatically open the completed report for user review

Present the finished report to the user by opening it in the appropriate application (browser for HTML, Word for .doc, etc.).";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create hands-on learning environment for command-line tools")]
    public static ChatMessage LearnCommandLineTool(
        [Description("Git, PowerShell, Bash, Docker, kubectl, etc.")]
    string tool,
        [Description("Beginner, Intermediate, Advanced")]
    string level = "Beginner")
    {
        var prompt = $@"Create hands-on {tool} learning environment for {level} level.

Steps:
1. Confirm user experience level and goals
2. Verify {tool} installation and setup practice folder
3. Follow: Explain → Demonstrate → User Practice → Verify → Next

Key principles:
- Start with simplest commands
- Always open files/terminals for user to practice
- Wait for user confirmation before proceeding
- Use PowerShell.MCP for all demonstrations
- Create progress tracking artifact
- Focus on real-world workflows, not just commands

Make the user actively practice each step.";

        return new ChatMessage(ChatRole.User, prompt);
    }
}
