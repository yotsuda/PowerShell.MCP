<#
.SYNOPSIS
    Exports PowerShell.MCP prompt templates as Claude Code skills.

.DESCRIPTION
    Reads prompt templates from the embedded resources and generates
    Claude Code skill files with YAML front-matter.

.PARAMETER OutputPath
    Output directory for skill files. Defaults to Staging/skills.

.EXAMPLE
    .\Export-ClaudeSkills.ps1
    Exports all skills to Staging/skills folder.
#>
param(
    [string]$OutputPath = "$PSScriptRoot\Staging\skills"
)

$templatesPath = "$PSScriptRoot\PowerShell.MCP.Proxy\Prompts\Templates"

# Skill definitions: name -> description, allowed-tools
$skillDefs = @{
    "analyze" = @{
        Description = "Analyze content using PowerShell.MCP and provide comprehensive insights"
        AllowedTools = "mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location"
    }
    "html-guidelines" = @{
        Description = "Technical guidelines for generating HTML reports with Chart.js"
        AllowedTools = "mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location"
    }
    "learn" = @{
        Description = "Create hands-on learning environment for programming and CLI tools"
        AllowedTools = "mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location"
    }
    "create-procedure" = @{
        Description = "Create work procedure documents for systematic task management"
        AllowedTools = "mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location"
    }
    "exec-procedure" = @{
        Description = "Execute work following established procedures in work_procedure.md"
        AllowedTools = "mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location"
    }
    "dictation" = @{
        Description = "Foreign language dictation training using text-to-speech"
        AllowedTools = "mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location"
    }
    "map" = @{
        Description = "Create interactive maps with PowerShell.Map module"
        AllowedTools = "mcp__PowerShell__invoke_expression, mcp__PowerShell__start_powershell_console, mcp__PowerShell__get_current_location, WebSearch"
    }
}

# Ensure output directory exists
if (-not (Test-Path $OutputPath)) {
    New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null
}

foreach ($skillName in $skillDefs.Keys) {
    $templateFile = Join-Path $templatesPath "$skillName.md"
    
    if (-not (Test-Path $templateFile)) {
        Write-Warning "Template not found: $templateFile"
        continue
    }
    
    $templateContent = Get-Content $templateFile -Raw
    
    # Replace {{request}} with $ARGUMENTS for Claude Code
    $skillContent = $templateContent -replace '\{\{request\}\}', '$ARGUMENTS'
    
    # Build skill file with YAML front-matter
    $def = $skillDefs[$skillName]
    $skillFile = @"
---
name: ps-$skillName
description: $($def.Description)
allowed-tools: $($def.AllowedTools)
---

$skillContent
"@
    
    $outputFile = Join-Path $OutputPath "ps-$skillName.md"
    Set-Content -Path $outputFile -Value $skillFile -Encoding UTF8
    Write-Host "Exported: ps-$skillName.md" -ForegroundColor Green
}

Write-Host "`nExported $($skillDefs.Count) skills to $OutputPath" -ForegroundColor Cyan