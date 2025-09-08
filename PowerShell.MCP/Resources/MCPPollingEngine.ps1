if (-not (Test-Path Variable:global:McpTimer)) {
    $global:McpTimer = New-Object System.Timers.Timer 500
    $global:McpTimer.AutoReset = $true

    Register-ObjectEvent `
        -InputObject    $global:McpTimer `
        -EventName      Elapsed `
        -SourceIdentifier MCP_Poll `
        -Action {
            $cmd = [PowerShell.MCP.Services.McpServerHost]::insertCommand
            if ($cmd) {
                [PowerShell.MCP.Services.McpServerHost]::insertCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)
                [Microsoft.PowerShell.PSConsoleReadLine]::DeleteLine()
                [Microsoft.PowerShell.PSConsoleReadLine]::Insert($cmd)
                [PowerShell.MCP.Services.McpServerHost]::outputFromCommand = "Your pipeline has been inserted into the PS console."
            }

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
                    }

                    Write-Host $cmd

                    $results = @()
                    $mcpErrors = @()
                    $mcpWarnings = @()
                    $mcpInformation = @()
                    $hostOutput = @()

                    $tempFile = [System.IO.Path]::GetTempFileName()

                    try {
                        Start-Transcript -Path $tempFile -Append | Out-Null
                        
                        Invoke-Expression $cmd `
                            -ErrorVariable mcpErrors `
                            -WarningVariable mcpWarnings `
                            -InformationVariable mcpInformation `
                            -OutVariable results
                        
                        Stop-Transcript | Out-Null
                        
                        $transcriptContent = Get-Content $tempFile -ErrorAction SilentlyContinue
                        $hostOutput = $transcriptContent | Where-Object { $_ -match '^(What if:|VERBOSE:|DEBUG:|WARNING:)' }
                    }
                    catch {
                        Invoke-Expression $cmd `
                            -ErrorVariable mcpErrors `
                            -WarningVariable mcpWarnings `
                            -InformationVariable mcpInformation `
                            -OutVariable results
                    }
                    finally {
                        if (Test-Path $tempFile) {
                            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
                        }
                    }

                    $results | Out-Default

                    # Add current location info at the beginning of results
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }
                    
                    $locationInfo = "Current Location: $($currentLocation.currentPath) [$($currentLocation.provider)]"

                    $allResults = @()
                    $allResults += $locationInfo  # Add location info first
                    $allResults += $results
                    foreach($err in $mcpErrors) { $allResults += $err }
                    foreach($warn in $mcpWarnings) { $allResults += $warn }
                    foreach($info in $mcpInformation) { $allResults += $info }
                    foreach($host in $hostOutput) { $allResults += $host }

                    $hasHostOutput = $hostOutput.Count -gt 0

                    $outputStreams = @{
                        Success = @()
                        Error = @()
                        Warning = @()
                        Information = @()
                        Host = @()
                    }

                    foreach ($item in $allResults) {
                        switch ($item.GetType().Name) {
                            'ErrorRecord' {
                                $outputStreams.Error += $item.Exception.Message
                            }
                            'WarningRecord' {
                                $outputStreams.Warning += $item.Message
                            }
                            'InformationRecord' {
                                $outputStreams.Information += $item.MessageData
                            }
                            'String' {
                                if ($item -in $hostOutput) {
                                    $outputStreams.Host += $item
                                } else {
                                    $outputStreams.Success += $item
                                }
                            }
                            default {
                                $outputStreams.Success += $item
                            }
                        }
                    }

                    $structuredOutput = @{
                        Success = ($outputStreams.Success | Out-String).Trim()
                        Error = ($outputStreams.Error -join "`n").Trim()
                        Warning = ($outputStreams.Warning -join "`n").Trim()
                        Information = ($outputStreams.Information -join "`n").Trim()
                        Host = ($outputStreams.Host -join "`n").Trim()
                    }

                    $cleanOutput = @{}
                    foreach ($key in $structuredOutput.Keys) {
                        if (-not [string]::IsNullOrEmpty($structuredOutput[$key])) {
                            $cleanOutput[$key] = $structuredOutput[$key]
                        }
                    }

                    if ($cleanOutput.Count -gt 1 -or ($cleanOutput.Keys -notcontains 'Success')) {
                        $formattedOutput = @()

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
                        }

                        if ($cleanOutput.Host) {
                            $formattedOutput += "=== HOST ==="
                            $formattedOutput += $cleanOutput.Host
                            $formattedOutput += ""
                        }

                        if ($cleanOutput.Success) {
                            $formattedOutput += "=== OUTPUT ==="
                            $formattedOutput += $cleanOutput.Success
                            $formattedOutput += ""
                        }

                        $text = ($formattedOutput -join "`n").Trim()
                        [PowerShell.MCP.Services.McpServerHost]::outputFromCommand = $text
                    } else {
                        $text = if ($cleanOutput.Success) { $cleanOutput.Success } else { "" }
                        [PowerShell.MCP.Services.McpServerHost]::outputFromCommand = $text
                    }
                }
                catch {
                    $errorMessage = "Command execution failed: $($_.Exception.Message)"
                    Write-Host $errorMessage -ForegroundColor Red
                    [PowerShell.MCP.Services.McpServerHost]::outputFromCommand = $errorMessage
                }
            }

            $silentCmd = [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent
            if ($silentCmd) {
                [PowerShell.MCP.Services.McpServerHost]::executeCommandSilent = $null

                try {
                    $results = Invoke-Expression $silentCmd
                    
                    # Add current location info for silent commands too
                    $currentLocation = @{
                        drive = (Get-Location).Drive.Name + ":"
                        currentPath = (Get-Location).Path
                        provider = (Get-Location).Provider.Name
                    }

                    $locationInfo = "Current Location: $($currentLocation.currentPath) [$($currentLocation.provider)]"

                    if ($results -ne $null) {
                        $output = $results | Out-String
                        $combinedOutput = $locationInfo + "`n" + $output.Trim()
                        [PowerShell.MCP.Services.McpServerHost]::outputFromCommand = $combinedOutput
                    } else {
                        [PowerShell.MCP.Services.McpServerHost]::outputFromCommand = $locationInfo
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
                    [PowerShell.MCP.Services.McpServerHost]::outputFromCommand = $locationInfo + "`n" + $errorMessage
                }
            }

            # === コマンド実行通知システム ===
            try {
                # 前回チェックした履歴の位置を追跡
                if (-not $global:MCP_LastHistoryId) {
                    $global:MCP_LastHistoryId = 0
                }

                # 対話的コマンド履歴をチェック
                $history = Get-History -ErrorAction SilentlyContinue
                if ($history -and $history.Count -gt $global:MCP_LastHistoryId) {
                    $newCommands = $history | Where-Object { $_.Id -gt $global:MCP_LastHistoryId }
                    
                    foreach ($historyItem in $newCommands) {
                        $command = $historyItem.CommandLine
                        $duration = if ($historyItem.EndExecutionTime -and $historyItem.StartExecutionTime) {
                            ($historyItem.EndExecutionTime - $historyItem.StartExecutionTime).TotalMilliseconds
                        } else { 0 }
                        
                        # 基本的なフィルタリング（調整済み）
                        $excludePatterns = @('cls', 'clear', 'exit', 'pwd')
                        $shouldExclude = $excludePatterns | Where-Object { $command -match "^$_\s*$" }
                        
                        if (-not $shouldExclude) {
                            # === MCP通知: 対話的コマンド実行 ===
                            try {
                                [PowerShell.MCP.Services.McpServerHost]::SendCommandExecuted(
                                    $command,
                                    $PWD.Path,
                                    $LASTEXITCODE,
                                    [long]$duration
                                )
                                
                                $icon = if ($duration -gt 3000) { "⏳" } else { "⚡" }
                                $durationText = if ($duration -gt 0) { " ($([math]::Round($duration))ms)" } else { "" }
#                                Write-Host "$icon Interactive command: $command$durationText" -ForegroundColor Green
                            }
                            catch {
                                # 通知エラーは無視（従来のログも残す）
#                                Add-Content -Path "$env:TEMP\mcp-notifications.log" -Value "$(Get-Date): Interactive command: $command" -ErrorAction SilentlyContinue
                            }
                        }
                    }
                    
                    $global:MCP_LastHistoryId = $history[-1].Id
                }
            }
            catch {
                # コマンド通知エラーは無視
                Add-Content -Path "$env:TEMP\mcp-errors.log" -Value "$(Get-Date): Command notification error: $_" -ErrorAction SilentlyContinue
            }

            # === 位置変更通知 ===
            try {
                # 初期化（意図的にコメントアウトして初回通知を有効化）
                #if (-not $global:MCP_LastLocation) {
                #    $global:MCP_LastLocation = $PWD.Path
                #}

                # 位置変更チェック
                $currentLocation = $PWD.Path
                if ($global:MCP_LastLocation -ne $currentLocation) {
                    $oldLocation = if ($global:MCP_LastLocation) { $global:MCP_LastLocation } else { "(initial)" }
                    
                    # === MCP通知: 位置変更 ===
                    try {
                        [PowerShell.MCP.Services.McpServerHost]::SendLocationChanged($oldLocation, $currentLocation)
#                        Write-Host "📍 Location changed: $oldLocation → $currentLocation" -ForegroundColor Cyan
                    }
                    catch {
                        # 通知エラーは無視（従来のログも残す）
#                        Add-Content -Path "$env:TEMP\mcp-notifications.log" -Value "$(Get-Date): Location changed: $oldLocation → $currentLocation" -ErrorAction SilentlyContinue
                    }
                    
                    $global:MCP_LastLocation = $currentLocation
                }
            }
            catch {
                # 位置変更通知エラーは無視
                Add-Content -Path "$env:TEMP\mcp-errors.log" -Value "$(Get-Date): Location notification error: $_" -ErrorAction SilentlyContinue
            }

        } | Out-Null
    $global:McpTimer.Start()
}
