using ModelContextProtocol.Server;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace PowerShell.MCP.Proxy.Prompts;

[McpServerPromptType]
public static class PowerShellPrompts
{
    [McpServerPrompt]
    [Description("Comprehensive software development support with coding, testing, and deployment assistance")]
    public static ChatMessage SoftwareDevelopment(
        [Description("Programming language or technology")]
        string? technology = null,
        [Description("Development task type")]
        string? task_type = null,
        [Description("Path to the project folder")]
        string? project_path = null)
    {
        var technologyFocus = !string.IsNullOrEmpty(technology)
            ? $"Focus on {technology} development"
            : "General software development support";

        var taskFocus = !string.IsNullOrEmpty(task_type)
            ? $"\nSpecific task: {task_type}"
            : "";

        var projectSection = !string.IsNullOrEmpty(project_path)
            ? $"\nProject location: {project_path}"
            : "";

        var prompt = $@"{technologyFocus} with comprehensive development lifecycle support.{taskFocus}{projectSection}

First, analyze project context (existing structure for established projects, requirements for new projects).

Provide development support across:
- Coding: creation, review, optimization, refactoring
- Testing: strategy, test creation, quality assurance
- Debugging: error analysis, performance optimization
- Build/Deploy: automation, CI/CD, environment setup
- Documentation: technical docs, API documentation

Use PowerShell.MCP for project operations:
- Navigate to project directory (assess existing or create new structure)
- File operations respecting existing conventions
- Build automation and testing execution
- Version control and deployment tasks

Execute safely with proper version control and follow existing project conventions when applicable.
For new projects, create appropriate directory structure and initialize version control.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Analyze content and provide insights with actionable recommendations")]
    public static ChatMessage AnalyzeContent(
        [Description("Path to content for analysis")]
        string content_path)
    {
        var prompt = $@"Analyze the content at '{content_path}' using PowerShell.MCP and provide comprehensive insights with actionable recommendations.

First, confirm the analysis scope with the user before starting.

Perform analysis including:
- Basic content structure and file distribution
- Project type detection (Git, .NET, Node.js, Python, etc.)
- For development projects: Git status, code review suggestions, commit message recommendations
- Security and cleanup opportunities
- Performance optimization suggestions

Use -WhatIf for safety and confirm with user before making changes. For sensitive operations, provide commands for manual execution.
If the specified path is not found, confirm the correct path with user.

Create a detailed report file with prioritized action items and present in user's preferred format (.html or .md).";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Execute and automate system administration tasks with PowerShell")]
    public static ChatMessage SystemAdministration(
        [Description("System administration task type")]
        string task_type,
        [Description("PowerShell module for task execution (optional)")]
        string? required_module = null)
    {
        var moduleSection = !string.IsNullOrEmpty(required_module)
            ? $"\nRequired PowerShell module: {required_module} - will be installed and imported if needed."
            : "";

        var prompt = $@"Execute and automate {task_type} tasks using PowerShell.MCP.{moduleSection}

First, confirm specific requirements with the user and create a work procedure document before starting.

Execute operations according to the following requirements:
- Verify required permissions and execute setup
- If a specific PowerShell module is required, ensure it is installed and imported before proceeding
- Safe execution with error handling and logging
- Operations following security best practices
- Verification and confirmation of operation results
- Automatic troubleshooting when problems occur

When executing cmdlets, use -WhatIf and proceed with user confirmation. For critical operations, only send commands to the console and have the user execute them manually.
If module installation fails, provide manual installation instructions to the user.

After completing the work, create a work report and show it to the user. Confirm the work report format with the user. Generally, HTML or Markdown format should be preferred.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create learning environment and guide step-by-step programming language learning with hands-on practice")]
    public static ChatMessage LearnProgrammingLanguage(
        [Description("Python, C#, JavaScript, Java, Go, Rust, etc.")]
        string programming_language)
    {
        var prompt = $@"Create a complete learning environment for {programming_language} programming and guide step-by-step learning with emphasis on hands-on practice.

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
If development tools are not installed, guide the user through installation process.
Maintain a learning progress report artifact tracking completed topics and next steps.

Remember: The goal is hands-on learning, not just demonstration. Each step should involve user interaction and practice.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create comprehensive work procedure documentation and progress tracking system")]
    public static ChatMessage CreateWorkProcedure(
        [Description("Description of the work or project")]
        string work_description,
        [Description("Path for procedure file (.md recommended)")]
        string? work_procedure_path = null,
        [Description("Path for progress file (.md recommended)")]
        string? work_progress_path = null)
    {
        var prompt = $@"Create comprehensive work procedure documentation and progress tracking for: {work_description}

STEP 1: REQUIREMENT CONFIRMATION
First, confirm detailed requirements and scope with the user before starting creation.

STEP 2: WORK PROCEDURE DOCUMENT (.md)
Create a detailed work procedure document including:
- Step-by-step instructions with clear, actionable tasks
- Prerequisites and required resources
- Success criteria for each step
- Risk mitigation strategies
- Quality checkpoints

STEP 3: WORK PROGRESS DOCUMENT (.md)
Create a progress tracking document with the following requirements:
- **Single-row-per-task format**: Each task occupies one row that gets updated (not appended)
- **Current status focus**: Show current progress and next actions, not historical records
- **Self-resumable format**: Enable you to resume work by reading this document alone
- **Required columns**: Task, Status, Due Date, Notes
- **Status indicators**: Use clear visual indicators (✅ Complete, 🟡 In Progress, ⏳ Pending, ❌ Blocked)
- **At-a-glance overview**: Provide immediate understanding of overall project health

DOCUMENT MANAGEMENT:
- Use PowerShell.MCP for structured document creation and maintenance
- Update progress table frequently as work advances
- Ensure documents remain organized and accessible
- Create parent directories for specified .md files if they do not exist

DELIVERABLES:
1. Work procedure document ({work_procedure_path ?? "work_procedure.md"})
2. Progress tracking document ({work_progress_path ?? "work_progress.md"})
3. Established update schedule and maintenance plan

Present both documents to the user and confirm the regular update schedule.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Execute/Resume work procedure and track progress by analyzing documentation")]
    public static ChatMessage ExecuteWorkProcedure(
        [Description("Path to the procedure file (.md recommended)")]
        string? work_procedure_path = null,
        [Description("Path to the progress file (.md recommended)")]
        string? work_progress_path = null,
        [Description("Context or focus area for work")]
        string? work_context = null)
    {
        var contextSection = !string.IsNullOrEmpty(work_context)
            ? $"\nWORK CONTEXT: {work_context}"
            : "";

        var prompt = $@"Resume and execute remaining work by analyzing documentation, performing tasks, and updating progress.{contextSection}

EXECUTE WORK FLOW:
1. Read procedure document ({work_procedure_path ?? "work_procedure.md"}) and progress tracking ({work_progress_path ?? "work_progress.md"})
2. Identify next actionable tasks (⏳ Pending, 🟡 In Progress, or ready to start)
3. Execute the identified tasks following the established procedure
4. Update progress tracking document in real-time as tasks are completed
5. Mark completed tasks with ✅ status and add completion notes

EXECUTION REQUIREMENTS:
- Actually perform the work, don't just plan it
- Follow the step-by-step procedure documented
- Update task statuses immediately upon completion
- Add detailed notes about work performed and any discoveries
- If blocked, mark as ❌ and document the blocker reason
- Maintain the single-row-per-task format in progress tracking

OUTPUT ACTIONS:
1. Execute the next available tasks from the procedure
2. Update {work_progress_path ?? "work_progress.md"} with current status
3. Report what was accomplished and next steps
4. Highlight any blockers or issues encountered

If documents are not found, offer to create basic procedure templates at specified locations.
Begin by reading the documents, then immediately start executing the next available work items.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Refine work procedure with new learnings and improvements")]
    public static ChatMessage RefineWorkProcedure(
        [Description("Path to the existing work procedure document")]
        string? work_procedure_path = null,
        [Description("Description of new learnings or changes to incorporate")]
        string? new_learnings = null)
    {
        var prompt = $@"Update and reorganize the work procedure document at '{work_procedure_path}' with new learnings and improvements.

First, analyze the existing document structure and confirm update requirements with the user.

Update process:
- Review current document content and structure
- Incorporate new learnings logically without creating redundancy
- Reorganize content to maintain clarity and flow
- Eliminate duplications and contradictions
- Ensure important information remains prominent and accessible

For new learnings provided: {(string.IsNullOrEmpty(new_learnings) ? "Identify learnings through user consultation" : new_learnings)}

Maintain document quality:
- Keep structure logical and well-organized
- Preserve important existing content
- Integrate new information seamlessly
- Update related sections consistently

Use PowerShell.MCP to safely update the document with backup creation.
If the document path is not found, confirm the correct location with user.
Present the updated document to the user for review and approval.";
        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create hands-on learning environment for development tools")]
    public static ChatMessage LearnCliTools(
        [Description("Git, Docker, PowerShell, kubectl, etc.")]
        string cli_tool,
        [Description("Beginner, Intermediate, Advanced")]
        string experience_level = "Beginner")
    {
        var prompt = $@"Create hands-on {cli_tool} learning environment for {experience_level} level.

Steps:
1. Confirm user experience level and goals
2. Verify {cli_tool} installation and setup practice folder
3. Follow: Explain → Demonstrate → User Practice → Verify → Next

Key principles:
- Start with simplest commands
- Always open files/terminals for user to practice
- Wait for user confirmation before proceeding
- Use PowerShell.MCP for all demonstrations
- Create progress tracking artifact
- Focus on real-world workflows, not just commands

If {cli_tool} is not installed, guide the user through installation process.
Make the user actively practice each step.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Start fast-paced foreign language dictation training with PowerShell audio and AI evaluation")]
    public static ChatMessage ForeignLanguageDictationTraining(
        [Description("Target language: English, French, German, Spanish, Italian, Chinese, Korean, Japanese")]
        string target_language,
        [Description("Short (3-5 words), Medium (6-8 words), Long (9-12 words), VeryLong (13-15 words)")]
        string sentence_length = "Short",
        [Description("Slow (rate -2), Normal (rate 0), Fast (rate +2), VeryFast (rate +4)")]
        string speech_speed = "Normal",
        [Description("General, Grammar, Vocabulary, Pronunciation, Business, Conversation")]
        string learning_focus = "General")
    {
        var prompt = $@"Start RAPID {target_language} dictation training using PowerShell.MCP:

Settings: {target_language} | {sentence_length} | {speech_speed} | {learning_focus} |

WORKFLOW:
1. Start PowerShell → Minimize: Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class Win32{{ [DllImport(""user32.dll"")]public static extern bool ShowWindow(IntPtr hWnd,int nCmdShow);[DllImport(""kernel32.dll"")]public static extern IntPtr GetConsoleWindow();}}'; [Win32]::ShowWindow([Win32]::GetConsoleWindow(),2)

2. Show guidance in user's native language
3. Generate {sentence_length} {target_language} sentence → Play 2x at {speech_speed} (rate: Slow=-2, Normal=0, Fast=+2, VeryFast=+4)
4. User responds in THIS CHAT (not PowerShell console)
5. Show minimal feedback with tip → Immediately continue
6. Repeat until 'stop'

SENTENCE LENGTHS: Short=3-5, Medium=6-8, Long=9-12, VeryLong=13-15 words

GUIDANCE TEMPLATE (user's native language):
🎯 DICTATION TRAINING - {sentence_length} sentences at {speech_speed} speed
- Audio from minimized PowerShell
- Answer in THIS CHAT
- Say 'stop' to end
- Say 'repeat' to hear again

QUESTION FORMAT:
**Question [X]** ([correct]/[total] correct)
[Audio plays from PowerShell]

FEEDBACK FORMAT (with tip):
✅/❌ ([correct]/[total]) | Answer: [correct] XX% | Tip: [brief learning tip]

Examples:
**Question 1** (0/1 correct)
✅ (1/1) | Answer: I like cats 100% | Tip: Perfect listening!

**Question 2** (1/2 correct)  
❌ (1/2) | Answer: She is happy 67% | Tip: Focus on 'love' vs 'like'

RULES:
- Native language for guidance/feedback, target language for dictation only
- Never show answer before user response
- Match exact word count for {sentence_length}
- Calculate word-level accuracy: (correct/total)×100%
- Track question number and cumulative correct/total count
- ONE brief tip per feedback
- FAST pace, minimal delays
- Restore on stop: [Win32]::ShowWindow([Win32]::GetConsoleWindow(),9)";

        return new ChatMessage(ChatRole.User, prompt);
    }
}
