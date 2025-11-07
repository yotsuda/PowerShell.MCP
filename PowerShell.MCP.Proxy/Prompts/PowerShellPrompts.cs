using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using PowerShell.MCP.Proxy.Attributes;
using System.Resources;
using System.Reflection;

namespace PowerShell.MCP.Proxy.Prompts;

[McpServerPromptType]
public static class PowerShellPrompts
{
    private static readonly Lazy<ResourceManager> _resourceManager = new Lazy<ResourceManager>(() =>
        new ResourceManager("PowerShell.MCP.Proxy.Resources.PromptDescriptions", Assembly.GetExecutingAssembly()));

    private static string GetResourceString(string key) => _resourceManager.Value.GetString(key) ?? key;
    [LocalizedName("Prompt_SoftwareDevelopment_Name")]
    [ResourceDescription("Prompt_SoftwareDevelopment_Description")]
    public static ChatMessage SoftwareDevelopment(
        [ResourceDescription("Prompt_SoftwareDevelopment_Param_Technology")]
        string? technology = null,
        [ResourceDescription("Prompt_SoftwareDevelopment_Param_TaskType")]
        string? task_type = null,
        [ResourceDescription("Prompt_SoftwareDevelopment_Param_ProjectPath")]
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
    [LocalizedName("Prompt_AnalyzeContent_Name")]
    [ResourceDescription("Prompt_AnalyzeContent_Description")]
    public static ChatMessage AnalyzeContent(
        [ResourceDescription("Prompt_AnalyzeContent_Param_ContentPath")]
        string content_path)
    {
        var htmlPromptName = GetResourceString("Prompt_HtmlGenerationGuidelinesForAi_Name");
        
        var prompt = $@"Analyze '{content_path}' using PowerShell.MCP and provide comprehensive insights with actionable recommendations.

If the '{htmlPromptName}' prompt is not also selected, remind the user: 'For a visual HTML report, please also select the {htmlPromptName} prompt.'

**FIRST**: Check if user already specified format/language. If not, ask user to choose:
1. Report format (html or md) - default: html
2. Preferred language - default: user's language

**Structure:** 
- **MANDATORY**: Title/Header section displaying the analysis target path
- Plan the optimal report structure based on content type (e.g., HAR files, logs, CSV, JSON, text)
- Common sections to consider: Executive Summary, Key Findings, Visual Charts, Detailed Analysis, Recommendations, Conclusion
- Adapt sections to best suit the analysis subject

**Output:**
If the analysis target is a file, save it in the same folder as that file.
If the analysis target is a folder, save it inside that folder.
The report file name should be as follows:
$reportPath = ""$sourceFolder\$contentName_AnalysisReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').<ext>""

# Save the report to $reportPath, then:
`Start-Process $reportPath`";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_HtmlGenerationGuidelinesForAi_Name")]
    [ResourceDescription("Prompt_HtmlGenerationGuidelinesForAi_Description")]
    public static ChatMessage HtmlGenerationGuidelinesForAi()
    {
        var prompt = $@"## HTML Report Generation - Technical Constraints

**USAGE:** Apply these technical guidelines when generating HTML files as specified in task instructions.

**CRITICAL - Variable Scope:** Use `$script:` for ALL variables shared across invoke_expression calls.

**CRITICAL - Chart.js Data:** Extract arrays using ForEach-Object (%), NOT .Count.
```powershell
# ❌ ($data | Select -First 10).Count  # Returns number, not array
# ✅ Correct:
$script:labels = ($data | Select -First 10 | % {{ $_.Name }}) | ConvertTo-Json -Compress -AsArray
$script:values = ($data | Select -First 10 | % {{ $_.Count }}) | ConvertTo-Json -Compress -AsArray
```

**CRITICAL - Data Validation:** Verify Chart.js data format with Write-Host.
```powershell
Write-Host ""Labels: $script:labels""  # Must show [""value""] not ""value""
Write-Host ""Values: $script:values""  # Must show [10] not 10
```

**CRITICAL - CSS Inheritance:** White backgrounds MUST override inherited text color.
```css
.parent {{ background: linear-gradient(...); color: white; }}
.child {{ background: white; color: #333; /* Prevents white-on-white */ }}
```

**Design:**
- **CRITICAL**: Display analysis target path at the top of the report (in title or header section)
- Chart.js: https://cdn.jsdelivr.net/npm/chart.js
- Use gradients in CSS and Chart.js (ctx.createLinearGradient)
- Use cohesive color palette with visual harmony - diversify colors across charts
- Display analysis target path in report header
- Responsive, print-friendly, with ""Back to Top"" button

**Output:**
If the analysis target is a file, save it in the same folder as that file.
If the analysis target is a folder, save it inside that folder.
The report file name should be as follows:
$reportPath = ""$sourceFolder\$contentName_AnalysisReport_$(Get-Date -Format 'yyyyMMdd_HHmmss').html""

# Save HTML to $reportPath, then:
Start-Process $reportPath
```";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_SystemAdministration_Name")]
    [ResourceDescription("Prompt_SystemAdministration_Description")]
    public static ChatMessage SystemAdministration(
        [ResourceDescription("Prompt_SystemAdministration_Param_TaskType")]
        string task_type,
        [ResourceDescription("Prompt_SystemAdministration_Param_RequiredModule")]
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
    [LocalizedName("Prompt_LearnProgrammingLanguage_Name")]
    [ResourceDescription("Prompt_LearnProgrammingLanguage_Description")]
    public static ChatMessage LearnProgrammingLanguage(
        [ResourceDescription("Prompt_LearnProgrammingLanguage_Param_ProgrammingLanguage")]
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
    [LocalizedName("Prompt_CreateWorkProcedure_Name")]
    [ResourceDescription("Prompt_CreateWorkProcedure_Description")]
    public static ChatMessage CreateWorkProcedure(
        [ResourceDescription("Prompt_CreateWorkProcedure_Param_WorkDescription")]
        string work_description,
        [ResourceDescription("Prompt_CreateWorkProcedure_Param_WorkingDirectory")]
        string working_directory,
        [ResourceDescription("Prompt_CreateWorkProcedure_Param_FocusArea")]
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
    [LocalizedName("Prompt_ExecuteWorkProcedure_Name")]
    [ResourceDescription("Prompt_ExecuteWorkProcedure_Description")]
    public static ChatMessage ExecuteWorkProcedure(
        [ResourceDescription("Prompt_ExecuteWorkProcedure_Param_WorkingDirectory")]
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
    [LocalizedName("Prompt_LearnCliTools_Name")]
    [ResourceDescription("Prompt_LearnCliTools_Description")]
    public static ChatMessage LearnCliTools(
        [LocalizedParameterName("Param_LearnCliTools_CliTool_Name")]
        [ResourceDescription("Prompt_LearnCliTools_Param_CliTool")]
        string cli_tool,
        [LocalizedParameterName("Param_LearnCliTools_ExperienceLevel_Name")]
        [ResourceDescription("Prompt_LearnCliTools_Param_ExperienceLevel")]
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
    [LocalizedName("Prompt_ForeignLanguageDictationTraining_Name")]
    [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Description")]
    public static ChatMessage ForeignLanguageDictationTraining(
        [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Param_TargetLanguage")]
        string target_language,
        [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Param_SentenceLength")]
        string sentence_length = "Short",
        [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Param_SpeechSpeed")]
        string speech_speed = "Normal",
        [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Param_Topic")]
        string topic = "General",
        [ResourceDescription("Prompt_ForeignLanguageDictationTraining_Param_ShowTranslation")]
        string show_translation = "off")
    {
        var prompt = $@"Start RAPID {target_language} dictation training using PowerShell.MCP:

Settings: {target_language} | Length: {sentence_length} | Speed: {speech_speed} | Topic: {topic} | Show translation: {show_translation}

WORKFLOW:
1. Start PowerShell → Minimize + Initialize Speech:
   Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class Win32{{ [DllImport(""user32.dll"")]public static extern bool ShowWindow(IntPtr hWnd,int nCmdShow);[DllImport(""kernel32.dll"")]public static extern IntPtr GetConsoleWindow();}}'; [Win32]::ShowWindow([Win32]::GetConsoleWindow(),2); Add-Type -AssemblyName System.Speech; $global:speech = New-Object System.Speech.Synthesis.SpeechSynthesizer; $speech.Rate = 0
2. Check available voices and set for {target_language}:
   $speech.GetInstalledVoices() | ForEach-Object {{ Write-Host ""Voice: $($_.VoiceInfo.Name) - Language: $($_.VoiceInfo.Culture)"" }}
3. Show guidance (user’s native language)
4. Generate {sentence_length} {target_language} sentence → Play twice at {speech_speed}
   (Slow=-2, Normal=0, Fast=+2, VeryFast=+4)
5. User answers in this chat (not PowerShell)
6. Before each new question, replay the previous sentence once, then play the new one twice.
7. Show brief feedback → Continue immediately
8. Say ""stop"" to end → Restore console: [Win32]::ShowWindow([Win32]::GetConsoleWindow(),9)

SENTENCE LENGTHS: Short=3-5, Medium=6-8, Long=9-12, VeryLong=13-15 words
SPEECH RATE MAPPING: Slow=-2, Normal=0, Fast=+2, VeryFast=+4

GUIDANCE TEMPLATE:
🎯 DICTATION TRAINING - {sentence_length} sentences at {speech_speed} speed

* Audio from minimized PowerShell
* Answer here in chat
* Commands: stop / repeat / faster / slower / longer / shorter / info / skip / back
* Say topic word (e.g., ""airport"") to change theme
* Say ""translation on/off"" to toggle translation

QUESTION FORMAT:
**Question [X]** ([correct]/[total] correct)
{(show_translation == "on" ? "[Show translation]" : "[No translation]")}

AUDIO COMMAND:
$speech.Rate=[rate]; $speech.Speak(""[previous_sentence]""); Start-Sleep -Seconds 1; $speech.Speak(""[sentence]""); $speech.Speak(""[sentence]"")

FEEDBACK FORMAT:
✅/❌ ([correct] / [total]) | Accuracy: [score] % | Tip: [brief tip]

RULES:
- Guidance / feedback in native language, dictation in target language
- Replay previous sentence before next question
- Don't show answers before user responds
- Accept phonetic / spelling variations
- Match {sentence_length}, calculate(correct words / total) × 100 %
- One short tip per feedback
- Keep pace fast and natural
- Use everyday sentences for {{topic}}
-Restore console on stop: [Win32]::ShowWindow([Win32]::GetConsoleWindow(), 9)";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_CreateInteractiveMap_Name")]
    [ResourceDescription("Prompt_CreateInteractiveMap_Description")]
    public static ChatMessage CreateInteractiveMap(
        [ResourceDescription("Prompt_CreateInteractiveMap_Param_MapTheme")]
        string map_theme,
        [ResourceDescription("Prompt_CreateInteractiveMap_Param_TargetArea")]
        string? target_area = null)
    {
        var areaSection = !string.IsNullOrEmpty(target_area)
            ? $"Target area: {target_area}"
            : "Target area: (to be determined)";

        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Create interactive map for ""{map_theme}"" using PowerShell.Map module.
{areaSection}

**IMPORTANT: If both theme and target area are provided in parameters, skip user confirmation and immediately start map creation.**

WORKFLOW:
1. Run start_powershell_console
2. Briefly mention to user: ""Run 'Update-Module PowerShell.Map -Force' if you haven't recently"" (then proceed immediately without waiting)
3. If theme/area incomplete, confirm with user first
4. Research locations thoroughly (use web search for detailed practical information)
5. **CRITICAL - Variable Scope**: Store location data in `$global:` scope for reuse across multiple invoke_expression calls
   ```powershell
   $global:mapLocations = @(
       @{{ Location = "".....""; Label = "".....""; Color = "".....""; Description = ""....."" }}
   )
   ```
   Create rich location data with Labels, Colors, and **detailed Descriptions**
6. Display map using Show-OpenStreetMap with **3D enabled by default** (unless flat terrain)
   ```powershell
   Show-OpenStreetMap -Locations $global:mapLocations -Enable3D -Zoom 12 -Pitch 60
   ```
7. **CRITICAL - Validate coordinates after display:**
   - Check latitude/longitude in results
   - If outliers exist (distance > 0.5° from median): inform user, remove from `$global:mapLocations`, re-display
8. Start automated tour (-Enable3D -Zoom 16 -Pitch 60 -Duration 8 -PauseTime 7) → **After tour completes, re-display all spots with camera reset (Pitch 0, Bearing 0)**
   ```powershell
   Show-OpenStreetMap -Locations $global:mapLocations -Pitch 0 -Bearing 0
   ```

## PowerShell.Map Commands

### Basic Usage:
```powershell
$locations = @(
    @{{
        Location = ""Tokyo Tower""
        Label = ""🗼 Tokyo Tower""
        Color = ""red""
        Description = ""🗼 Tokyo Tower`nHeight: 332.9m`nBuilt: 1958`nEntry: ¥1,200`nHours: 9:00-23:00`nBest: Sunset views""
    }}
)
Show-OpenStreetMap -Locations $locations -Enable3D -Zoom 12 -Pitch 60
```

### Parameters:
- **-Locations**: Array of hashtables with Location, Label, Color, Description
- **-Enable3D**: Show 3D buildings/terrain (recommended for cities/mountains)
- **-Disable3D**: Force 2D flat view
- **-Zoom**: 1-19 (default=13)
- **-Pitch**: 0-85° (0=top-down, 60=3D view)
- **-Bearing**: 0-360° (0=North, 90=East)
- **-Duration**: 0.0-10.0s animation (0=instant)

**IMPORTANT:** If Pitch/Bearing/3D are NOT specified, the map maintains its current state (user's last camera position/mode). Only specify these when you want to change or reset the view.

### Other Commands:
```powershell
# Route display
Show-OpenStreetMapRoute -From ""Tokyo"" -To ""Osaka"" -Color ""#ff0000"" -Width 6

# Automated tour with descriptions (CRITICAL for guided tours)
$tourStops = @(
    @{{ Location = ""Tokyo Tower""; Description = ""🗼 Tokyo Tower`nHeight: 332.9m`nBuilt: 1958`nBest view: Sunset"" }}
    @{{ Location = ""Mount Fuji""; Description = ""🗻 Mt. Fuji`nElevation: 3,776m`nUNESCO World Heritage`nBest: Early morning"" }}
    @{{ Location = ""Kyoto""; Description = ""⛩️ Kyoto`n2000+ temples`nFormer capital 794-1868"" }}
)
Start-OpenStreetMapTour -Locations $tourStops -Enable3D -Zoom 16 -Pitch 60 -Duration 8 -PauseTime 7
```

### Colors:
red, blue, green, orange, violet, yellow, grey, black, gold

## Description Best Practices

**CRITICAL:** Descriptions appear on marker click. Make them informative!

Include: 
- Emoji identifier
- Key facts (height, date, capacity, rating)
- Practical info (entry fee, hours, access)
- Tips (best time, insider knowledge)

Format: Use backtick-n (`n) for line breaks, keep lines 40-60 chars

Example:
```
🗼 Eiffel Tower`nHeight: 330m (1,083 ft)`nBuilt: 1889`nEntry: €28 summit, €18 2nd floor`nHours: 9:00-00:45`nBest: Sunset or night illumination`nAccess: Trocadéro Metro 10 min
```

## Map Theme Examples

- **Tourist spots**: Color by type (museums=blue, monuments=red, parks=green)
- **Restaurants**: Color by cuisine type, include ratings/prices
- **Natural landmarks**: Color by feature type, include elevation/access
- **Historical sites**: Color by period, include UNESCO status/dates
- **Viewpoints**: Color by view type, include best photo times

## Notes

- Map opens at http://localhost:8765/
- Always validate coordinates for geographic accuracy
- Use web search for current information (hours, prices)
- 3D recommended when geography/architecture matters
- When showing new markers to existing map, omit Pitch/Bearing/3D to preserve user's view";

        return new ChatMessage(ChatRole.User, prompt);
    }
}
