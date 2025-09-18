using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
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

        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

{technologyFocus} with comprehensive development lifecycle support.{taskFocus}{projectSection}

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
        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Analyze the content at '{content_path}' using PowerShell.MCP and provide comprehensive insights with actionable recommendations.

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

        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Execute and automate {task_type} tasks using PowerShell.MCP.{moduleSection}

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
        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Create a complete learning environment for {programming_language} programming and guide step-by-step learning with emphasis on hands-on practice.

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
    [Description("Create work procedure and progress tracking files.")]
    public static ChatMessage CreateWorkProcedure(
        [Description("Description of the work or project")]
        string work_description,
        [Description("Working directory to analyze")]
        string working_directory,
        [Description("specific focus area")]
        string? focus_area = "all")
    {
        var prompt = $@"LANGUAGE:
Communicate with users and write files in the user's native language

Create work procedure for: {work_description}
Directory: {working_directory} | Focus: {focus_area ?? "all"}

FORMATS:
- File: filename | status | priority | effort_remaining | notes
- Task: task_name | status | dependencies | effort_est | effort_actual | notes  
- Milestone: milestone_name | status | target_date | actual_date | success_criteria | notes
- STATUS: ✅🟡⏳❌🔄🚀

EXECUTE:
1. ASK USER: purpose・scope・deadline・quality criteria
2. Git check → init if needed → ASK Y/N initial commit
3. ANALYZE working directory + auto-detect format type if needed
4. ASK USER: format type + scope
5. CREATE work_procedure.md:
   - LLM reference document for consistent workflow execution
   - overview
   - Git Y/N policy
   - refine this file when learning
   - update work_progress.txt immediately when progress occurs
   - procedures
   - quality
   - risks
6. CREATE work_progress.txt: exact format + COMPLETE coverage
   - List EVERY work item identified (files/tasks/milestones)
   - Zero omissions - if N items exist, list N items
   - NO history section
7. ASK Y/N final commit

CRITICAL: Create real files on disk + user approval for commits + exact formats";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Execute work procedure and track progress with continuous improvement.")]
    public static ChatMessage ExecuteWorkProcedure(
        [Description("Working directory where work will be performed")]
    string working_directory)
    {
        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Execute work in '{working_directory}' with mandatory continuous procedure refinement.

EXECUTE:
1. Navigate to: {working_directory}
2. READ work_procedure.md + work_progress.txt
3. IDENTIFY next priority tasks from documents
4. PERFORM actual work following documented procedures
5. UPDATE work_progress.txt immediately with ✅ status + completion notes
6. MANDATORY: UPDATE work_procedure.md with new learnings/improvements
7. ASK Y/N commit approval

WORK STANDARDS:
- Actually perform tasks, don't just plan
- Generate real outputs + validate quality
- Document what you actually did
- Create backups before significant changes

PROGRESS TRACKING:
- Update status immediately upon completion
- Add specific notes about work performed + results
- Mark blocked tasks as ❌ with clear blocker descriptions

PROCEDURE REFINEMENT (CRITICAL):
Update procedure document with:
- New insights or better approaches discovered
- Clarifications for unclear steps
- Additional details for future sessions
- Remove outdated/incorrect information

DOCUMENT HANDLING:
- Standard filenames: work_procedure.md, work_progress.txt
- If missing: Create basic templates + begin execution
- User approval required for all commits

Start by reading documents, then execute next priority tasks while improving procedures.";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [Description("Create hands-on learning environment for development tools")]
    public static ChatMessage LearnCliTools(
        [Description("PowerShell, Git, Docker, kubectl, etc.")]
        string cli_tool,
        [Description("Beginner, Intermediate, Advanced")]
        string experience_level = "Beginner")
    {
        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Create hands-on {cli_tool} learning environment for {experience_level} level.

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
        [Description("Zoo, Grammar, Vocabulary, Pronunciation, Business, Conversation")]
        string learning_focus = "General")
    {
        var prompt = $@"Start RAPID {target_language} dictation training using PowerShell.MCP:

Settings: {target_language} | {sentence_length} | {speech_speed} | {learning_focus} |

WORKFLOW:
1. Start PowerShell → Minimize + Initialize Speech:
Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class Win32{{ [DllImport(""user32.dll"")]public static extern bool ShowWindow(IntPtr hWnd,int nCmdShow);[DllImport(""kernel32.dll"")]public static extern IntPtr GetConsoleWindow();}}'; [Win32]::ShowWindow([Win32]::GetConsoleWindow(),2); Add-Type -AssemblyName System.Speech; $global:speech = New-Object System.Speech.Synthesis.SpeechSynthesizer; $speech.Rate = 0

2. Check available voices and configure for {target_language}:
$speech.GetInstalledVoices() | ForEach-Object {{ Write-Host ""Voice: $($_.VoiceInfo.Name) - Language: $($_.VoiceInfo.Culture)"" }}

3. Show guidance in user's native language
4. Generate {sentence_length} {target_language} sentence → Play 2x at {speech_speed} (rate: Slow=-2, Normal=0, Fast=+2, VeryFast=+4)
5. User responds in THIS CHAT (not PowerShell console)
6. Show minimal feedback with tip → Immediately continue
7. Repeat until 'stop'

SENTENCE LENGTHS: Short=3-5, Medium=6-8, Long=9-12, VeryLong=13-15 words

GUIDANCE TEMPLATE (user's native language):
🎯 DICTATION TRAINING - {sentence_length} sentences at {speech_speed} speed
- Audio from minimized PowerShell
- Answer in THIS CHAT
- Say 'stop' to end
- Say 'repeat' to hear again
- Say 'faster' or 'slower' to adjust speech speed
- Say 'longer' or 'shorter' to change sentence length
- Say 'info' to show current settings
- Say 'skip' to move to next question
- Say 'back' to replay previous question

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
- Accept phonetically equivalent answers and common spelling variations as correct
- Match exact word count for {sentence_length}
- Calculate word-level accuracy: (correct/total)×100%
- Track question number and cumulative correct/total count
- ONE brief tip per feedback in user's native language
- FAST pace, minimal delays
- Restore on stop: [Win32]::ShowWindow([Win32]::GetConsoleWindow(),9)";

        return new ChatMessage(ChatRole.User, prompt);
    }
}
