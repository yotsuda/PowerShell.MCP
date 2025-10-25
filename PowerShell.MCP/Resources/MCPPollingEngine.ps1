function Invoke-WithStreamCapture($Command) { $captured = @(); & { Invoke-Expression $Command } *>&1 | Tee-Object -Variable captured | Out-Default; return $captured }

# ===== Main Timer Setup =====

if (-not (Test-Path Variable:global:McpTimer)) {
    $global:McpTimer = New-Object System.Timers.Timer 100
    $global:McpTimer.AutoReset = $true

    Register-ObjectEvent `
        -InputObject    $global:McpTimer `
        -EventName      Elapsed `
        -SourceIdentifier MCP_Poll `
        -Action {
            
            # ===== Handle Insert Command =====
            $cmd = [PowerShell.MCP.Services.McpServerHost]::insertCommand
            if ($cmd) {
                [PowerShell.MCP.Services.McpServerHost]::insertCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)
                [Microsoft.PowerShell.PSConsoleReadLine]::DeleteLine()
                [Microsoft.PowerShell.PSConsoleReadLine]::Insert($cmd)
                [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady("Your pipeline has been inserted into the PS console.")
            }

            # ===== Handle Execute Command =====
            $cmd = [PowerShell.MCP.Services.McpServerHost]::executeCommand
            if ($cmd) {
                [PowerShell.MCP.Services.McpServerHost]::executeCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)

                try {
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
                        Write-Host $cmd
                        [Console]::WriteLine()
                    }
                    else
                    {
                        Write-Host $cmd
                    }

                    # Execute using Invoke-Expression with stream capture
                    # Use *>&1 to merge all streams, then Tee-Object to display and capture
                    $output = @()
                    $errorsBefore = $Error.Count
                    
                    $duration = Measure-Command {
                        $output = Invoke-WithStreamCapture $cmd
                    }
                    
                    $hadErrors = ($Error.Count -gt $errorsBefore)

                    # Get current location info
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }

                    # Generate status line
                    $statusIcon = if ($hadErrors) { "✗" } else { "✓" }
                    $statusText = if ($hadErrors) { "executed with errors" } else { "executed successfully" }
                    $durationText = "{0:F2}s" -f $duration.TotalSeconds
                    
                    $locationInfo = "Location [$($currentLocation.provider)]: $($currentLocation.currentPath)"
                    $statusLine = "$statusIcon Pipeline $statusText | Duration: $durationText | $locationInfo"

                    # Build MCP output
                    $outputParts = @($statusLine, "")
                    
                    # Add captured output
                    $outputParts += $output

                    $mcpOutput = ($outputParts -join "`n")
                }
                catch {
                    $errorMessage = "Command execution failed: $($_.Exception.Message)"
                    Write-Host $errorMessage -ForegroundColor Red
                    $mcpOutput = $errorMessage
                }
                finally {
                    # Ensure NotifyResultReady is always called
                    if ($null -eq $mcpOutput) {
                        $mcpOutput = "Command execution completed"
                    }
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($mcpOutput)
                }
            }

            # ===== Handle Silent Execute Command =====
            $silentCmd = [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent
            if ($silentCmd) {
                [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent = $null

                try {
                    # Execute with silent capture (no console display)
                    # Use *>&1 to merge all streams, but don't display to console
                    $output = @()
                    $output = & {
                        Invoke-Expression $silentCmd
                    } *>&1
                    
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }
                    
                    $locationInfo = "Location: $($currentLocation.currentPath) [$($currentLocation.provider)]"

                    if ($output -and $output.Count -gt 0) {
                        $outputText = ($output | ForEach-Object { $_.ToString() }) -join "`n"
                        $text = $locationInfo + "`n" + $outputText.Trim()
                    } else {
                        $text = $locationInfo
                    }
                    
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($text)
                }
                catch {
                    $errorMessage = "Silent command execution failed: $($_.Exception.Message)"
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($errorMessage)
                }
            }
        }

    $global:McpTimer.Start()
}



