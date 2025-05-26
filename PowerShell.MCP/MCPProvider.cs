using System.Management.Automation;
using System.Management.Automation.Provider;

namespace PowerShell.MCP
{
    [CmdletProvider("PowerShell.MCP", ProviderCapabilities.None)]
    public class MCPProvider : CmdletProvider
    {
        public static readonly string url = "http://localhost:8086/";
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
            $cmd = [PowerShell.MCP.MCPServerHost]::insertCommand
            if ($cmd) {
                [PowerShell.MCP.MCPServerHost]::insertCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)
                [Microsoft.PowerShell.PSConsoleReadLine]::DeleteLine()
                [Microsoft.PowerShell.PSConsoleReadLine]::Insert($cmd)
            }
            
            $cmd = [PowerShell.MCP.MCPServerHost]::executeCommand
            if ($cmd) {
                [PowerShell.MCP.MCPServerHost]::executeCommand = $null
                [Microsoft.PowerShell.PSConsoleReadLine]::AddToHistory($cmd)
                
                try {
                    $cursorX = $Host.UI.RawUI.CursorPosition.X
                    if ($cursorX -eq 0) {
                        try {
                            $promptText = & { prompt }
                            $cleanPrompt = $promptText.TrimEnd(' ').TrimEnd('>')
                            [Console]::Write(""${cleanPrompt}> ${cmd}"")
                        }
                        catch {
                            [Console]::Write(""PS $((Get-Location).Path)> ${cmd}"")
                        }
                        [Console]::WriteLine()
                    } else {
                        Write-Host $cmd
                    }
                    
                    $results = @()
                    
                    Invoke-Expression ""$cmd *>&1"" | Tee-Object -Variable results | Out-Default
                    
                    $outputStreams = @{
                        Success = @()
                        Error = @()
                        Warning = @()
                        Verbose = @()
                        Debug = @()
                        Information = @()
                    }
                    
                    foreach ($item in $results) {
                        switch ($item.GetType().Name) {
                            'ErrorRecord' {
                                $outputStreams.Error += $item.Exception.Message
                            }
                            'WarningRecord' {
                                $outputStreams.Warning += $item.Message
                            }
                            'VerboseRecord' {
                                $outputStreams.Verbose += $item.Message
                            }
                            'DebugRecord' {
                                $outputStreams.Debug += $item.Message
                            }
                            'InformationRecord' {
                                $outputStreams.Information += $item.MessageData
                            }
                            default {
                                $outputStreams.Success += $item
                            }
                        }
                    }
                    
                    $structuredOutput = @{
                        Success = ($outputStreams.Success | Out-String -Width 800).Trim()
                        Error = ($outputStreams.Error -join ""`n"").Trim()
                        Warning = ($outputStreams.Warning -join ""`n"").Trim()
                        Verbose = ($outputStreams.Verbose -join ""`n"").Trim()
                        Debug = ($outputStreams.Debug -join ""`n"").Trim()
                        Information = ($outputStreams.Information -join ""`n"").Trim()
                    }
                    
                    $cleanOutput = @{}
                    foreach ($key in $structuredOutput.Keys) {
                        if (-not [string]::IsNullOrEmpty($structuredOutput[$key])) {
                            $cleanOutput[$key] = $structuredOutput[$key]
                        }
                    }
                    
                    if ($cleanOutput.Count -gt 1 -or ($cleanOutput.Keys -notcontains 'Success')) {
                        $formattedOutput = @()
                        
                        if ($cleanOutput.Success) {
                            $formattedOutput += ""=== OUTPUT ===""
                            $formattedOutput += $cleanOutput.Success
                            $formattedOutput += """"
                        }
                        
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
                        
                        if ($cleanOutput.Verbose) {
                            $formattedOutput += ""=== VERBOSE ===""
                            $formattedOutput += $cleanOutput.Verbose
                            $formattedOutput += """"
                        }
                        
                        if ($cleanOutput.Debug) {
                            $formattedOutput += ""=== DEBUG ===""
                            $formattedOutput += $cleanOutput.Debug
                            $formattedOutput += """"
                        }
                        
                        if ($cleanOutput.Information) {
                            $formattedOutput += ""=== INFO ===""
                            $formattedOutput += $cleanOutput.Information
                        }
                        
                        $text = ($formattedOutput -join ""`n"").Trim()
                        if ($text.Length -gt 800) {
                            $text = $text.Substring(0, 800)
                        }
                        [PowerShell.MCP.MCPServerHost]::outputFromCommand = $text
                    } else {
                        $text = if ($cleanOutput.Success) { $cleanOutput.Success } else { """" }
                        if ($text.Length -gt 800) {
                            $text = $text.Substring(0, 800)
                        }
                        [PowerShell.MCP.MCPServerHost]::outputFromCommand = $text
                    }
                }
                catch {
                    $errorMessage = ""Command execution failed: $($_.Exception.Message)""
                    Write-Host $errorMessage -ForegroundColor Red
                    [PowerShell.MCP.MCPServerHost]::outputFromCommand = $errorMessage
                }
            }
        } | Out-Null
    $global:McpTimer.Start()
}
");
            Task.Run(() =>
            {
                McpServerHost.StartServer(this, url, _tokenSource.Token);
            }, _tokenSource.Token);

            WriteInformation(
                "[PowerShell.MCP] MCP server started at http://localhost:8086/",
                ["PowerShell.MCP", "ServerStart"]
            );
            return pi;
        }

        protected override void Stop()
        {
            try
            {
                McpServerHost.StopServer();

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
