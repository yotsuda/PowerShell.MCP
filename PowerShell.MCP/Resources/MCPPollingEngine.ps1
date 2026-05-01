# MCPPollingEngine.ps1

# ===== Main Timer Setup =====

if (-not (Test-Path Variable:global:McpTimer)) {
    $global:McpTimer = New-Object System.Timers.Timer 100
    $global:McpTimer.AutoReset = $false

    # Trim PSReadLine history file if it exceeds 1MB to prevent input lag
    if ($IsWindows) {
        try {
            $histPath = (Invoke-Expression 'Get-PSReadLineOption').HistorySavePath
            if ($histPath -and (Test-Path $histPath) -and (Get-Item $histPath).Length -gt 1MB) {
                $lines = Get-Content $histPath -Tail 4096
                [System.IO.File]::WriteAllLines($histPath, $lines)
            }
        } catch {}
    }

    # Enable ANSI colors for common development CLI tools
    # These environment variables force color output even when stdout is not a TTY

    # Git: Preserve existing GIT_CONFIG_PARAMETERS and append color.ui=always
    # Only add color.ui=always if not already configured
    $existingGitConfig = $env:GIT_CONFIG_PARAMETERS
    if ($existingGitConfig -and $existingGitConfig -match "'color\.ui\s*=") {
        # User already has color.ui configured, respect their setting
    } elseif ($existingGitConfig) {
        # Append color.ui=always to existing config
        $env:GIT_CONFIG_PARAMETERS = "$existingGitConfig 'color.ui=always'"
    } else {
        # No existing config, set color.ui=always
        $env:GIT_CONFIG_PARAMETERS = "'color.ui=always'"
    }

    # yarn, and other Node.js tools
    if (-not $env:FORCE_COLOR) {
        $env:FORCE_COLOR = '1'
    }
    # Modern terminals supporting 24-bit color (truecolor)
    if (-not $env:COLORTERM) {
        $env:COLORTERM = 'truecolor'
    }
    # npm uses NPM_CONFIG_* environment variables for configuration
    if (-not $env:NPM_CONFIG_COLOR) {
        $env:NPM_CONFIG_COLOR = 'always'
    }
    # Rust cargo
    if (-not $env:CARGO_TERM_COLOR) {
        $env:CARGO_TERM_COLOR = 'always'
    }
    # pytest and other Python tools
    if (-not $env:PY_COLORS) {
        $env:PY_COLORS = '1'
    }
    # .NET tools using System.Console
    if (-not $env:DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION) {
        $env:DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION = '1'
    }

    Register-ObjectEvent `
        -InputObject    $global:McpTimer `
        -EventName      Elapsed `
        -SourceIdentifier MCP_Poll `
        -Action { try {
            # Update heartbeat to indicate runspace is available
            [PowerShell.MCP.Services.ExecutionState]::Heartbeat()

            # ===== Proxy Liveness Check (every ~5 seconds) =====
            if (-not $global:__mcpProxyCheckCounter) { $global:__mcpProxyCheckCounter = 0 }
            if ((++$global:__mcpProxyCheckCounter) -ge 50) {
                $global:__mcpProxyCheckCounter = 0
                # Extract proxy PID from pipe name (authoritative source, updated on claim)
                # Owned: PSMCP.{proxyPid}.{agentId}.{pwshPid} = 4 segments
                $pipeName = [PowerShell.MCP.MCPModuleInitializer]::GetPipeName()
                $segments = if ($pipeName) { $pipeName.Split('.') } else { @() }
                if ($segments.Length -eq 4) {
                    $proxyPid = [int]$segments[1]
                    $proxyAlive = try { [System.Diagnostics.Process]::GetProcessById($proxyPid); $true } catch { $false }
                    if (-not $proxyAlive) {
                        [PowerShell.MCP.MCPModuleInitializer]::ReleaseConsole()
                        $global:PowerShellMCPProxyPid = $null
                        $global:PowerShellMCPAgentId = $null
                        $Host.UI.RawUI.WindowTitle = "#$PID ____"
                        [Console]::WriteLine()
                        [Console]::WriteLine()
                        Write-Host 'AI session disconnected. Waiting for next connection.' -ForegroundColor Yellow
                        [Console]::WriteLine()
                        try { $p = & { prompt }; [Console]::Write($p.TrimEnd(' ').TrimEnd('>') + '> ') } catch { [Console]::Write("PS $((Get-Location).Path)> ") }
                    }
                }
            }

            # ===== Helper Functions (Defined within Action Block) =====

            # Invoke-Captured is a thin advanced-function wrapper used so
            # the pipeline that runs the user's command can carry
            # -WarningVariable / -InformationVariable common parameters.
            # Those parameters are only honored on cmdlets and advanced
            # functions, not on bare scriptblock invocations, so the
            # wrapper exists solely to attach them. & $Block invokes the
            # body in the wrapper's scope; PowerShell's CmdletBinding
            # plumbing forwards the variable bindings through.
            function Invoke-Captured {
                [CmdletBinding()]
                param([scriptblock]$Block)
                & $Block
            }

            function Invoke-CommandWithAllStreams {
                param([string]$Command)

                # Snapshot $LASTEXITCODE BEFORE the pipeline so we can
                # tell whether this invocation TOUCHED it (a native exe
                # ran and updated the variable) vs inherited a stale
                # value from an earlier native in a prior call. Without
                # this snapshot, reporting $LASTEXITCODE verbatim would
                # leak a stale 7 from `cmd /c exit 7` into every
                # subsequent pure-PowerShell pipeline.
                $lecAtStart = $global:LASTEXITCODE

                # Hybrid capture wiring:
                #   * 2>&1 | Tee-Object | Out-Host
                #       Streams 1 (Output) and 2 (Error) merge into a
                #       single chronological PSObject sequence — Tee-Object
                #       captures into $pipelineStream while Out-Host
                #       renders each item to the visible terminal in real
                #       time with the host's standard coloring (red+cyan
                #       for ErrorRecord, plain for String, etc.). The
                #       captured items keep their PowerShell types
                #       (String / ErrorRecord / etc.) so the formatter
                #       can decide rendering on the AI side.
                #   * -WarningVariable / -InformationVariable
                #       Streams 3 (Warning) and 6 (Information, includes
                #       Write-Host) are NOT merged: merging them would
                #       force their rendering through Out-Host's
                #       generic record renderer and lose Write-Host's
                #       chosen ForegroundColor on the visible terminal.
                #       Their auto-rendering via the host UI's
                #       WriteWarningLine / WriteInformation paths fires
                #       independently of capture and keeps the colors
                #       intact for the user.
                #   * TeeTextWriter on Console.Out / Console.Error
                #       Catches [Console]::WriteLine and
                #       [Console]::Error.WriteLine direct writes that
                #       bypass the PowerShell stream system entirely.
                #       Pre-fix those bytes were invisible to the AI
                #       side. The tee writes to BOTH the original
                #       writer (preserves real-time visible-console
                #       output) AND a StringBuilder for capture.
                $warningVar = @()
                $informationVar = @()
                $exceptionVar = @()
                $pipelineStream = @()

                $origOut = [Console]::Out
                $origErr = [Console]::Error
                $consoleOutBuf = [System.Text.StringBuilder]::new()
                $consoleErrBuf = [System.Text.StringBuilder]::new()
                [Console]::SetOut([PowerShell.MCP.Services.TeeTextWriter]::new($origOut, $consoleOutBuf))
                [Console]::SetError([PowerShell.MCP.Services.TeeTextWriter]::new($origErr, $consoleErrBuf))

                $ok = $false
                $lec = $lecAtStart
                try {
                    try {
                        $sb = [scriptblock]::Create($Command)
                        Invoke-Captured -Block $sb `
                            -WarningVariable +warningVar `
                            -InformationVariable +informationVar 2>&1 |
                            Tee-Object -Variable pipelineStream |
                            Out-Host
                        # Capture post-pipeline state IMMEDIATELY. Any
                        # statement below (even a bare variable
                        # assignment) resets $? to True, so grab the two
                        # signals we care about right after the pipe.
                        $ok = $?
                        $lec = $global:LASTEXITCODE
                    }
                    catch {
                        # Terminating errors (throw, .NET exceptions
                        # that bubble out, parser errors from
                        # [scriptblock]::Create) reach here. Non-
                        # terminating errors stay inside $pipelineStream
                        # via 2>&1 and don't trigger the catch.
                        $exceptionVar = @($_)
                        $ok = $false
                        $lec = $global:LASTEXITCODE
                    }
                }
                finally {
                    [Console]::SetOut($origOut)
                    [Console]::SetError($origErr)
                }

                # Deduplicate errors in the chronological stream.
                # PowerShell can emit the same ErrorRecord more than once
                # in some pipelines (the legacy -ErrorVariable code
                # documented this). Keep the first occurrence in place so
                # the chronological order is preserved.
                $seenErrors = @{}
                $dedupedStream = [System.Collections.Generic.List[object]]::new()
                foreach ($item in $pipelineStream) {
                    if ($item -is [System.Management.Automation.ErrorRecord]) {
                        $key = "$($item.Exception.Message)|$($item.FullyQualifiedErrorId)|$($item.CategoryInfo.Category)"
                        if ($seenErrors.ContainsKey($key)) { continue }
                        $seenErrors[$key] = $true
                    }
                    $dedupedStream.Add($item)
                }

                # LastExitReport gating: only surface a non-zero native
                # exit when the pipeline OVERALL SUCCEEDED ($? True) AND
                # $LASTEXITCODE was actually written by this pipeline
                # (not stale from before) AND the written value is
                # non-zero. When $? is False the status icon flips to ✗
                # and errorCount is already surfacing the failure, so
                # LastExit would be redundant. Zero means "silent — no
                # native reported, or the last one returned 0". Mirrors
                # ripple's OSC 633;L contract so the two MCPs surface
                # the same semantic across shells.
                $lecChanged = $lec -ne $lecAtStart
                $lastExitReport = if ($ok -and $lecChanged -and $null -ne $lec -and $lec -ne 0) {
                    [int]$lec
                } else {
                    0
                }

                return @{
                    PipelineItems = $dedupedStream
                    Warning = $warningVar
                    Information = $informationVar
                    Exception = $exceptionVar
                    ConsoleOut = $consoleOutBuf.ToString()
                    ConsoleErr = $consoleErrBuf.ToString()
                    LastExitReport = $lastExitReport
                }
            }

            # Cache PSReadLine options (Windows only) - retrieved once for performance
            $script:cachedPSReadLineOptions = if ($IsWindows) {
                try { Invoke-Expression 'Get-PSReadLineOption' } catch { $null }
            } else { $null }

            function Write-ColoredCommand {
                param([string]$Command)

                try {
                    # Use cached PSReadLine options for performance
                    $psReadLineOptions = $script:cachedPSReadLineOptions

                    # Default ANSI colors (matching PSReadLine defaults)
                    $defaultColors = @{
                        'Command'   = "`e[93m" # Yellow
                        'Parameter' = "`e[90m" # DarkGray
                        'String'    = "`e[36m" # DarkCyan
                        'Variable'  = "`e[92m" # Green
                        'Number'    = "`e[97m" # White
                        'Operator'  = "`e[90m" # DarkGray
                        'Keyword'   = "`e[92m" # Green
                        'Comment'   = "`e[32m" # DarkGreen
                        'Member'    = "`e[37m" # Gray
                        'Default'   = "`e[37m" # Gray
                    }

                    $tokens = [System.Management.Automation.PSParser]::Tokenize($Command, [ref]$null)

                    $lastEnd = 0
                    foreach ($token in $tokens) {
                        # Write any text between tokens (whitespace, etc.)
                        if ($token.Start -gt $lastEnd) {
                            [Console]::Write($Command.Substring($lastEnd, $token.Start - $lastEnd))
                        }

                        # Get token text
                        $tokenText = $Command.Substring($token.Start, $token.Length)

                        # Get ANSI color from PSReadLine options, with fallback to defaults
                        $ansiColor = switch ($token.Type) {
                            'Command' {
                                if ($psReadLineOptions.CommandColor) { $psReadLineOptions.CommandColor } else { $defaultColors['Command'] }
                            }
                            'CommandParameter' {
                                if ($psReadLineOptions.ParameterColor) { $psReadLineOptions.ParameterColor } else { $defaultColors['Parameter'] }
                            }
                            'CommandArgument' {
                                if ($psReadLineOptions.DefaultTokenColor) { $psReadLineOptions.DefaultTokenColor } else { $defaultColors['Default'] }
                            }
                            'String' {
                                if ($psReadLineOptions.StringColor) { $psReadLineOptions.StringColor } else { $defaultColors['String'] }
                            }
                            'Variable' {
                                if ($psReadLineOptions.VariableColor) { $psReadLineOptions.VariableColor } else { $defaultColors['Variable'] }
                            }
                            'Member' {
                                if ($psReadLineOptions.MemberColor) { $psReadLineOptions.MemberColor } else { $defaultColors['Member'] }
                            }
                            'Number' {
                                if ($psReadLineOptions.NumberColor) { $psReadLineOptions.NumberColor } else { $defaultColors['Number'] }
                            }
                            'Operator' {
                                if ($psReadLineOptions.OperatorColor) { $psReadLineOptions.OperatorColor } else { $defaultColors['Operator'] }
                            }
                            'Keyword' {
                                if ($psReadLineOptions.KeywordColor) { $psReadLineOptions.KeywordColor } else { $defaultColors['Keyword'] }
                            }
                            'Comment' {
                                if ($psReadLineOptions.CommentColor) { $psReadLineOptions.CommentColor } else { $defaultColors['Comment'] }
                            }
                            default {
                                if ($psReadLineOptions.DefaultTokenColor) { $psReadLineOptions.DefaultTokenColor } else { $defaultColors['Default'] }
                            }
                        }

                        # Write colored token
                        [Console]::Write("${ansiColor}${tokenText}`e[0m")
                        $lastEnd = $token.Start + $token.Length
                    }

                    # Write any remaining text
                    if ($lastEnd -lt $Command.Length) {
                        [Console]::Write($Command.Substring($lastEnd))
                    }

                    [Console]::WriteLine()
                }
                catch {
                    # Fallback to simple output if parsing fails
                    [Console]::WriteLine($Command)
                }
            }

            function Format-McpOutput {
                param(
                    [hashtable]$StreamResults,
                    [string]$LocationInfo,
                    [double]$Duration,
                    [string]$Status = "Ready",
                    [string]$Pipeline = ""
                )

                # Walk PipelineItems in emit order, render each item the
                # same way the streaming Out-Host did on the visible
                # console, and count errors as we go. Output and errors
                # interleave in this single text block — the AI sees the
                # error in its actual position in the run, not collected
                # at the end of a separate "=== ERRORS ===" section.
                $pipelineLines = @()
                $errorCount = 0
                foreach ($item in $StreamResults.PipelineItems) {
                    if ($item -is [System.Management.Automation.ErrorRecord]) {
                        $errorCount++
                        # Use Exception.Message (matches the visible
                        # red+cyan render the user sees, minus the
                        # `Write-Error: ` prefix and trace context that
                        # are PowerShell's own decoration).
                        $pipelineLines += $item.Exception.Message
                    } elseif ($null -eq $item) {
                        $pipelineLines += ""
                    } else {
                        # Out-String runs every item through the same
                        # default formatter Out-Host used; trim trailing
                        # newlines so the join below doesn't double up.
                        $pipelineLines += ($item | Out-String).TrimEnd("`r","`n")
                    }
                }
                $pipelineText = ($pipelineLines -join "`n").Trim()

                # Process exceptions (terminating throws caught inside
                # Invoke-CommandWithAllStreams).
                $exceptionLines = @()
                foreach ($ex in $StreamResults.Exception) {
                    $exceptionLines += if ($ex -is [System.Management.Automation.ErrorRecord]) {
                        $ex.Exception.Message
                    } else {
                        $ex.ToString()
                    }
                }
                $exceptionText = ($exceptionLines -join "`n").Trim()

                # Process warnings.
                $warningLines = @()
                foreach ($warn in $StreamResults.Warning) {
                    $warningLines += if ($warn -is [System.Management.Automation.WarningRecord]) {
                        $warn.Message
                    } else {
                        $warn.ToString()
                    }
                }
                $warningText = ($warningLines -join "`n").Trim()

                # Process information / Write-Host. Skip empty/whitespace
                # records (PowerShell sometimes emits a blank
                # InformationRecord at pipeline boundaries).
                $infoLines = @()
                foreach ($info in $StreamResults.Information) {
                    $messageData = if ($info -is [System.Management.Automation.InformationRecord]) {
                        if ($null -ne $info.MessageData) { $info.MessageData.ToString() } else { $info.ToString() }
                    } else {
                        $info.ToString()
                    }
                    if (-not [string]::IsNullOrWhiteSpace($messageData)) {
                        $infoLines += $messageData
                    }
                }
                $infoText = ($infoLines -join "`n").Trim()

                # Direct console writes — only present when something
                # bypassed the PowerShell stream system entirely
                # ([Console]::WriteLine etc.). On the happy path these
                # buffers stay empty and the section is omitted.
                $consoleOutText = if ($null -ne $StreamResults.ConsoleOut) {
                    ([string]$StreamResults.ConsoleOut).TrimEnd("`r","`n")
                } else { "" }
                $consoleErrText = if ($null -ne $StreamResults.ConsoleErr) {
                    ([string]$StreamResults.ConsoleErr).TrimEnd("`r","`n")
                } else { "" }

                # Append PromptAI (Invoke-Claude/Invoke-GPT/Invoke-Gemini) output if available.
                try {
                    $aiResponse = [PromptAI.Cmdlets.AIStreamingCmdletBase]::LastResponse
                    if (-not [string]::IsNullOrEmpty($aiResponse)) {
                        if ($pipelineText) {
                            $pipelineText = $pipelineText + "`n" + $aiResponse.TrimEnd()
                        } else {
                            $pipelineText = $aiResponse.TrimEnd()
                        }
                        [PromptAI.Cmdlets.AIStreamingCmdletBase]::LastResponse = $null
                    }
                } catch { }

                # Calculate statistics.
                $errorCount += $StreamResults.Exception.Count
                $warningCount = $StreamResults.Warning.Count
                $infoCount = $infoLines.Count
                $hasErrors = $errorCount -gt 0
                # LastExitReport is 0 when the invocation did not
                # surface a hidden native exit (see
                # Invoke-CommandWithAllStreams for the gating). Null-
                # safe read — the older wire shape without this field
                # still drains to 0 silently.
                $lastExitReport = if ($StreamResults.ContainsKey('LastExitReport')) {
                    [int]$StreamResults.LastExitReport
                } else {
                    0
                }

                # Truncate pipeline for status line
                # Split by newline, pipe character, and limit to 30 chars
                $pipelineSummary = ""
                if ($Pipeline) {
                    # Strip leading whitespace/newlines so a pipeline like
                    # "\nWrite-Host hi" doesn't become "..." (or empty when
                    # the pipeline is whitespace-only) on the status line.
                    $trimmedPipeline = $Pipeline.Trim()
                    # Split by newline first, take first non-empty line
                    $firstPart = ($trimmedPipeline -split "[\r\n]")[0].Trim()
                    # Split by pipe character, take first segment
                    $firstPart = ($firstPart -split "\|")[0].Trim()
                    # Truncate to 30 chars if needed
                    if ($firstPart.Length -gt 30) {
                        $pipelineSummary = $firstPart.Substring(0, 27) + "..."
                    } elseif ($firstPart.Length -lt $trimmedPipeline.Length) {
                        # Was truncated by newline or pipe
                        $pipelineSummary = $firstPart + "..."
                    } else {
                        $pipelineSummary = $firstPart
                    }
                }

                # Generate status line
                $statusIcon = if ($hasErrors) { "✗" } else { "✓" }
                $statusText = if ($hasErrors) { "executed with errors" } else { "executed successfully" }
                $durationText = "{0:F2}s" -f $Duration

                # Get window title (contains PID and name like "#12345 Cat")
                $windowTitle = $Host.UI.RawUI.WindowTitle

                $pipelineInfo = if ($pipelineSummary) { " | Pipeline: $pipelineSummary" } else { "" }
                # Zero-count tags (Errors: 0 / Warnings: 0 / Info: 0) are
                # silent on the happy path — a clean run produces a
                # shorter status line and a run with actual events
                # produces a line where every count listed is non-zero
                # by construction. This follows the same "omit what
                # equals zero" discipline the status line already uses
                # for `$pipelineInfo`.
                $errInfo     = if ($errorCount     -gt 0) { " | Errors: $errorCount" }       else { "" }
                $warnInfo    = if ($warningCount   -gt 0) { " | Warnings: $warningCount" }   else { "" }
                $infoInfo    = if ($infoCount      -gt 0) { " | Info: $infoCount" }          else { "" }
                # `LastExit: N` surfaces a native exe that returned
                # non-zero mid-pipeline when the pipeline overall
                # succeeded — the ✓ badge would otherwise silently
                # hide the non-zero exit. Positioned right after
                # Duration so it sits next to the exit-code domain
                # (Errors is about PS $Error stream, a separate axis).
                $lastExitInfo = if ($lastExitReport -gt 0) { " | LastExit: $lastExitReport" } else { "" }
                $statusLine = "$statusIcon Pipeline $statusText | Window: $windowTitle | Status: $Status$pipelineInfo | Duration: $durationText$lastExitInfo$errInfo$warnInfo$infoInfo | $LocationInfo"

                # Compose response. PipelineItems already interleaves
                # Output and Error in emit order, so the leading text
                # block reads chronologically — the AI can see at which
                # point in the run an error happened relative to the
                # surrounding output. Warnings, Information, and direct
                # console writes follow as separate sections only when
                # non-empty, so the simple-success path stays terse.
                $sections = @()
                if ($pipelineText) {
                    $sections += $pipelineText
                    $sections += ""
                }

                # Exceptions (terminating throws) follow the chronological
                # block. Labelled because PowerShell distinguishes
                # terminating from non-terminating errors and the AI
                # reading the response benefits from the same distinction.
                if ($exceptionText) {
                    $sections += "=== EXCEPTIONS ==="
                    $sections += $exceptionText
                    $sections += ""
                }

                if ($warningText) {
                    $sections += "=== WARNINGS ==="
                    $sections += $warningText
                    $sections += ""
                }

                if ($infoText) {
                    $sections += "=== INFO ==="
                    $sections += $infoText
                    $sections += ""
                }

                # Direct console writes (only present when something
                # bypassed the PowerShell stream system entirely). These
                # used to be invisible to the AI side; surfacing them
                # here closes the [Console]::Error.WriteLine gap.
                if ($consoleOutText) {
                    $sections += "=== CONSOLE.OUT (direct) ==="
                    $sections += $consoleOutText
                    $sections += ""
                }
                if ($consoleErrText) {
                    $sections += "=== CONSOLE.ERR (direct) ==="
                    $sections += $consoleErrText
                    $sections += ""
                }

                if ($sections.Count -eq 0) {
                    return $statusLine
                }
                return $statusLine + "`n`n" + (($sections -join "`n").TrimEnd())
            }

            # ===== Main Event Processing =====

            # Handle execute command (atomic read-and-clear)
            $cmdSlot = [PowerShell.MCP.Services.McpServerHost]::ConsumeCommand()
            $cmd = $cmdSlot.Command
            if ($cmd) {
                # Check for elevation patterns and prepend user consent prompt
                if ($cmd -match '-Verb\s+RunAs|(?<!\w)runas[\s/]|(?<!\w)gsudo\s|(?<!\w)sudo\s') {
                    $global:__mcpElevationCmd = $cmd
                    $cmd = 'Write-Host "`n⚠  ELEVATION REQUEST DETECTED" -ForegroundColor Yellow; Write-Host $global:__mcpElevationCmd; $r = Read-Host "`nAllow? (Y/N)"; Remove-Variable __mcpElevationCmd -Scope Global -EA SilentlyContinue; if ($r -ne "Y" -and $r -ne "y") { throw "Elevation denied by user" }; ' + $cmd
                }

                # Inject variables if provided (set before pipeline execution to bypass parser)
                $cmdVariables = $cmdSlot.Variables
                if ($cmdVariables) {
                    foreach ($kvp in $cmdVariables.GetEnumerator()) {
                        Set-Variable -Name $kvp.Key -Value $kvp.Value
                    }
                }

                $mcpOutput = $null
                try {
                    # Add to PSReadLine history (best-effort, before command display)
                    if ($IsWindows -and ($cmd -split "`n").Count -le 2) { try { Invoke-Expression '[Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)' } catch {} }

                    # Display command in console
                    [Console]::WriteLine()
                    try {
                        $promptText = & { prompt }
                        $cleanPrompt = $promptText.TrimEnd(' ').TrimEnd('>')
                        [Console]::Write("${cleanPrompt}> ")
                    }
                    catch {
                        [Console]::Write("PS $((Get-Location).Path)> ")
                    }

                    $isMultiLine = $cmd.Contains("`n") -or $cmd.Contains("`r")
                    if ($isMultiLine) {
                        [Console]::WriteLine()
                    }

                    Write-ColoredCommand $cmd

                    # Clear PromptAI output before execution
                    try { [PromptAI.Cmdlets.AIStreamingCmdletBase]::LastResponse = $null } catch { }

                    # Execute command with hybrid stream capture. The
                    # capture function wires `... 2>&1 | Tee-Object |
                    # Out-Host` internally, which means each output and
                    # error item already streamed to the visible console
                    # in real time (ErrorRecord: red+cyan, plain output:
                    # default formatter), and Warning / Information were
                    # auto-rendered by the host's stream-3 / stream-6
                    # writers (yellow / Write-Host's chosen color)
                    # alongside. So the post-execute Out-Default + manual
                    # Write-Host re-render that used to live here would
                    # duplicate the visible-console output line-for-line
                    # — removed.
                    $streamResults = Invoke-CommandWithAllStreams -Command $cmd

                    # Get duration from C# ExecutionState (managed by WaitForResult)
                    $duration = [PowerShell.MCP.Services.ExecutionState]::ElapsedSeconds

                    # Render terminating-exception messages (the catch
                    # path inside Invoke-CommandWithAllStreams swallowed
                    # them so they didn't reach the streaming Out-Host).
                    # Non-terminating errors went through 2>&1 already
                    # and are visible — they don't need a second pass.
                    if ($streamResults.Exception -and $streamResults.Exception.Count -gt 0) {
                        foreach ($ex in $streamResults.Exception) {
                            if ($ex -is [System.Management.Automation.ErrorRecord]) {
                                Write-Host $ex.Exception.Message -ForegroundColor Red
                            } else {
                                Write-Host $ex.ToString() -ForegroundColor Red
                            }
                        }
                    }

                    # Get current location info
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }

                    $locationInfo = "Location [$($currentLocation.provider)]: $($currentLocation.currentPath)"

                    # Display prompt after command output (without trailing newline)
                    try {
                        $promptText = & { prompt }
                        $cleanPrompt = $promptText.TrimEnd(' ').TrimEnd('>')
                        [Console]::Write("${cleanPrompt}> ")
                    }
                    catch {
                        [Console]::Write("PS $($currentLocation.currentPath)> ")
                    }

                    # Generate MCP formatted output with duration
                    $mcpOutput = Format-McpOutput -StreamResults $streamResults -LocationInfo $locationInfo -Duration $duration -Pipeline $cmd
                }
                catch {
                    $errorMessage = "Command execution failed: $($_.Exception.Message)"
                    Write-Host $errorMessage -ForegroundColor Red
                    $mcpOutput = $errorMessage
                }
                finally {
                    # Ensure NotifyResultReady is always called, even if exit or other terminating statements were executed
                    if ($null -eq $mcpOutput) {
                        $mcpOutput = "Command execution completed"
                    }
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($mcpOutput)
                }
            }

            # Handle silent execute command (atomic read-and-clear)
            $silentCmd = [PowerShell.MCP.Services.McpServerHost]::ConsumeSilentCommand()
            if ($silentCmd) {

                $mcpOutput = $null
                try {
                    # Clear PromptAI output before execution
                    try { [PromptAI.Cmdlets.AIStreamingCmdletBase]::LastResponse = $null } catch { }

                    # Execute command with stream capture
                    $streamResults = Invoke-CommandWithAllStreams -Command $silentCmd

                    # Get duration from C# ExecutionState (managed by WaitForResult)
                    $duration = [PowerShell.MCP.Services.ExecutionState]::ElapsedSeconds

                    # Get current location info
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }

                    $locationInfo = "Location [$($currentLocation.provider)]: $($currentLocation.currentPath)"

                    # Generate MCP formatted output with duration
                    $mcpOutput = Format-McpOutput -StreamResults $streamResults -LocationInfo $locationInfo -Duration $duration -Pipeline $silentCmd
                }
                catch {
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }

                    $locationInfo = "Location [$($currentLocation.provider)]: $($currentLocation.currentPath)"
                    $errorMessage = "Error: $($_.Exception.Message)"
                    $mcpOutput = $locationInfo + "`n" + $errorMessage
                }
                finally {
                    # Ensure NotifyResultReady is always called, even if exit or other terminating statements were executed
                    if ($null -eq $mcpOutput) {
                        $mcpOutput = "Command execution completed"
                    }
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifySilentResultReady($mcpOutput)
                }
            }
            } finally {
                # Restart one-shot timer for next poll
                # (prevents event accumulation when the main runspace is busy with a user command)
                $global:McpTimer.Start()
            }
        } | Out-Null
    $global:McpTimer.Start()
}
