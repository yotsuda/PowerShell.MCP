# MCPPollingEngine.ps1

# ===== Main Timer Setup =====

if (-not (Test-Path Variable:global:McpTimer)) {
    $global:McpTimer = New-Object System.Timers.Timer 100
    $global:McpTimer.AutoReset = $true

    Register-ObjectEvent `
        -InputObject    $global:McpTimer `
        -EventName      Elapsed `
        -SourceIdentifier MCP_Poll `
        -Action {
            
            # ===== Helper Functions (Defined within Action Block) =====
            
            function Invoke-CommandWithAllStreams {
                param([string]$Command)
                
                # Initialize output variables
                $outVar = @()
                $errorVar = @()
                $warningVar = @()
                $informationVar = @()
                $verboseVar = @()
                $debugVar = @()
                
                # Save current preference settings
                $originalVerbose = $VerbosePreference
                $originalDebug = $DebugPreference
                $originalInformation = $InformationPreference
                
                try {
                    # Enable all streams
                    $VerbosePreference = 'Continue'
                    $DebugPreference = 'Continue' 
                    $InformationPreference = 'Continue'
                    
                    # Use PowerShell native stream variables with redirection
                    $redirectedOutput = Invoke-Expression $Command `
                        -OutVariable outVar `
                        -ErrorVariable errorVar `
                        -WarningVariable warningVar `
                        -InformationVariable informationVar `
                        4>&1 5>&1  # Redirect Verbose(4) and Debug(5) to Success stream
                    
                    # Separate Verbose/Debug records from redirected output
                    if ($redirectedOutput) {
                        $redirectedOutput | ForEach-Object {
                            if ($_ -is [System.Management.Automation.VerboseRecord]) {
                                $verboseVar += $_
                            }
                            elseif ($_ -is [System.Management.Automation.DebugRecord]) {
                                $debugVar += $_
                            }
                        }
                    }
                    
                    return @{
                        Success = $outVar
                        Error = $errorVar
                        Warning = $warningVar
                        Information = $informationVar
                        Verbose = $verboseVar
                        Debug = $debugVar
                    }
                }
                finally {
                    # Restore preference settings
                    $VerbosePreference = $originalVerbose
                    $DebugPreference = $originalDebug
                    $InformationPreference = $originalInformation
                }
            }

            function Format-McpOutput {
                param(
                    [hashtable]$StreamResults,
                    [string]$LocationInfo
                )
                
                # Process each stream type
                $outputStreams = @{
                    Success = @()
                    Error = @()
                    Warning = @()
                    Information = @()
                    Host = @()
                }

                # Process errors
                foreach($err in $StreamResults.Error) { 
                    if ($err -is [System.Management.Automation.ErrorRecord]) {
                        $outputStreams.Error += $err.Exception.Message
                    } else {
                        $outputStreams.Error += $err.ToString()
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
                        $outputStreams.Information += $info.MessageData
                    } else {
                        $outputStreams.Information += $info.ToString()
                    }
                }
                
                # Process verbose
                foreach($verbose in $StreamResults.Verbose) { 
                    if ($verbose -is [System.Management.Automation.VerboseRecord]) {
                        $outputStreams.Host += "VERBOSE: $($verbose.Message)"
                    } else {
                        $outputStreams.Host += "VERBOSE: $verbose"
                    }
                }
                
                # Process debug
                foreach($debug in $StreamResults.Debug) { 
                    if ($debug -is [System.Management.Automation.DebugRecord]) {
                        $outputStreams.Host += "DEBUG: $($debug.Message)"
                    } else {
                        $outputStreams.Host += "DEBUG: $debug"
                    }
                }

                # Process success
                foreach ($item in $StreamResults.Success) {
                    $outputStreams.Success += $item
                }

                # Generate structured output strings
                $structuredOutput = @{
                    Success = ($outputStreams.Success | Out-String).Trim()
                    Error = ($outputStreams.Error -join "`n").Trim()
                    Warning = ($outputStreams.Warning -join "`n").Trim()
                    Information = ($outputStreams.Information -join "`n").Trim()
                    Host = ($outputStreams.Host -join "`n").Trim()
                }

                # Remove empty outputs
                $cleanOutput = @{}
                foreach ($key in $structuredOutput.Keys) {
                    if (-not [string]::IsNullOrEmpty($structuredOutput[$key])) {
                        $cleanOutput[$key] = $structuredOutput[$key]
                    }
                }

                # Generate formatted output
                if ($cleanOutput.Count -gt 1 -or ($cleanOutput.Keys -notcontains 'Success') -or ($cleanOutput.Error -or $cleanOutput.Warning -or $cleanOutput.Information -or $cleanOutput.Host)) {
                    $formattedOutput = @()
                    
                    # Always start with location info
                    $formattedOutput += $LocationInfo

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

                    if ($cleanOutput.Information) {
                        $formattedOutput += "=== INFO ==="
                        $formattedOutput += $cleanOutput.Information
                        $formattedOutput += ""
                    }

                    if ($cleanOutput.Host) {
                        $formattedOutput += "=== HOST ==="
                        $formattedOutput += $cleanOutput.Host
                        $formattedOutput += ""
                    }

                    $formattedOutput += "=== OUTPUT ==="
                    if ($cleanOutput.Success) {
                        $formattedOutput += $cleanOutput.Success
                    }

                    return ($formattedOutput -join "`n").Trim()
                } else {
                    # Simple output: location info + results only
                    if ($cleanOutput.Success) {
                        return $LocationInfo + "`n" + $cleanOutput.Success
                    } else {
                        return $LocationInfo
                    }
                }
            }

            # ===== Main Event Processing =====
            
            # Handle insert command
            $cmd = [PowerShell.MCP.Services.McpServerHost]::insertCommand
            if ($cmd) {
                [PowerShell.MCP.Services.McpServerHost]::insertCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)
                [Microsoft.PowerShell.PSConsoleReadLine]::DeleteLine()
                [Microsoft.PowerShell.PSConsoleReadLine]::Insert($cmd)
                
                [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady("Your pipeline has been inserted into the PS console.")

            }

            # Handle execute command
            $cmd = [PowerShell.MCP.Services.McpServerHost]::executeCommand
            if ($cmd) {
                [PowerShell.MCP.Services.McpServerHost]::executeCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)

                try {
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

                    Write-Host $cmd

                    # Execute command with stream capture
                    $streamResults = Invoke-CommandWithAllStreams -Command $cmd
                    
                    # Display results in console
                    $streamResults.Success | Out-Default

                    # Get current location info
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }
                    
                    $locationInfo = "Your pipeline executed in: $($currentLocation.currentPath) [$($currentLocation.provider)]"

                    # Generate MCP formatted output
                    $mcpOutput = Format-McpOutput -StreamResults $streamResults -LocationInfo $locationInfo
                    
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($mcpOutput)
                }
                catch {
                    $errorMessage = "Command execution failed: $($_.Exception.Message)"
                    Write-Host $errorMessage -ForegroundColor Red
                    
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($errorMessage)
                }
            }

            # Handle silent execute command
            $silentCmd = [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent
            if ($silentCmd) {
                [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent = $null

                try {
                    $results = Invoke-Expression $silentCmd
                    
                    # Get current location info
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }

                    $locationInfo = "Current Location: $($currentLocation.currentPath) [$($currentLocation.provider)]"

                    if ($results -ne $null) {
                        $output = $results | Out-String
                        $combinedOutput = $locationInfo + "`n" + $output.Trim()
                        [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($combinedOutput)
                    } else {
                        [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($locationInfo)
                    }
                }
                catch {
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }

                    $locationInfo = "Current Location: $($currentLocation.currentPath) [$($currentLocation.provider)]"
                    $errorMessage = "Error: $($_.Exception.Message)"
                    
                    [PowerShell.MCP.Services.PowerShellCommunication]::NotifyResultReady($locationInfo + "`n" + $errorMessage)
                }
            }

            # Notification system (future support)
            # Most MCP clients do not currently support MCP protocol notifications
            # Skip the following notification processing
            return

            # Command execution notification system
            try {
                # Track last checked history position
                if (-not $global:MCP_LastHistoryId) {
                    $global:MCP_LastHistoryId = 0
                }

                # Check interactive command history
                $history = Get-History -ErrorAction SilentlyContinue
                if ($history -and $history.Count -gt $global:MCP_LastHistoryId) {
                    $newCommands = $history | Where-Object { $_.Id -gt $global:MCP_LastHistoryId }
                    
                    foreach ($historyItem in $newCommands) {
                        $command = $historyItem.CommandLine
                        $duration = if ($historyItem.EndExecutionTime -and $historyItem.StartExecutionTime) {
                            ($historyItem.EndExecutionTime - $historyItem.StartExecutionTime).TotalMilliseconds
                        } else { 0 }
                        
                        # Basic filtering
                        $excludePatterns = @('cls', 'clear', 'exit', 'pwd')
                        $shouldExclude = $excludePatterns | Where-Object { $command -match "^$_\s*$" }
                        
                        if (-not $shouldExclude) {
                            # MCP notification: Interactive command executed
                            try {
                                [PowerShell.MCP.Services.McpServerHost]::SendCommandExecuted(
                                    $command,
                                    $PWD.Path,
                                    $LASTEXITCODE,
                                    [long]$duration
                                )
                            }
                            catch {
                                # Ignore notification errors
                            }
                        }
                    }
                    
                    $global:MCP_LastHistoryId = $history[-1].Id
                }
            }
            catch {
                # Ignore command notification errors
                Add-Content -Path "$env:TEMP\mcp-errors.log" -Value "$(Get-Date): Command notification error: $_" -ErrorAction SilentlyContinue
            }

            # Location change notification
            try {
                # Check for location changes
                $currentLocation = $PWD.Path
                if ($global:MCP_LastLocation -ne $currentLocation) {
                    $oldLocation = if ($global:MCP_LastLocation) { $global:MCP_LastLocation } else { "(initial)" }
                    
                    # MCP notification: Location changed
                    try {
                        [PowerShell.MCP.Services.McpServerHost]::SendLocationChanged($oldLocation, $currentLocation)
                    }
                    catch {
                        # Ignore notification errors
                    }
                    
                    $global:MCP_LastLocation = $currentLocation
                }
            }
            catch {
                # Ignore location change notification errors
                Add-Content -Path "$env:TEMP\mcp-errors.log" -Value "$(Get-Date): Location notification error: $_" -ErrorAction SilentlyContinue
            }

        } | Out-Null
    $global:McpTimer.Start()
}
