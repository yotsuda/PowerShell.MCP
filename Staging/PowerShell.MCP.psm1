# PowerShell.MCP Module Script
# Provides automatic cleanup when Remove-Module is executed


# On Linux/macOS, PSReadLine interferes with timer events
# Remove it to ensure MCP polling works correctly
if (-not $IsWindows) {
    Remove-Module PSReadLine -ErrorAction SilentlyContinue
}

# Set OnRemove script block to execute cleanup automatically
$ExecutionContext.SessionState.Module.OnRemove = {
    try {
        #Write-Host "[PowerShell.MCP] Module removal detected, starting cleanup..." -ForegroundColor Yellow

        # Load and execute MCPCleanup.ps1 from embedded resources
        $assembly = [System.Reflection.Assembly]::GetAssembly([PowerShell.MCP.MCPModuleInitializer])
        $resourceName = "PowerShell.MCP.Resources.MCPCleanup.ps1"

        $stream = $assembly.GetManifestResourceStream($resourceName)
        if ($stream) {
            $reader = New-Object System.IO.StreamReader($stream)
            $cleanupScript = $reader.ReadToEnd()
            $reader.Close()
            $stream.Close()

            # Execute cleanup script
            Invoke-Expression $cleanupScript
            #Write-Host "[PowerShell.MCP] OnRemove cleanup completed" -ForegroundColor Green
        } else {
            #Write-Warning "[PowerShell.MCP] MCPCleanup.ps1 resource not found"
        }
    }
    catch {
        #Write-Warning "[PowerShell.MCP] Error during module removal cleanup: $($_.Exception.Message)"
    }
}

#Write-Host "[PowerShell.MCP] Module loaded with OnRemove cleanup support" -ForegroundColor Green

<#
.SYNOPSIS
    Gets the path to the PowerShell.MCP.Proxy executable for the current platform.

.DESCRIPTION
    Returns the full path to the platform-specific PowerShell.MCP.Proxy executable.
    Use this path in your MCP client configuration.

.PARAMETER Escape
    If specified, escapes backslashes for use in JSON configuration files.

.EXAMPLE
    Get-MCPProxyPath
    Returns: C:\Program Files\PowerShell\7\Modules\PowerShell.MCP\bin\win-x64\PowerShell.MCP.Proxy.exe

.EXAMPLE
    Get-MCPProxyPath -Escape
    Returns: C:\\Program Files\\PowerShell\\7\\Modules\\PowerShell.MCP\\bin\\win-x64\\PowerShell.MCP.Proxy.exe

.OUTPUTS
    System.String
#>
function Get-MCPProxyPath {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [switch]$Escape
    )

    $moduleBase = $PSScriptRoot
    $binFolder = Join-Path $moduleBase 'bin'

    # Determine RID based on OS and architecture
    $rid = if ($IsWindows) {
        switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
            'X64'  { 'win-x64' }
            'Arm64' { 'win-arm64' }
            default { 'win-x64' }
        }
    }
    elseif ($IsMacOS) {
        switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
            'X64'  { 'osx-x64' }
            'Arm64' { 'osx-arm64' }
            default { 'osx-x64' }
        }
    }
    elseif ($IsLinux) {
        switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
            'X64'  { 'linux-x64' }
            'Arm64' { 'linux-arm64' }
            'Arm'   { 'linux-arm' }
            default { 'linux-x64' }
        }
    }
    else {
        throw "Unsupported operating system"
    }

    # Determine executable name
    $exeName = if ($IsWindows) { 'PowerShell.MCP.Proxy.exe' } else { 'PowerShell.MCP.Proxy' }

    $proxyPath = Join-Path $binFolder $rid $exeName

    if (-not (Test-Path $proxyPath)) {
        throw "PowerShell.MCP.Proxy not found at: $proxyPath. Please ensure the module is properly installed for your platform ($rid)."
    }

    if ($Escape) {
        return $proxyPath.Replace('\', '\\')
    }

    return $proxyPath
}


<#
.SYNOPSIS
    Gets information about the MCP client that owns this console.

.DESCRIPTION
    Returns ownership information for the current PowerShell console, including
    whether it is owned by an MCP proxy, the proxy's PID, the agent ID,
    and the client name (e.g., Claude Desktop, Claude Code, VS Code).

.EXAMPLE
    Get-MCPOwner

    Owned      : True
    ProxyPid   : 22208
    AgentId    : cc19706b
    ClientName : Claude Desktop

.EXAMPLE
    Get-MCPOwner

    Owned      : False
    ProxyPid   :
    AgentId    :
    ClientName :

.OUTPUTS
    PSCustomObject with Owned, ProxyPid, AgentId, and ClientName properties
#>
function Get-MCPOwner {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    $pipeName = [PowerShell.MCP.MCPModuleInitializer]::GetPipeName()

    if (-not $pipeName) {
        return [PSCustomObject]@{
            Owned      = $false
            ProxyPid   = $null
            AgentId    = $null
            ClientName = $null
        }
    }

    # Parse pipe name segments
    # Unowned: PowerShell.MCP.Communication.{pwshPid} (4 segments)
    # Owned:   PowerShell.MCP.Communication.{proxyPid}.{agentId}.{pwshPid} (6 segments)
    $segments = $pipeName.Split('.')

    if ($segments.Length -ne 6) {
        return [PSCustomObject]@{
            Owned      = $false
            ProxyPid   = $null
            AgentId    = $null
            ClientName = $null
        }
    }

    $proxyPid = [int]$segments[3]
    $agentId  = $segments[4]

    # Determine client name by examining process path and parent chain
    # Uses Get-Process Path and Parent properties (cross-platform, no Win32_Process)
    $clientName = $null
    try {
        $proxyProcess = Get-Process -Id $proxyPid -ErrorAction SilentlyContinue
        $currentProcess = $proxyProcess

        for ($i = 0; $currentProcess -and $i -lt 5; $i++) {
            $processName = $currentProcess.ProcessName.ToLower()
            $processPath = $currentProcess.Path

            # Check process name and path for known clients
            if ($processName -eq 'claude' -or $processPath -match 'AnthropicClaude') {
                $clientName = 'Claude Desktop'
                break
            }
            elseif ($processName -eq 'node' -or $processPath -match 'claude-code|claude_code') {
                $clientName = 'Claude Code'
                break
            }
            elseif ($processName -match '^code$|^code - insiders$') {
                $clientName = 'VS Code'
                break
            }
            elseif ($processName -match 'cursor') {
                $clientName = 'Cursor'
                break
            }

            # Get parent process (PowerShell 7.4+, cross-platform)
            $currentProcess = $currentProcess.Parent
        }

        # Fallback to proxy process name
        if (-not $clientName -and $proxyProcess) {
            $clientName = $proxyProcess.ProcessName
        }
    }
    catch {
        # Ignore errors in process lookup
    }

    return [PSCustomObject]@{
        Owned      = $true
        ProxyPid   = $proxyPid
        AgentId    = $agentId
        ClientName = $clientName
    }
}


<#
.SYNOPSIS
    Installs PowerShell.MCP skills for Claude Code.

.DESCRIPTION
    Copies skill files from the PowerShell.MCP module to the Claude Code skills directory
    (~/.claude/skills/). These skills provide slash commands for common PowerShell.MCP operations.

.PARAMETER Name
    Specifies the names of skills to install. If not specified, all available skills are installed.
    Available skills: ps-analyze, ps-create-procedure, ps-dictation, ps-exec-procedure, ps-html-guidelines, ps-learn, ps-map

.PARAMETER Force
    Overwrites existing skill files without prompting.

.PARAMETER WhatIf
    Shows what would happen if the cmdlet runs. The cmdlet is not run.

.PARAMETER PassThru
    Returns the installed skill file objects.

.EXAMPLE
    Install-ClaudeSkill
    Installs all available skills to ~/.claude/skills/

.EXAMPLE
    Install-ClaudeSkill ps-analyze, ps-learn
    Installs only the 'ps-analyze' and 'ps-learn' skills.

.EXAMPLE
    Install-ClaudeSkill -WhatIf
    Shows which skills would be installed without actually installing them.

.EXAMPLE
    Install-ClaudeSkill ps-analyze -Force
    Installs the 'ps-analyze' skill, overwriting if it exists.

.OUTPUTS
    System.IO.FileInfo (when -PassThru is specified)
#>
function Install-ClaudeSkill {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([System.IO.FileInfo])]
    param(
        [Parameter(Position = 0, ValueFromPipeline, ValueFromPipelineByPropertyName)]
        [ValidateSet('ps-analyze', 'ps-create-procedure', 'ps-dictation', 'ps-exec-procedure', 'ps-html-guidelines', 'ps-learn', 'ps-map')]
        [string[]]$Name,

        [switch]$Force,

        [switch]$PassThru
    )

    begin {
        $moduleSkillsPath = Join-Path $PSScriptRoot 'skills'
        $userSkillsPath = Join-Path $HOME '.claude' 'skills'

        if (-not (Test-Path $moduleSkillsPath)) {
            throw "Skills directory not found in module: $moduleSkillsPath"
        }

        if (-not (Test-Path $userSkillsPath)) {
            New-Item -Path $userSkillsPath -ItemType Directory -Force | Out-Null
            Write-Verbose "Created directory: $userSkillsPath"
        }

        $skillsToInstall = @()
    }

    process {
        if ($Name) {
            $skillsToInstall += $Name
        }
    }

    end {
        $availableSkills = Get-ChildItem -Path $moduleSkillsPath -Filter '*.md' -File

        if ($skillsToInstall.Count -eq 0) {
            $skillsToInstall = $availableSkills | ForEach-Object { $_.BaseName }
        }

        $installedCount = 0
        $skippedCount = 0

        foreach ($skillName in $skillsToInstall | Select-Object -Unique) {
            $sourceFile = Join-Path $moduleSkillsPath "$skillName.md"
            $destFile = Join-Path $userSkillsPath "$skillName.md"

            if (-not (Test-Path $sourceFile)) {
                Write-Warning "Skill not found: $skillName"
                continue
            }

            $shouldInstall = $true
            if ((Test-Path $destFile) -and -not $Force) {
                $sourceHash = (Get-FileHash $sourceFile -Algorithm MD5).Hash
                $destHash = (Get-FileHash $destFile -Algorithm MD5).Hash

                if ($sourceHash -eq $destHash) {
                    Write-Verbose "Skill '$skillName' is already up to date"
                    $skippedCount++
                    $shouldInstall = $false
                }
                else {
                    Write-Warning "Skill '$skillName' already exists with different content. Use -Force to overwrite."
                    $skippedCount++
                    $shouldInstall = $false
                }
            }

            if ($shouldInstall -and $PSCmdlet.ShouldProcess($destFile, "Install skill '$skillName'")) {
                Copy-Item -Path $sourceFile -Destination $destFile -Force
                $installedCount++
                Write-Host "Installed: $skillName" -ForegroundColor Green

                if ($PassThru) {
                    Get-Item $destFile
                }
            }
        }

        if (-not $WhatIfPreference) {
            Write-Host "`nInstalled: $installedCount skill(s), Skipped: $skippedCount skill(s)" -ForegroundColor Cyan
            if ($installedCount -gt 0) {
                Write-Host "Restart Claude Code to use the new skills." -ForegroundColor Yellow
            }
        }
    }
}

Export-ModuleMember -Function Get-MCPProxyPath, Get-MCPOwner, Install-ClaudeSkill
