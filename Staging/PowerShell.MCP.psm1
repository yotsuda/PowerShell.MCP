# PowerShell.MCP Module Script
# Provides automatic cleanup when Remove-Module is executed


# On Linux/macOS, PSReadLine interferes with timer events.
# Remove it and replace with a custom PSConsoleHostReadLine that polls
# Console.KeyAvailable instead of blocking on Console.ReadLine().
# This allows the MCP timer event action block to run between input polls.
if (-not $IsWindows) {
    Remove-Module PSReadLine -ErrorAction SilentlyContinue

    function global:PSConsoleHostReadLine {
        $line = [System.Text.StringBuilder]::new()
        # Publish the in-progress line so the idle-reap tick can tell that the user
        # is mid-typing and keep this console open (#53). PSReadLine's GetBufferState
        # serves that role on Windows, but PSReadLine is removed here — without this
        # the reap tick sees an empty prompt and closes a console the user is
        # actively typing in. StringBuilder is a reference type, so the engine
        # observes edits live; cleared on exit so a submitted or cancelled line is
        # not mistaken for text still pending at the prompt.
        $global:McpTypedBuffer = $line
        try {
            while ($true) {
                if ([Console]::KeyAvailable) {
                    $key = [Console]::ReadKey($true)
                    switch ($key.Key) {
                        'Enter' {
                            [Console]::WriteLine()
                            return $line.ToString()
                        }
                        'Backspace' {
                            if ($line.Length -gt 0) {
                                $line.Length--
                                [Console]::Write("`b `b")
                            }
                        }
                        default {
                            # Ctrl+C: cancel current line
                            if ($key.Key -eq 'C' -and ($key.Modifiers -band [ConsoleModifiers]::Control)) {
                                [Console]::WriteLine("^C")
                                return ""
                            }
                            if ($key.KeyChar -ge ' ') {
                                $line.Append($key.KeyChar) | Out-Null
                                [Console]::Write($key.KeyChar)
                            }
                        }
                    }
                } else {
                    # No input available - sleep briefly.
                    # PowerShell processes the event queue between pipeline statements,
                    # so the MCP timer action block can run during this Sleep.
                    Start-Sleep -Milliseconds 50
                }
            }
        }
        finally {
            $global:McpTypedBuffer = $null
        }
    }
}

# Set OnRemove script block to execute cleanup automatically
<#
.SYNOPSIS
    Registers PowerShell.MCP as an MCP server in Claude Desktop.

.DESCRIPTION
    Adds or updates the "pwsh" entry in Claude Desktop's
    claude_desktop_config.json. Existing settings in the file are preserved,
    including the entry's own "args" and "env": only its command is repointed
    at this module's proxy, so proxy flags and environment settings survive a
    re-registration.
    If a legacy "PowerShell" entry pointing to PowerShell.MCP.Proxy exists,
    it is removed and replaced with the new "pwsh" entry.

.EXAMPLE
    Register-PwshToClaudeDesktop
    Registers PowerShell.MCP in Claude Desktop configuration.

.OUTPUTS
    None. Writes status messages to the host.
#>
function Register-PwshToClaudeDesktop {
    [CmdletBinding()]
    param()

    $serverName = 'pwsh'
    $legacyName = 'PowerShell'
    $command = Get-MCPProxyPath

    # Determine config file path per platform
    # MSIX (Microsoft Store) installs virtualize %APPDATA% under Packages\
    $configPath = if ($IsWindows) {
        $msixDir = Get-ChildItem "$env:LOCALAPPDATA\Packages\Claude_*" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($msixDir) {
            Join-Path $msixDir.FullName 'LocalCache\Roaming\Claude\claude_desktop_config.json'
        } else {
            Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
        }
    } elseif ($IsMacOS) {
        Join-Path $HOME 'Library/Application Support/Claude/claude_desktop_config.json'
    } elseif ($IsLinux) {
        Join-Path $HOME '.config/Claude/claude_desktop_config.json'
    } else {
        throw "Unsupported platform."
    }

    # Load or create config
    if (Test-Path $configPath) {
        $json = Get-Content -Path $configPath -Raw -Encoding UTF8
        $config = $json | ConvertFrom-Json -AsHashtable
    } else {
        $configDir = Split-Path $configPath -Parent
        if (-not (Test-Path $configDir)) {
            New-Item -ItemType Directory -Path $configDir -Force | Out-Null
        }
        $config = @{}
    }

    if (-not $config.ContainsKey('mcpServers')) {
        $config['mcpServers'] = @{}
    }

    # Migrate legacy "PowerShell" entry if it points to PowerShell.MCP.Proxy
    if ($config['mcpServers'].ContainsKey($legacyName)) {
        $legacyCommand = $config['mcpServers'][$legacyName]['command']
        if ($legacyCommand -match 'PowerShell\.MCP\.Proxy') {
            $config['mcpServers'].Remove($legacyName)
            Write-Host "Removed legacy '$legacyName' entry." -ForegroundColor DarkYellow
        }
    }

    # Update the command in place rather than replacing the entry. An entry the
    # user had configured also carries "args" (proxy flags such as
    # --no-profile) and "env" (settings such as POWERSHELL_MCP_TIMEOUT_CEILING);
    # assigning a fresh @{ command = ... } over it dropped both, silently, and
    # the loss only showed up later as a flag that had stopped taking effect.
    if ($config['mcpServers'].ContainsKey($serverName)) {
        $action = 'Updated'
        $entry = $config['mcpServers'][$serverName]
        if ($entry -isnot [System.Collections.IDictionary]) { $entry = @{} }
    }
    else {
        $action = 'Added'
        $entry = @{}
    }

    $entry['command'] = $command
    $config['mcpServers'][$serverName] = $entry

    $config | ConvertTo-Json -Depth 10 | Set-Content -Path $configPath -Encoding UTF8 -NoNewline
    Write-Host "$action '$serverName' in $configPath" -ForegroundColor Green
    Write-Host "  command: $command" -ForegroundColor Gray
    if ($entry['args']) {
        Write-Host "  args: $($entry['args'] -join ' ')" -ForegroundColor Gray
    }
    if ($entry['env']) {
        $pairs = foreach ($key in $entry['env'].Keys) { "$key=$($entry['env'][$key])" }
        Write-Host "  env: $($pairs -join ', ')" -ForegroundColor Gray
    }

    # Both paths need the restart: an updated entry is the case where the point
    # of registering was to move Claude Desktop onto a newly installed proxy.
    Write-Host "Restart Claude Desktop to apply changes." -ForegroundColor Yellow
}

<#
.SYNOPSIS
    Registers PowerShell.MCP as an MCP server in Claude Code.

.DESCRIPTION
    Registers this module's proxy executable as the 'pwsh' MCP server at user
    scope, via the claude CLI.

    If a 'pwsh' entry already exists at that scope — typically written by an
    earlier install and still pointing at that install's proxy — it is replaced,
    because 'claude mcp add' refuses a name that is already registered. The
    arguments and environment variables the old entry carried (proxy flags such
    as --no-profile, settings such as POWERSHELL_MCP_TIMEOUT_CEILING) are
    carried over to the new one, and if the new entry cannot be added the old
    one is put back.

    An entry at local or project scope for the current directory takes
    precedence over the user-scope entry there. It is left alone, and a warning
    names it.

    A legacy "PowerShell" entry pointing at PowerShell.MCP.Proxy is removed
    first.

.EXAMPLE
    Register-PwshToClaudeCode

.EXAMPLE
    Update-Module PowerShell.MCP
    Register-PwshToClaudeCode

    Points Claude Code at the proxy of the version just installed.

.OUTPUTS
    None. Passes through output from the claude CLI.
#>
function Register-PwshToClaudeCode {
    [CmdletBinding()]
    param()

    if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
        Write-Error "Claude Code CLI ('claude') not found. Install it first: https://docs.anthropic.com/en/docs/claude-code"
        return
    }

    $serverName = 'pwsh'
    $legacyName = 'PowerShell'
    $proxyPath = Get-MCPProxyPath

    # 'claude mcp get' reports only the entry that wins for the current directory
    # (local, then project, then user), so inside a project with an entry of its
    # own the user-scope entry this cmdlet manages is hidden. Asked from an empty
    # directory it reports the user-scope entry itself. ('mcp get' takes no -s
    # switch: passing one fails with "unknown option", which is why the legacy
    # migration below never used to run.)
    $neutralDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'PowerShell.MCP.Register'

    function ConvertFrom-McpGetOutput([object[]]$Output) {
        $entry = [pscustomobject]@{ Scope = $null; Command = $null; Args = @(); Env = @() }
        $inEnvironment = $false
        foreach ($item in $Output) {
            $line = [string]$item
            if ($inEnvironment) {
                if ($line -match '^\s{4}(\S[^=]*=.*)$') { $entry.Env += $Matches[1]; continue }
                $inEnvironment = $false
            }
            if ($line -match '^\s*Scope:\s*(\w+)') { $entry.Scope = $Matches[1] }
            elseif ($line -match '^\s*Command:\s*(\S.*)$') { $entry.Command = $Matches[1].Trim() }
            elseif ($line -match '^\s*Args:\s*(\S.*)$') { $entry.Args = @($Matches[1].Trim() -split '\s+') }
            elseif ($line -match '^\s*Environment:\s*$') { $inEnvironment = $true }
        }
        return $entry
    }

    function Get-UserScopeEntry([string]$Name) {
        New-Item -ItemType Directory -Path $neutralDirectory -Force | Out-Null
        Push-Location -LiteralPath $neutralDirectory
        try {
            $output = @(claude mcp get $Name 2>&1)
            $found = ($LASTEXITCODE -eq 0)
        }
        finally {
            Pop-Location
        }
        if (-not $found) { return $null }
        $entry = ConvertFrom-McpGetOutput $output
        if ($entry.Scope -ne 'User') { return $null }
        return $entry
    }

    function Add-UserScopeEntry([string]$Command, [string[]]$CommandArgs, [string[]]$Environment, [switch]$Quiet) {
        $envArgs = @(foreach ($pair in $Environment) { '-e'; $pair })
        if ($Quiet) { claude mcp add $serverName -s user @envArgs -- $Command @CommandArgs 2>&1 | Out-Null }
        else { claude mcp add $serverName -s user @envArgs -- $Command @CommandArgs 2>&1 | Out-Host }
        return ($LASTEXITCODE -eq 0)
    }

    # Remove the legacy "PowerShell" entry if it is ours.
    $legacy = Get-UserScopeEntry $legacyName
    if ($legacy -and $legacy.Command -match 'PowerShell\.MCP\.Proxy') {
        claude mcp remove $legacyName -s user 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Removed legacy '$legacyName' entry from Claude Code." -ForegroundColor DarkYellow
        }
        else {
            Write-Warning "Could not remove the legacy '$legacyName' entry (exit code $LASTEXITCODE). Remove it with: claude mcp remove $legacyName -s user"
        }
    }

    # 'claude mcp add' refuses a name that already exists at the same scope, so a
    # registration written by an older install could never be updated: the add
    # failed and Claude Code kept launching that install's proxy. Remove the
    # user-scope entry first, carrying its arguments and environment variables
    # across, and put it back if the new one cannot be added.
    $existing = Get-UserScopeEntry $serverName
    $commandArgs = @()
    $environment = @()
    if ($existing) {
        $commandArgs = @($existing.Args)
        $environment = @($existing.Env)

        claude mcp remove $serverName -s user 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Could not remove the existing user-scope '$serverName' entry (exit code $LASTEXITCODE); it still points at: $($existing.Command)"
            return
        }
    }

    if (-not (Add-UserScopeEntry $proxyPath $commandArgs $environment)) {
        $exitCode = $LASTEXITCODE
        if (-not $existing) {
            Write-Error "'claude mcp add $serverName' failed with exit code $exitCode."
        }
        elseif (Add-UserScopeEntry $existing.Command $commandArgs $environment -Quiet) {
            Write-Error "'claude mcp add $serverName' failed with exit code $exitCode. The previous entry was restored; it still points at: $($existing.Command)"
        }
        else {
            Write-Error ("'claude mcp add $serverName' failed with exit code $exitCode, and restoring the previous entry failed as well. " +
                "It was: $($existing.Command) $($commandArgs -join ' ') (environment: $($environment -join ', '))")
        }
        return
    }

    $action = if ($existing) { 'Updated' } else { 'Added' }
    Write-Host "$action '$serverName' (user scope)" -ForegroundColor Green
    Write-Host "  command: $proxyPath" -ForegroundColor Gray
    if ($commandArgs) { Write-Host "  args: $($commandArgs -join ' ')" -ForegroundColor Gray }
    if ($environment) { Write-Host "  env: $($environment -join ', ')" -ForegroundColor Gray }

    # An entry at local or project scope for this directory still wins over the
    # user-scope one here. Leave it alone, but say so, or the update would look as
    # if it had not taken effect.
    $visible = @(claude mcp get $serverName 2>&1)
    if ($LASTEXITCODE -eq 0) {
        $winner = ConvertFrom-McpGetOutput $visible
        if ($winner.Scope -and $winner.Scope -ne 'User') {
            $scope = $winner.Scope.ToLowerInvariant()
            Write-Warning ("A '$serverName' MCP server is also configured at $scope scope for $((Get-Location).Path) and takes precedence over the user-scope entry there; it points at $($winner.Command). " +
                "Remove it with: claude mcp remove $serverName -s $scope")
        }
    }

    Write-Host "Restart Claude Code to pick up the change." -ForegroundColor Yellow
}


$ExecutionContext.SessionState.Module.OnRemove = {
    try {
        #Write-Host "[PowerShell.MCP] Module removal detected, starting cleanup..." -ForegroundColor Yellow

        # Load and execute MCPCleanup.ps1 from embedded resources
        $assembly = [System.Reflection.Assembly]::GetAssembly([PowerShell.MCP.MCPModuleInitializer])
        $resourceName = "PowerShell.MCP.Resources.MCPCleanup.ps1"

        $stream = $assembly.GetManifestResourceStream($resourceName)
        if ($stream) {
            try {
                $reader = New-Object System.IO.StreamReader($stream)
                $cleanupScript = $reader.ReadToEnd()
            } finally {
                if ($reader) { $reader.Dispose() }
                if ($stream) { $stream.Dispose() }
            }

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
function Get-McpStatus {
    # Internal helper: builds the unified PowerShell.MCP.Status object that
    # Get-MCPOwner and Restart-MCPServer both return (same type + columns).
    $engineReady = [PowerShell.MCP.MCPModuleInitializer]::EngineReady
    $lastError   = [PowerShell.MCP.MCPModuleInitializer]::LastEngineErrorMessage

    $pipeName = [PowerShell.MCP.MCPModuleInitializer]::GetPipeName()
    $owned      = $false
    $proxyPid   = $null
    $agentId    = $null
    $clientName = $null

    # Owned: PSMCP.{proxyPid}.{agentId}.{pwshPid} (4 segments)
    # Unowned: PSMCP.{pwshPid} (2 segments)
    $segments = if ($pipeName) { $pipeName.Split('.') } else { @() }
    if ($segments.Length -eq 4) {
        $owned    = $true
        $proxyPid = [int]$segments[1]
        $agentId  = $segments[2]

        # Determine client name by examining process path and parent chain
        # Uses Get-Process Path and Parent properties (cross-platform, no Win32_Process)
        try {
            $proxyProcess = Get-Process -Id $proxyPid -ErrorAction SilentlyContinue
            $currentProcess = $proxyProcess
            for ($i = 0; $currentProcess -and $i -lt 5; $i++) {
                $processName = $currentProcess.ProcessName.ToLower()
                $processPath = $currentProcess.Path
                # Claude Desktop and the Claude Code CLI are BOTH a process
                # named 'claude' (Windows: claude.exe; macOS: Claude). Only the
                # install path separates them — Desktop lives under
                # ...\AnthropicClaude\... or /Applications/Claude.app/..., while
                # the CLI is a standalone binary such as ~/.local/bin/claude.
                # So the path test has to come first: with the name test
                # leading, every Claude Code session was reported as
                # 'Claude Desktop' once the CLI stopped being an npm/node
                # install and became its own executable.
                if ($processPath -match 'AnthropicClaude|Claude\.app') { $clientName = 'Claude Desktop'; break }
                elseif ($processName -eq 'claude' -or $processName -eq 'node' -or $processPath -match 'claude-code|claude_code') { $clientName = 'Claude Code'; break }
                elseif ($processName -match '^code$|^code - insiders$') { $clientName = 'VS Code'; break }
                elseif ($processName -match 'cursor') { $clientName = 'Cursor'; break }
                $currentProcess = $currentProcess.Parent
            }
            if (-not $clientName -and $proxyProcess) { $clientName = $proxyProcess.ProcessName }
        }
        catch {
            # Ignore errors in process lookup
        }
    }

    [PowerShell.MCP.Status]@{
        EngineReady = $engineReady
        Owned       = $owned
        ProxyPid    = $proxyPid
        AgentId     = $agentId
        ClientName  = $clientName
        LastError   = $lastError
    }
}

function Get-MCPOwner {
    [CmdletBinding()]
    [OutputType('PowerShell.MCP.Status')]
    param()

    Get-McpStatus
}


<#
.SYNOPSIS
    Stops all pwsh processes to release DLL locks.

.DESCRIPTION
    Stops all pwsh processes on the system to release DLL locks.
    Useful for PowerShell module developers before dotnet build.

    With -PwshPath: starts a new pwsh session from the specified binary with
    PowerShell.MCP loaded, then stops all other pwsh processes.

    WARNING: This stops ALL pwsh processes, including those used by other users
    or other MCP clients on the same machine.

.PARAMETER PwshPath
    Path to the pwsh binary to start. If specified, a new session is started
    from this binary with PowerShell.MCP imported before stopping other processes.

.EXAMPLE
    Stop-AllPwsh
    Stops all pwsh processes. Use before rebuilding a PowerShell module to release DLL locks.

.EXAMPLE
    Stop-AllPwsh -PwshPath (Get-PSOutput)
    Starts a new session using the built pwsh binary, then stops all other pwsh processes.
    Use in the PowerShell repository after Start-PSBuild.
#>
function Stop-AllPwsh {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
    param(
        [Parameter(Position = 0)]
        [string]$PwshPath
    )

    $targets = Get-Process pwsh -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID }
    $count = ($targets | Measure-Object).Count + 1  # +1 for self

    if (-not $PSCmdlet.ShouldProcess("All $count pwsh processes (including this session)", "Stop")) {
        return
    }

    if ($PwshPath) {
        if (-not (Test-Path $PwshPath)) {
            throw "pwsh not found at: $PwshPath"
        }

        # Start new pwsh with PowerShell.MCP loaded
        $newProc = Start-Process $PwshPath -ArgumentList '-NoExit', '-Command', 'Import-Module PowerShell.MCP' -PassThru
        Start-Sleep -Seconds 3

        # Stop all other pwsh processes (except the new one and self)
        Get-Process pwsh -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -notin @($newProc.Id, $PID) } |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }
    else {
        # Stop all other pwsh processes
        $targets | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    # Stop self last
    Stop-Process -Id $PID -Force
}

function Restart-MCPServer {
    <#
    .SYNOPSIS
        Retries starting the PowerShell.MCP console engine.
    .DESCRIPTION
        If the embedded polling engine was blocked at import (usually a
        transient antivirus/AMSI false positive), the module stays loaded but
        commands are unavailable until the engine starts. Run this in the
        affected console to retry. Safe to run when the engine is already
        running (the engine setup is idempotent).
    .EXAMPLE
        Restart-MCPServer
    #>
    [CmdletBinding()]
    [OutputType('PowerShell.MCP.Status')]
    param()

    [void][PowerShell.MCP.MCPModuleInitializer]::TryStartEngine()

    $status = Get-McpStatus

    # Actionable hint on the warning stream only (does not clutter the object
    # output, so the function stays a clean object-returner like Get-MCPOwner).
    if (-not $status.EngineReady) {
        Write-Warning "[PowerShell.MCP] Console engine still not running. Last error: $($status.LastError)"
        Write-Warning "If this persists, it may be Constrained Language Mode / WDAC or an AV policy rather than a transient AMSI block. See https://github.com/yotsuda/PowerShell.MCP"
    }

    $status
}



