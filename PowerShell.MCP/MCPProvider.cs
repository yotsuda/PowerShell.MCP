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
                    [Console]::WriteLine()  # 確実に改行 + プロンプト
                    try {
                        $promptText = & { prompt }
                        $cleanPrompt = $promptText.TrimEnd(' ').TrimEnd('>')
                        [Console]::Write(""${cleanPrompt}> "")
                    }
                    catch {
                        [Console]::Write(""PS $((Get-Location).Path)> "")
                    }

                    # 複数行コマンドの判定
                    $isMultiLine = $cmd.Contains(""`n"") -or $cmd.Contains(""`r"")
                    if ($isMultiLine) {
                        # 複数行コマンド: 常に改行
                        [Console]::WriteLine()  # 確実に改行
                    }

                    Write-Host $cmd

                    # メインの実行処理
                    $ErrorActionPreference = 'Continue'
                    $WarningPreference = 'Continue'

                    $results = @()
                    $mcpErrors = @()
                    $mcpWarnings = @()
                    $mcpInformation = @()

                    # コマンド実行
                    Invoke-Expression $cmd -ErrorVariable mcpErrors -WarningVariable mcpWarnings -InformationVariable mcpInformation -OutVariable results

                    # コンソール出力
                    $results | Out-Default
                    if ($mcpErrors) { $mcpErrors | ForEach-Object { Write-Error $_ } }
                    if ($mcpWarnings) { $mcpWarnings | ForEach-Object { Write-Warning $_ } }

                    # 構造化出力の生成
                    $allResults = @()
                    $allResults += $results
                    foreach($err in $mcpErrors) { $allResults += $err }
                    foreach($warn in $mcpWarnings) { $allResults += $warn }
                    foreach($info in $mcpInformation) { $allResults += $info }

                    $outputStreams = @{
                        Success = @()
                        Error = @()
                        Warning = @()
                        Verbose = @()
                        Debug = @()
                        Information = @()
                    }

                    foreach ($item in $allResults) {
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
                        Success = ($outputStreams.Success | Out-String).Trim()
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
