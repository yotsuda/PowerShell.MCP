using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using PowerShell.MCP.Proxy.Attributes;

namespace PowerShell.MCP.Proxy.Prompts;

[McpServerPromptType]
public static class PowerShellPrompts
{
    [McpServerPrompt]
    [LocalizedName("Prompt_AnalyzeContent_Name")]
    [ResourceDescription("Prompt_AnalyzeContent_Description")]
    public static ChatMessage AnalyzeContent(
        [ResourceDescription("Prompt_AnalyzeContent_Param_ContentPath")]
        string content_path)
    {
        var htmlPromptName = Resources.PromptDescriptions.Prompt_HtmlGenerationGuidelinesForAi_Name;
        
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
- Display analysis target path at the top of the report (in title or header section)
- Chart.js: https://cdn.jsdelivr.net/npm/chart.js
- Choose the most appropriate chart type based on the analysis results (e.g., Bar, Line, Pie, Scatter, Histogram, Box Plot, Stacked Bar, Radar).
- Use gradients in CSS and Chart.js (ctx.createLinearGradient)
- Use cohesive color palette with visual harmony - diversify colors across charts
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
    [LocalizedName("Prompt_LearnProgrammingAndCli_Name")]
    [ResourceDescription("Prompt_LearnProgrammingAndCli_Description")]
    public static ChatMessage LearnProgrammingAndCli(
        [ResourceDescription("Prompt_LearnProgrammingAndCli_Param_Technology")]
        string technology,
        [ResourceDescription("Prompt_LearnProgrammingAndCli_Param_LearningFocus")]
        string? learning_focus = null)
    {
        var focusSection = string.IsNullOrEmpty(learning_focus)
            ? "Start from fundamentals and progress systematically through core concepts."
            : $"Focus specifically on: {learning_focus}\nProvide targeted learning and practical exercises for this topic.";

        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Create a complete hands-on learning environment for {technology} and guide step-by-step learning with emphasis on practical experience.

LEARNING APPROACH:
{focusSection}

SETUP PHASE:
1. Confirm user's current knowledge and learning goals
2. Create a dedicated practice folder
3. Verify {technology} installation (guide installation if needed)
4. For programming languages: Check Git availability and initialize repository
5. Initialize with the simplest possible working example

LEARNING METHODOLOGY:
Follow this cycle for each concept:
- **Explain**: Introduce the concept clearly and concisely
- **Demonstrate**: Create example files using PowerShell.MCP
- **Practice**: Open files in editor (notepad/code) for hands-on user editing
- **Verify**: Run and explain results together
- **Commit** (for programming): Save progress with clear commit messages

Key principles:
- Always create → open in editor → let user edit → run → explain → commit (for code)
- Make the user an active participant, not a passive observer
- Provide clear, specific editing instructions
- Wait for user confirmation after each practice step
- Encourage experimentation and learning from mistakes
- Start with the simplest examples and add complexity incrementally
- Focus on real-world practical workflows
- Use PowerShell.MCP for all setup and demonstrations
- Create and maintain a progress tracking artifact

PROGRESSION:
- Build knowledge step by step
- Provide exercises and variations for practice
- Connect concepts to practical applications
- Adapt pace to user feedback

Remember: The goal is hands-on learning through active practice, not passive observation.";

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

TRACKING FORMAT (File-level - Default):
filename | status | priority | effort_remaining | notes
STATUS: 🚀NotStarted ⏳Working 🔍Review ✅Complete 🟡Hold ❌Error

WORKFLOW:
1. ASK USER: purpose, scope
2. Git check:
   - Check if git.exe exists → if not: ASK user to auto-install Git CLI → if YES: install git.exe automatically → if NO: continue without version control
   - If Git exists: check repo → init if needed → if uncommitted changes: LIST files → ASK initial commit Y/N
3. ANALYZE: Does work need multiple stages (design→implementation)?
   - Most tasks: Single stage with file-level tracking
   - Complex projects: ASK USER stage breakdown → each stage uses file-level tracking
4. CREATE work_procedure.md using Add-LinesToFile in working_directory IMMEDIATELY:
   - Overview, procedures, quality criteria (AI sets; consult user if unclear), risks, commit policy, progress update rule, learning update rule
   - **COMMIT POLICY**: Unless user explicitly permits otherwise, all commits require BOTH test pass AND user review approval
   - **PROGRESS UPDATE RULE**: Must update work_progress.md immediately whenever work progresses (status changes, task completion, etc.)
   - **LEARNING UPDATE RULE**: Update work_procedure.md when learning occurs during work. Organize and insert at appropriate location (not just append to end). Keep document concise and focused.
5. CREATE work_progress.md using Add-LinesToFile in working_directory IMMEDIATELY:
   - Overall progress summary (counts + percentage)
   - Status legend with workflow: 🚀→⏳→🔍→✅ (🟡Hold/❌Error as needed)
   - File list with exact format (if staged: use ## sections)
   - List ALL files - zero omissions
6. LIST files to commit → ASK final commit Y/N

CRITICAL:
- Default to file-level tracking (works for translation, refactoring, testing, etc.)
- When uncertain, prefer simple file-level over complex formats
- Status workflow: NotStarted → AI works (Working) → AI done (Review) → User approves (Complete)
- Complete means work finished; Git commits handled separately";

        return new ChatMessage(ChatRole.User, prompt);
    }

    [McpServerPrompt]
    [LocalizedName("Prompt_ExecuteWorkProcedure_Name")]
    [ResourceDescription("Prompt_ExecuteWorkProcedure_Description")]
    public static ChatMessage ExecuteWorkProcedure(
        [ResourceDescription("Prompt_ExecuteWorkProcedure_Param_WorkingDirectory")]
        string working_directory)
    {
        var createWorkProcedureName = Resources.PromptDescriptions.Prompt_CreateWorkProcedure_Name;
        var prompt = $@"LANGUAGE:
Communicate with users in the user's native language

Execute work in '{working_directory}' following established procedures.

WORKFLOW:
1. Navigate to: {working_directory}
2. READ work_procedure.md + work_progress.md
3. IDENTIFY next priority tasks (prioritize 🚀NotStarted and ❌Error items)
4. PERFORM work:
   - Update status: 🚀→⏳ (start work) → 🔍 (AI done, needs review)
   - Create real outputs + validate quality
   - Create backups before significant changes
5. FOLLOW work_procedure.md rules for commits, progress updates, and learning updates

CRITICAL:
- Actually perform tasks, don't just plan
- Status workflow: AI works (⏳) → AI done (🔍) → User reviews → User approves (✅)
- If documents missing: Guide user to run '{createWorkProcedureName}' prompt first before executing this prompt
- Follow all policies defined in work_procedure.md

Start by reading documents, then execute next priority tasks.";

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
