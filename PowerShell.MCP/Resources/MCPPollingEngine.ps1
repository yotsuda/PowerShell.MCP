# MCPPollingEngine.ps1

# ===== Main Timer Setup =====

if (-not (Test-Path Variable:global:McpTimer)) {
    $global:McpTimer = New-Object System.Timers.Timer 100
    $global:McpTimer.AutoReset = $false

    # Trim PSReadLine history file if it exceeds 1MB to prevent input lag
    if ($IsWindows) {
        try {
            $histPath = (Get-PSReadLineOption).HistorySavePath
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

            # Cache PSDrive $PWD on the home thread so the pipe-server
            # threads (which can't safely touch SessionState) read AI's
            # actual cwd, not the stale process cwd that Set-Location
            # never updates.
            try { [PowerShell.MCP.Services.ExecutionState]::SetCurrentAiCwd($PWD.Path) } catch {}

            # ===== Proxy disconnect detection (event-driven) =====
            # MCPModuleInitializer watches the owning proxy process via
            # Process.Exited and sets this flag the instant it exits. We consume
            # it here on the runspace's home thread each ~100ms tick and run the
            # thread-affine ReleaseConsole + disconnect notice. Arming covers
            # every owned path (OnImport owned launch + ClaimConsole reclaim), so
            # no liveness poll is needed as a backstop.
            if ([PowerShell.MCP.Services.ExecutionState]::ConsumeProxyExited()) {
                # Act only when currently an owned console (4-segment pipe).
                $pipeName = [PowerShell.MCP.MCPModuleInitializer]::GetPipeName()
                $segments = if ($pipeName) { $pipeName.Split('.') } else { @() }
                if ($segments.Length -eq 4) {
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
                # Write-Progress renders normally on the visible console
                # so the user can see progress for AI-initiated
                # long-running commands (Compress-Archive,
                # Invoke-WebRequest, etc.). Each redraw of pwsh 7's
                # "Minimal" Progress view also writes the bar's text to
                # Console.Out, which our tee captures.
                # Format-McpOutput's $progressOverlayPattern strips the
                # overlay blocks (recognized by their reverse-video
                # bracketed-status framing) from the captured ConsoleOut
                # buffer before surfacing to the AI, so the response
                # stays clean while the visible terminal keeps the
                # animated bar.
                & $Block
            }

            function Invoke-CommandWithAllStreams {
                param(
                    [string]$Command,
                    # When set, do NOT pipe pipeline items to Out-Host —
                    # silent execution paths (get_current_location, the
                    # other internal location/status probes) need the
                    # streams captured into the response hashtable but
                    # MUST NOT splash the captured items onto the
                    # visible console. The user-facing execute_command
                    # path leaves this off so each item streams to the
                    # terminal in real time as the command runs.
                    [switch]$Silent
                )

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
                $informationVar = @()
                $exceptionVar = @()
                # Accumulate via ForEach-Object into this list rather
                # than via Tee-Object -Variable. Tee-Object writes its
                # -Variable only when the pipeline COMPLETES — if a
                # `throw` from inside the user's command terminates
                # the pipeline mid-stream, every item that already
                # flowed through Tee-Object is silently dropped from
                # the variable. ForEach-Object adds to the list one
                # item at a time, so a mid-pipeline throw still leaves
                # the items emitted up to that point in $pipelineStream.
                $pipelineStream = [System.Collections.Generic.List[object]]::new()

                $origOut = [Console]::Out
                $origErr = [Console]::Error
                $consoleOutBuf = [System.Text.StringBuilder]::new()
                $consoleErrBuf = [System.Text.StringBuilder]::new()
                [Console]::SetOut([PowerShell.MCP.Services.TeeTextWriter]::new($origOut, $consoleOutBuf))
                [Console]::SetError([PowerShell.MCP.Services.TeeTextWriter]::new($origErr, $consoleErrBuf))

                # Host UI tee. Catches output that bypasses both the
                # PowerShell stream system AND [Console]::Out: chiefly
                # the `What if:` text that ShouldProcess writes via
                # host.UI.WriteLine, and direct $Host.UI.WriteLine
                # calls in user scripts. Done by reflecting on
                # InternalHostUserInterface's private `_externalUI`
                # field and swapping it to a TeePSHostUserInterface
                # decorator for the duration of the command. Restored
                # in finally so a thrown exception cannot leak the
                # swap into subsequent commands. The reflection target
                # has been stable across every PowerShell version since
                # the InternalHost split (PS 1.0); failure to reflect
                # is non-fatal — we just lose host-tee capture for that
                # command.
                $hostWriteBuf = [System.Text.StringBuilder]::new()
                $uiSwapper = $Host.UI
                $externalUIField = $uiSwapper.GetType().GetField('_externalUI',
                    [System.Reflection.BindingFlags]::NonPublic -bor
                    [System.Reflection.BindingFlags]::Instance)
                $origExternalUI = $null
                if ($null -ne $externalUIField) {
                    try {
                        $origExternalUI = $externalUIField.GetValue($uiSwapper)
                        $teeUI = [PowerShell.MCP.Services.TeePSHostUserInterface]::new($origExternalUI, $hostWriteBuf)
                        $externalUIField.SetValue($uiSwapper, $teeUI)
                    } catch {
                        # Reflection access denied — keep going without
                        # host-tee capture rather than refusing to run
                        # the command.
                        $origExternalUI = $null
                    }
                }

                $ok = $false
                $lec = $lecAtStart
                try {
                    try {
                        $sb = [scriptblock]::Create($Command)
                        # Stream merge map: 2 (Error), 3 (Warning), 4
                        # (Verbose), 5 (Debug) all merge into stream 1
                        # so the chronological tee captures them in
                        # emit order. Only stream 6 (Information) stays
                        # UN-merged because the InformationRecord
                        # carries Write-Host's user-chosen
                        # ConsoleColor as a property, and Out-Host's
                        # generic record formatter doesn't read it back
                        # when rendering merged records — Write-Host's
                        # color would silently flatten to default.
                        # Streams 2/3/4/5 each have their own
                        # type-specific Out-Host formatter that emits
                        # the canonical colored prefix (red+cyan for
                        # ErrorRecord, yellow `WARNING:` for
                        # WarningRecord, yellow `VERBOSE:` /
                        # yellow `DEBUG:` for the corresponding
                        # records) regardless of which stream the
                        # record arrived on, so merging is safe for
                        # them. Tested all four before settling on
                        # this layout.
                        # Silent path swaps Out-Host for Out-Null so
                        # internal probes (get_current_location etc.)
                        # don't dump their JSON onto the user's
                        # terminal. The capture pipeline is otherwise
                        # identical so the AI-facing response shape
                        # stays consistent across both paths.
                        if ($Silent) {
                            Invoke-Captured -Block $sb `
                                -InformationVariable +informationVar 2>&1 3>&1 4>&1 5>&1 |
                                ForEach-Object { [void]$pipelineStream.Add($_); $_ } |
                                Out-Null
                        } else {
                            Invoke-Captured -Block $sb `
                                -InformationVariable +informationVar 2>&1 3>&1 4>&1 5>&1 |
                                ForEach-Object { [void]$pipelineStream.Add($_); $_ } |
                                Out-Host
                        }
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
                    # Restore host UI's external writer if we swapped.
                    if ($null -ne $origExternalUI -and $null -ne $externalUIField) {
                        try { $externalUIField.SetValue($uiSwapper, $origExternalUI) } catch { }
                    }
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
                    Information = $informationVar
                    Exception = $exceptionVar
                    ConsoleOut = $consoleOutBuf.ToString()
                    ConsoleErr = $consoleErrBuf.ToString()
                    HostWrite = $hostWriteBuf.ToString()
                    LastExitReport = $lastExitReport
                }
            }

            # Cache PSReadLine options (Windows only) - retrieved once for performance
            $script:cachedPSReadLineOptions = if ($IsWindows) {
                try { Get-PSReadLineOption } catch { $null }
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
                # console, and count errors as we go. Output, errors,
                # verbose, and debug interleave in this single text
                # block — the AI sees each event in its actual position
                # in the run, not collected at the end of a separate
                # "=== ERRORS ===" section.
                # ArrayList (not @() / +=) because $flushNormalBatch
                # below is a script block with its own scope — `+=` on
                # a plain array would create a local copy and the outer
                # variable would never get the appended item. .Add() on
                # the ArrayList reference mutates the underlying object
                # so the outer scope sees the change.
                $pipelineLines = [System.Collections.ArrayList]::new()
                $errorCount = 0
                $warningCount = 0
                # When a user's pipeline ends with Format-Table /
                # Format-List / Format-Wide / Format-Custom, the items
                # captured by Tee-Object are PowerShell's internal
                # format records (FormatStartData, GroupStartData,
                # FormatEntryData, GroupEndData, FormatEndData). These
                # records can ONLY be rendered as a complete sibling
                # stream — passing a single FormatStartData (or any
                # record without its siblings) through Out-String makes
                # the format engine throw `Operation is not valid due
                # to the current state of the object` from
                # MshCommandRuntime.ThrowTerminatingError. So accumulate
                # consecutive non-special items in $normalBatch and
                # flush them through Out-String collectively whenever a
                # special record (Error / Warning / Verbose / Debug /
                # $null) interrupts, or at end-of-loop.
                $normalBatch = [System.Collections.ArrayList]::new()
                $flushNormalBatch = {
                    if ($normalBatch.Count -gt 0) {
                        $rendered = ($normalBatch | Out-String).TrimEnd("`r","`n")
                        if ($rendered.Length -gt 0) { [void]$pipelineLines.Add($rendered) }
                        $normalBatch.Clear()
                    }
                }
                foreach ($item in $StreamResults.PipelineItems) {
                    if ($item -is [System.Management.Automation.ErrorRecord]) {
                        & $flushNormalBatch
                        $errorCount++
                        # Use Exception.Message (matches the visible
                        # red+cyan render the user sees, minus the
                        # `Write-Error: ` prefix and trace context that
                        # are PowerShell's own decoration).
                        [void]$pipelineLines.Add($item.Exception.Message)
                    } elseif ($item -is [System.Management.Automation.WarningRecord]) {
                        & $flushNormalBatch
                        $warningCount++
                        # Mirrors PowerShell's own visible render which
                        # prefixes WARNING: in yellow.
                        [void]$pipelineLines.Add("WARNING: " + $item.Message)
                    } elseif ($item -is [System.Management.Automation.VerboseRecord]) {
                        & $flushNormalBatch
                        # Mirrors PowerShell's own visible render which
                        # prefixes VERBOSE: in yellow. AI side gets the
                        # plain text version.
                        [void]$pipelineLines.Add("VERBOSE: " + $item.Message)
                    } elseif ($item -is [System.Management.Automation.DebugRecord]) {
                        & $flushNormalBatch
                        [void]$pipelineLines.Add("DEBUG: " + $item.Message)
                    } elseif ($null -eq $item) {
                        & $flushNormalBatch
                        [void]$pipelineLines.Add("")
                    } else {
                        [void]$normalBatch.Add($item)
                    }
                }
                & $flushNormalBatch
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
                #
                # Strip VT control sequences before surfacing. Reasons:
                #   * Write-Progress writes to Console.Out via SGR +
                #     cursor manipulation (e.g. CSI 7m for inverse, the
                #     bar fills with spaces inside CSI 7m...CSI 27m).
                #     Those bytes give the AI nothing useful — the
                #     visual bar doesn't translate to text — and clog
                #     responses with escape gibberish.
                #   * ConPTY can re-emit cursor / clear sequences when
                #     a previous Progress overlay hasn't been scrolled
                #     away by the next command, leaking into ConsoleOut
                #     of unrelated subsequent commands.
                # Strip pattern matches the SGR / cursor-control families
                # we actually see in practice (CSI Ps m, CSI Ps J, CSI
                # Ps K, CSI x;y H/f); not exhaustive across the whole
                # ECMA-48 spec but tight on what shows up in pwsh +
                # ConPTY output.
                $vtPattern = "`e\[[\d;]*[a-zA-Z]"

                # Strip ConsoleOut / ConsoleErr more aggressively than
                # just VT codes. Two cleanup passes:
                #
                #   1. Remove ECMA-48 control sequences (CSI, SGR,
                #      cursor moves, line clears).
                #   2. Collapse runs of spaces longer than 8 to a
                #      single space. Write-Progress draws its bar with
                #      80-column space-padded segments; a leftover
                #      Progress overlay leaking into the next command's
                #      ConsoleOut via ConPTY's redraw-on-next-write
                #      shows up as long horizontal-space runs that
                #      carry no useful information for the AI side.
                #      The collapse keeps incidental double-spaces in
                #      legitimate output untouched.
                #
                # If after stripping the result is whitespace only,
                # surface as empty so the section is omitted entirely
                # rather than rendering an empty header.
                # pwsh 7's "Minimal" Progress view writes each redraw to
                # Console.Out (not via cursor save/restore — the visible
                # console position is fixed by Win32 SetCursorPosition,
                # which doesn't go through our tee). The redraw bytes
                # captured to ConsoleOut form a highly recognizable
                # shape:
                #   \e[<sgr>;1m  <activity> [
                #   \e[7m  <status>  <padding>  \e[27m
                #   <padding>  ]  \e[0m
                # The reverse-video framing of the status (\e[7m...\e[27m)
                # combined with the bar's literal [ ... ] is distinctive
                # enough that no legitimate user output would coincidentally
                # match it. Strip these blocks BEFORE the generic VT-strip
                # so the inner SGR markers are still there to anchor on.
                $progressOverlayPattern = "`e\[\d+(?:;\d+)*m[^`e]*?\[`e\[7m[^`e]*?`e\[27m[^`e]*?\]`e\[0m"
                $cleanConsoleStream = {
                    param([string]$raw)
                    if (-not $raw) { return "" }
                    $stripped = $raw -replace $progressOverlayPattern, ""
                    $stripped = $stripped -replace $vtPattern, ""
                    $collapsed = $stripped -replace ' {8,}', '  '
                    $trimmed = $collapsed.TrimEnd("`r","`n", " ", "`t")
                    if ([string]::IsNullOrWhiteSpace($trimmed)) { return "" }
                    return $trimmed
                }
                $consoleOutText = if ($null -ne $StreamResults.ConsoleOut) {
                    & $cleanConsoleStream ([string]$StreamResults.ConsoleOut)
                } else { "" }
                $consoleErrText = if ($null -ne $StreamResults.ConsoleErr) {
                    & $cleanConsoleStream ([string]$StreamResults.ConsoleErr)
                } else { "" }

                # Host-UI-level Write/WriteLine (WhatIf messages,
                # $Host.UI.WriteLine direct calls). Captured by
                # TeePSHostUserInterface in Invoke-CommandWithAllStreams.
                # Same VT-strip rationale as ConsoleOut: the visible
                # console gets the colored render via the inner UI; the
                # AI side wants the plain text. Trim trailing
                # whitespace so the section doesn't end on dangling
                # blanks.
                #
                # Out-Host renders normal pipeline output by calling
                # $Host.UI.WriteLine internally — so without filtering,
                # every regular output line would appear twice (once in
                # pipelineText, once in this section). Filter
                # line-by-line: keep only HostWrite lines that don't
                # appear in any of the already-emitted sections. What
                # remains is the novel host-UI writes the streams /
                # Console-tee never saw — chiefly WhatIf messages from
                # ShouldProcess and direct $Host.UI.WriteLine calls.
                $hostWriteText = ""
                if ($null -ne $StreamResults.HostWrite) {
                    $hostRaw = ([string]$StreamResults.HostWrite -replace $vtPattern, "")
                    if ($hostRaw.Trim()) {
                        # Build a HashSet of lines that are already
                        # surfaced in another section. A line that
                        # exactly matches one of these is treated as a
                        # render-time duplicate and dropped from
                        # HostWrite.
                        # VT-strip both sides before comparison.
                        # pipelineText (and the other section sources)
                        # carries the cmdlet's own SGR codes from
                        # AnsiColors.* renders — Update-LinesInFile in
                        # particular emits coloured context / inserted
                        # / deleted lines. $hostRaw was already
                        # VT-stripped earlier in this method. Without
                        # also stripping the comparison set, the dedup
                        # mismatched on the ANSI runs and let the
                        # whole cmdlet output leak into the HOST.UI
                        # section as plain text duplicates.
                        $known = [System.Collections.Generic.HashSet[string]]::new(
                            [System.StringComparer]::Ordinal)
                        # Whitespace-collapsed parallel set: catches the
                        # Out-String vs Out-Host column-width drift on
                        # Format-Table / Format-Wide / Format-List.
                        # Out-String uses the IRawUserInterface buffer
                        # width (often 120) while Out-Host uses the
                        # actual visible terminal width, so the same
                        # row often ends up with one space difference
                        # in inter-column padding ("xxx   yyy" vs
                        # "xxx    yyy"). Exact-match and substring
                        # checks both miss it. Normalising whitespace
                        # runs to a single space catches these without
                        # affecting other dedup paths (legitimate
                        # host-UI lines that happen to normalise the
                        # same as a pipeline line are virtually
                        # impossible in practice).
                        $knownNorm = [System.Collections.Generic.HashSet[string]]::new(
                            [System.StringComparer]::Ordinal)
                        foreach ($src in @($pipelineText, $exceptionText, $infoText, $consoleOutText, $consoleErrText)) {
                            if (-not [string]::IsNullOrEmpty($src)) {
                                $srcStripped = $src -replace $vtPattern, ""
                                foreach ($line in $srcStripped -split "`r?`n") {
                                    $trimmed = $line.Trim()
                                    if ($trimmed) {
                                        [void]$known.Add($trimmed)
                                        [void]$knownNorm.Add(($trimmed -replace '\s+', ' '))
                                    }
                                }
                            }
                        }
                        $novel = @()
                        foreach ($line in $hostRaw -split "`r?`n") {
                            $trimmed = $line.Trim()
                            if (-not $trimmed) { continue }
                            if ($known.Contains($trimmed)) { continue }
                            $normalized = $trimmed -replace '\s+', ' '
                            if ($knownNorm.Contains($normalized)) { continue }
                            # Check substring containment for partial
                            # matches: Out-Host renders ErrorRecord
                            # through a multi-line formatter so a
                            # single host-UI WriteLine carries the
                            # whole render block, while pipelineText
                            # only has the ErrorRecord's
                            # Exception.Message. Drop host-UI lines
                            # that are entirely inside a pipelineText
                            # line (or vice versa) to suppress this
                            # form of duplicate.
                            $isDup = $false
                            foreach ($k in $known) {
                                if ($k.Contains($trimmed) -or $trimmed.Contains($k)) {
                                    $isDup = $true; break
                                }
                            }
                            if (-not $isDup) { $novel += $trimmed }
                        }
                        $hostWriteText = ($novel -join "`n").Trim()
                    }
                }

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
                # warningCount accumulates as we walked PipelineItems above
                # — Warning records arrive on stream 3 and merge into
                # pipelineStream via 3>&1, so they're counted there.
                $infoCount = $infoLines.Count
                $hasErrors = $errorCount -gt 0
                # LastExitReport is 0 when the invocation did not
                # surface a hidden native exit (see
                # Invoke-CommandWithAllStreams for the gating).
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

                # Warning records now interleave inline in pipelineText
                # via the 3>&1 stream merge, so the dedicated
                # `=== WARNINGS ===` section is gone — the AI sees
                # `WARNING: msg` in its actual position relative to
                # surrounding output / errors / verbose / debug.
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
                # Host UI Write/WriteLine (WhatIf, $Host.UI.WriteLine).
                # Empty on the happy path (no ShouldProcess, no direct
                # host-UI write); section omitted then.
                if ($hostWriteText) {
                    $sections += "=== HOST.UI (direct) ==="
                    $sections += $hostWriteText
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
                    if ($IsWindows -and ($cmd -split "`n").Count -le 2) { try { [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd) } catch {} }

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

                    # Generate MCP formatted output with duration
                    $mcpOutput = Format-McpOutput -StreamResults $streamResults -LocationInfo $locationInfo -Duration $duration -Pipeline $cmd
                }
                catch {
                    $errorMessage = "Command execution failed: $($_.Exception.Message)"
                    Write-Host $errorMessage -ForegroundColor Red
                    $mcpOutput = $errorMessage
                }
                finally {
                    # Render the visible-console post-prompt LAST so it
                    # always appears below any error message that the
                    # try-body's exception render or the outer catch
                    # wrote. Pre-fix, the post-prompt was rendered inside
                    # the try BEFORE Format-McpOutput; if Format-McpOutput
                    # (or any preceding step) threw, the catch's
                    # `Write-Host error -Red` ended up to the right of
                    # the already-written prompt, producing
                    # "PS C:\tmp> ERROR MESSAGE" with no fresh prompt
                    # below. Triple fallback: prompt() → Get-Location →
                    # bare "PS> " keeps the line correct even if both
                    # earlier paths threw on this iteration.
                    try {
                        $promptText = & { prompt }
                        # Force cursor to col 0 of a fresh line before
                        # writing the prompt. Out-Host's flush of the
                        # last pipeline item can leave the cursor at a
                        # non-zero column (especially after recent
                        # changes that increased polling-engine activity
                        # — $PWD probes during the pipeline, etc.); a
                        # prompt written at that position appears glued
                        # to the AI's last output line and PSReadLine
                        # then anchors user input there.
                        if ([Console]::CursorLeft -gt 0) { [Console]::WriteLine() }
                        [Console]::Write($promptText.TrimEnd(' ').TrimEnd('>') + '> ')
                    }
                    catch {
                        try {
                            if ([Console]::CursorLeft -gt 0) { [Console]::WriteLine() }
                            [Console]::Write("PS $((Get-Location).Path)> ")
                        } catch { [Console]::Write("PS> ") }
                    }

                    # Refresh AI cwd cache so the pipe server's post-execution
                    # cwd snapshot reflects any Set-Location the pipeline did.
                    # The 100ms timer Action only fires when runspace is idle,
                    # so the pre-pipeline tick's cached value is stale here.
                    try { [PowerShell.MCP.Services.ExecutionState]::SetCurrentAiCwd($PWD.Path) } catch {}

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

                    # Execute command with stream capture in silent
                    # mode so the result lands in $streamResults but
                    # nothing goes to the user's terminal.
                    $streamResults = Invoke-CommandWithAllStreams -Command $silentCmd -Silent

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
                    # Refresh AI cwd cache (silent commands can also Set-Location)
                    try { [PowerShell.MCP.Services.ExecutionState]::SetCurrentAiCwd($PWD.Path) } catch {}

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
