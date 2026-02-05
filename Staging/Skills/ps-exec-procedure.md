---
name: ps-exec-procedure
description: Execute work following established procedures in work_procedure.md
allowed-tools: mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location
---

LANGUAGE:
Communicate with users in the user's native language

Execute work in '$ARGUMENTS' following established procedures.

WORKFLOW:
1. Navigate to: $ARGUMENTS
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
- If documents missing: Guide user to run 'Create Work Procedure' prompt first before executing this prompt
- Follow all policies defined in work_procedure.md

Start by reading documents, then execute next priority tasks.
