using System.Management.Automation;
using System.Management.Automation.Provider;

namespace PowerShell.MCP
{
    [CmdletProvider("PowerShell.MCP", ProviderCapabilities.None)]
    public class MCPProvider : CmdletProvider
    {
        private CancellationTokenSource? _tokenSource;

        protected override ProviderInfo Start(ProviderInfo providerInfo)
        {
            var pi = base.Start(providerInfo);

            _tokenSource = new CancellationTokenSource();

            this.InvokeCommand.InvokeScript(
@"if (-not (Test-Path Variable:global:McpTimer)) {
    $global:McpTimer = New-Object System.Timers.Timer 500
    $global:McpTimer.AutoReset = $true

    Register-ObjectEvent `
        -InputObject    $global:McpTimer `
        -EventName      Elapsed `
        -SourceIdentifier MCP_Poll `
        -Action {
            $cmd = [PowerShell.MCP.McpServerHost]::insertCommand
            if ($cmd) {
                [PowerShell.MCP.McpServerHost]::insertCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)
                [Microsoft.PowerShell.PSConsoleReadLine]::DeleteLine()
                [Microsoft.PowerShell.PSConsoleReadLine]::Insert($cmd)
            }

            $cmd = [PowerShell.MCP.McpServerHost]::executeCommand
            if ($cmd) {
                [PowerShell.MCP.McpServerHost]::executeCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)

                try {
                    [Console]::WriteLine()
                    try {
                        $promptText = & { prompt }
                        $cleanPrompt = $promptText.TrimEnd(' ').TrimEnd('>')
                        [Console]::Write(""${cleanPrompt}> "")
                    }
                    catch {
                        [Console]::Write(""PS $((Get-Location).Path)> "")
                    }

                    $isMultiLine = $cmd.Contains(""`n"") -or $cmd.Contains(""`r"")
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

                    $allResults = @()
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
                        Error = ($outputStreams.Error -join ""`n"").Trim()
                        Warning = ($outputStreams.Warning -join ""`n"").Trim()
                        Information = ($outputStreams.Information -join ""`n"").Trim()
                        Host = ($outputStreams.Host -join ""`n"").Trim()
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
                            $formattedOutput += ""=== ERRORS ===""
                            $formattedOutput += $cleanOutput.Error
                            $formattedOutput += """"
                        }

                        if ($cleanOutput.Warning) {
                            $formattedOutput += ""=== WARNINGS ===""
                            $formattedOutput += $cleanOutput.Warning
                            $formattedOutput += """"
                        }

                        if ($cleanOutput.Information) {
                            $formattedOutput += ""=== INFO ===""
                            $formattedOutput += $cleanOutput.Information
                        }

                        if ($cleanOutput.Host) {
                            $formattedOutput += ""=== HOST ===""
                            $formattedOutput += $cleanOutput.Host
                            $formattedOutput += """"
                        }

                        if ($cleanOutput.Success) {
                            $formattedOutput += ""=== OUTPUT ===""
                            $formattedOutput += $cleanOutput.Success
                            $formattedOutput += """"
                        }

                        $text = ($formattedOutput -join ""`n"").Trim()
                        [PowerShell.MCP.McpServerHost]::outputFromCommand = $text
                    } else {
                        $text = if ($cleanOutput.Success) { $cleanOutput.Success } else { """" }
                        [PowerShell.MCP.McpServerHost]::outputFromCommand = $text
                    }
                }
                catch {
                    $errorMessage = ""Command execution failed: $($_.Exception.Message)""
                    Write-Host $errorMessage -ForegroundColor Red
                    [PowerShell.MCP.McpServerHost]::outputFromCommand = $errorMessage
                }
            }
        } | Out-Null
    $global:McpTimer.Start()
}
");

            Task.Run(() =>
            {
                try
                {
                    McpServerHost.StartServer(this, _tokenSource.Token);
                }
                catch (Exception ex)
                {
                    WriteWarning($"[PowerShell.MCP] Failed to start Named Pipe server: {ex.Message}");
                }
            }, _tokenSource.Token);

            WriteInformation(
                "[PowerShell.MCP] MCP Named Pipe server started",
                ["PowerShell.MCP", "ServerStart"]
            );
            return pi;
        }

        protected override void Stop()
        {
            try
            {
                _tokenSource?.Cancel();
                _tokenSource?.Dispose();
                _tokenSource = null;
            }
            catch (Exception ex)
            {
                WriteWarning($"[PowerShell.MCP] Error during stop: {ex.Message}");
            }
            finally
            {
                base.Stop();
            }
        }
    }
}
