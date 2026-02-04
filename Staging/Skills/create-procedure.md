---
name: create-procedure
description: Create work procedure documents for systematic task management
allowed-tools: mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location
---

LANGUAGE:
Communicate with users and write files in the user's native language

Create work procedure for: $ARGUMENTS

Parse arguments: First argument is work_description, second is working_directory, third (optional) is focus_area (default: "all")

TRACKING FORMAT (File-level - Default):
filename | status | priority | effort | notes
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
   - **PROGRESS UPDATE RULE**: Must update work_progress.md immediately whenever work progresses
   - **LEARNING UPDATE RULE**: Update work_procedure.md when learning occurs during work
5. CREATE work_progress.md using Add-LinesToFile in working_directory IMMEDIATELY:
   - Overall progress summary (counts + percentage)
   - Status legend with workflow: 🚀→⏳→🔍→✅ (🟡Hold/❌Error as needed)
   - File list with markdown table format
   - List ALL files - zero omissions
6. LIST files to commit → ASK final commit Y/N

CRITICAL:
- Default to file-level tracking (works for translation, refactoring, testing, etc.)
- When uncertain, prefer simple file-level over complex formats
- Status workflow: NotStarted → AI works (Working) → AI done (Review) → User approves (Complete)