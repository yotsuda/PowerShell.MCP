# MCPPollingEngine.ps1

# ===== Main Timer Setup =====

if (-not (Test-Path Variable:global:McpTimer)) {
    $global:McpTimer = New-Object System.Timers.Timer 100
    $global:McpTimer.AutoReset = $true

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
    # GitHub CLI (gh) and other tools using CLICOLOR
    if (-not $env:CLICOLOR_FORCE) {
        $env:CLICOLOR_FORCE = '1'
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
        -Action {
            # Update heartbeat to indicate runspace is available
            [PowerShell.MCP.Services.ExecutionState]::Heartbeat()

            # ===== Helper Functions (Defined within Action Block) =====

            function Invoke-CommandWithAllStreams {
                param([string]$Command)

                # Initialize output variables
                $outVar = @()
                $errorVar = @()
                $warningVar = @()
                $informationVar = @()

                try {
                    $redirectedOutput = Invoke-Expression $Command `
                        -OutVariable outVar `
                        -ErrorVariable errorVar `
                        -WarningVariable warningVar `
                        -InformationVariable informationVar

                    # Deduplicate errors (PowerShell ErrorVariable can record the same error multiple times)
                    $uniqueErrors = @()
                    $seenErrors = @{}
                    foreach ($err in $errorVar) {
                        # Create a unique key based on message, error ID, and category
                        $key = if ($err -is [System.Management.Automation.ErrorRecord]) {
                            "$($err.Exception.Message)|$($err.FullyQualifiedErrorId)|$($err.CategoryInfo.Category)"
                        } else {
                            $err.ToString()
                        }

                        if (-not $seenErrors.ContainsKey($key)) {
                            $uniqueErrors += $err
                            $seenErrors[$key] = $true
                        }
                    }

                    return @{
                        Success = $outVar
                        Error = $uniqueErrors
                        Exception = @()
                        Warning = $warningVar
                        Information = $informationVar
                    }
                }
                catch {
                    $exceptionVar = @($_)

                    return @{
                        Success = $outVar
                        Error = @()
                        Exception = $exceptionVar
                        Warning = $warningVar
                        Information = $informationVar
                    }
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

                # Process each stream type
                $outputStreams = @{
                    Success = @()
                    Error = @()
                    Exception = @()
                    Warning = @()
                    Information = @()
                }

                # Process errors
                foreach($err in $StreamResults.Error) {
                    if ($err -is [System.Management.Automation.ErrorRecord]) {
                        $outputStreams.Error += $err.Exception.Message
                    } else {
                        $outputStreams.Error += $err.ToString()
                    }
                }

                # Process exceptions
                foreach($ex in $StreamResults.Exception) {
                    if ($ex -is [System.Management.Automation.ErrorRecord]) {
                        $outputStreams.Exception += $ex.Exception.Message
                    } else {
                        $outputStreams.Exception += $ex.ToString()
                    }
                }

                # Process warnings
                foreach($warn in $StreamResults.Warning) {
                    if ($warn -is [System.Management.Automation.WarningRecord]) {
                        $outputStreams.Warning += $warn.Message
                    } else {
                        $outputStreams.Warning += $warn.ToString()
                    }
                }

                # Process information
                foreach($info in $StreamResults.Information) {
                    if ($info -is [System.Management.Automation.InformationRecord]) {
                        $messageData = if ($info.MessageData -ne $null) {
                            $info.MessageData.ToString()
                        } else {
                            $info.ToString()
                        }
                        $outputStreams.Information += $messageData
                    } else {
                        $outputStreams.Information += $info.ToString()
                    }
                }

                # Process success
                foreach ($item in $StreamResults.Success) {
                    $outputStreams.Success += $item
                }

                # Calculate statistics
                $errorCount = $outputStreams.Error.Count + $outputStreams.Exception.Count
                $warningCount = $outputStreams.Warning.Count
                $infoCount = $outputStreams.Information.Count
                $hasErrors = $errorCount -gt 0

                # Truncate pipeline for status line
                # Split by newline, pipe character, and limit to 30 chars
                $pipelineSummary = ""
                if ($Pipeline) {
                    # Split by newline first, take first line
                    $firstPart = ($Pipeline -split "[\r\n]")[0].Trim()
                    # Split by pipe character, take first segment
                    $firstPart = ($firstPart -split "\|")[0].Trim()
                    # Truncate to 30 chars if needed
                    if ($firstPart.Length -gt 30) {
                        $pipelineSummary = $firstPart.Substring(0, 27) + "..."
                    } elseif ($firstPart.Length -lt $Pipeline.TrimEnd().Length) {
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
                $statusLine = "$statusIcon Pipeline $statusText | Window: $windowTitle | Status: $Status$pipelineInfo | Duration: $durationText | Errors: $errorCount | Warnings: $warningCount | Info: $infoCount | $LocationInfo"

                # Generate structured output strings
                $structuredOutput = @{
                    Success = ($outputStreams.Success | Out-String).Trim()
                    Error = ($outputStreams.Error -join "`n").Trim()
                    Exception = ($outputStreams.Exception -join "`n").Trim()
                    Warning = ($outputStreams.Warning -join "`n").Trim()
                    Information = ($outputStreams.Information -join "`n").Trim()
                }

                # Remove empty outputs
                $cleanOutput = @{}
                foreach ($key in $structuredOutput.Keys) {
                    if (-not [string]::IsNullOrEmpty($structuredOutput[$key])) {
                        $cleanOutput[$key] = $structuredOutput[$key]
                    }
                }

                # Generate formatted output
                # Check if only success output exists
                $onlySuccess = ($cleanOutput.Count -eq 1 -and $cleanOutput.Keys -contains 'Success')

                if ($onlySuccess -and -not $hasErrors) {
                    # Simple success case: status + output
                    if ($cleanOutput.Success) {
                        return $statusLine + "`n`n" + $cleanOutput.Success
                    } else {
                        return $statusLine
                    }
                } else {
                    # Complex case with multiple streams
                    $formattedOutput = @($statusLine, "")

                    if ($cleanOutput.Exception) {
                        $formattedOutput += "=== EXCEPTIONS ==="
                        $formattedOutput += $cleanOutput.Exception
                        $formattedOutput += ""
                    }

                    if ($cleanOutput.Error) {
                        $formattedOutput += "=== ERRORS ==="
                        $formattedOutput += $cleanOutput.Error
                        $formattedOutput += ""
                    }

                    if ($cleanOutput.Warning) {
                        $formattedOutput += "=== WARNINGS ==="
                        $formattedOutput += $cleanOutput.Warning
                        $formattedOutput += ""
                    }

                    if ($cleanOutput.Success) {
                        $formattedOutput += "=== SUCCESS ==="
                        $formattedOutput += $cleanOutput.Success
                        $formattedOutput += ""
                    }

                    if ($cleanOutput.Information) {
                        $formattedOutput += "=== INFO ==="
                        $formattedOutput += $cleanOutput.Information
                        $formattedOutput += ""
                    }

                    return ($formattedOutput -join "`n").Trim()
                }
            }

            # ===== Main Event Processing =====

            # Handle execute command
            $cmd = [PowerShell.MCP.Services.McpServerHost]::executeCommand
            if ($cmd) {
                [PowerShell.MCP.Services.McpServerHost]::executeCommand = $null
                if ($IsWindows) { Invoke-Expression '[Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)' }

                $mcpOutput = $null
                try {
                    # Display command in console
                    [Console]::WriteLine()
                    try {
                        $promptText = & { prompt }
                        $cleanPrompt = $promptText.TrimEnd(' ').TrimEnd('>')
                        [Console]::Write("${cleanPrompt}> `e[0K")
                    }
                    catch {
                        [Console]::Write("PS $((Get-Location).Path)> `e[0K")
                    }

                    $isMultiLine = $cmd.Contains("`n") -or $cmd.Contains("`r")
                    if ($isMultiLine) {
                        [Console]::WriteLine()
                    }

                    Write-ColoredCommand $cmd

                    # Execute command with clean stream capture
                    $streamResults = Invoke-CommandWithAllStreams -Command $cmd

                    # Get duration from C# ExecutionState (managed by WaitForResult)
                    $duration = [PowerShell.MCP.Services.ExecutionState]::ElapsedSeconds

                    # Display results in console
                    $streamResults.Success | Out-Default

                    # Display exceptions in console
                    if ($streamResults.Exception -and $streamResults.Exception.Count -gt 0) {
                        foreach ($ex in $streamResults.Exception) {
                            if ($ex -is [System.Management.Automation.ErrorRecord]) {
                                Write-Host $ex.Exception.Message -ForegroundColor Red
                            } else {
                                Write-Host $ex.ToString() -ForegroundColor Red
                            }
                        }
                    }

                    # Display errors in console
                    if ($streamResults.Error -and $streamResults.Error.Count -gt 0 -and (-not $streamResults.Exception -or $streamResults.Exception.Count -eq 0)) {
                        foreach ($err in $streamResults.Error) {
                            if ($err -is [System.Management.Automation.ErrorRecord]) {
                                Write-Host $err.Exception.Message -ForegroundColor Red
                            } else {
                                Write-Host $err.ToString() -ForegroundColor Red
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
                        [Console]::Write("${cleanPrompt}> `e[0K")
                    }
                    catch {
                        [Console]::Write("PS $($currentLocation.currentPath)> `e[0K")
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

            # Handle silent execute command
            $silentCmd = [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent
            if ($silentCmd) {
                [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent = $null

                $mcpOutput = $null
                try {
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
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($mcpOutput)
                }
            }
        } | Out-Null
    $global:McpTimer.Start()
}
