# Behavioural cover for the console's command echo: the AI's command must
# reach a user's Start-Transcript, as one plain line, above the output it
# produced. The echo used to be written with [Console]::Write, which bypasses
# the host UI where transcription taps, so a transcript held every command's
# output and not one command line.
#
# The echo function is lifted out of the embedded polling engine by AST and
# run standalone — no console, no pipe, no proxy — so these assertions are
# about the echo itself and nothing else.

Describe "Command echo reaches Start-Transcript" {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
        $enginePath = Join-Path $repoRoot 'PowerShell.MCP' 'Resources' 'MCPPollingEngine.ps1'

        $parseErrors = $null
        $engineAst = [System.Management.Automation.Language.Parser]::ParseFile(
            $enginePath, [ref]$null, [ref]$parseErrors)
        if ($parseErrors) { throw "MCPPollingEngine.ps1 has $($parseErrors.Count) parse error(s)." }

        $echoAst = $engineAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Write-ColoredCommand'
        }, $true) | Select-Object -First 1

        if (-not $echoAst) { throw 'Write-ColoredCommand was not found in the polling engine.' }

        # Dot-sourced into this BeforeAll scope, which the It blocks see and
        # which ends with the block (a global definition would outlive the run).
        # Its $script:cachedPSReadLineOptions is left unset on purpose: the
        # function falls back to its own default palette, which keeps the
        # assertions independent of the machine's PSReadLine theme.
        . ([scriptblock]::Create($echoAst.Extent.Text))

        $script:Ansi = "`e\[[0-9;]*m"
        $script:Command = "Get-ChildItem -Path 'C:\temp' -Filter *.log | Select-Object -First 3"
    }


    Context "as a single host write" {
        It "emits the whole command in one record, not one per token" {
            # Transcription writes one record per call, so a per-token echo
            # would land in the transcript shredded across one line per token.
            $records = @(Write-ColoredCommand $script:Command 6>&1)

            $records.Count | Should -Be 1
        }

        It "keeps the command text intact once the colouring is stripped" {
            $records = @(Write-ColoredCommand $script:Command 6>&1)
            $text = ($records[0].MessageData.Message -replace $script:Ansi, '').TrimEnd()

            $text | Should -BeExactly $script:Command
        }

        It "keeps a multi-line command in one record" {
            $multi = "foreach (`$n in 1..3) {`n    `$n * 2`n}"
            $records = @(Write-ColoredCommand $multi 6>&1)

            $records.Count | Should -Be 1
        }

        It "still emits a command that cannot be tokenized" {
            # The catch fallback: a command the parser chokes on must still be
            # echoed, or the console would run something it never showed.
            $broken = "Get-Process | Where-Object { `$_.Name -eq 'pwsh'"
            $records = @(Write-ColoredCommand $broken 6>&1)

            $records.Count | Should -Be 1
            ($records[0].MessageData.Message -replace $script:Ansi, '').TrimEnd() |
                Should -BeExactly $broken
        }
    }

    Context "in a real transcript" {
        It "appears as one plain, greppable line" {
            $file = Join-Path ([System.IO.Path]::GetTempPath()) "mcp-echo-transcript-$(Get-Random).txt"

            try {
                Start-Transcript -Path $file -Force -ErrorAction Stop | Out-Null
            }
            catch {
                Set-ItResult -Skipped -Because 'this host does not support transcription, or one is already running'
                return
            }

            try {
                Write-ColoredCommand $script:Command
            }
            finally {
                Stop-Transcript | Out-Null
            }

            try {
                $lines = @(Get-Content -LiteralPath $file)

                # Exact match, not -Match: transcription strips the ANSI, so
                # the command must be in the log as plain text someone can grep.
                @($lines | Where-Object { $_ -eq $script:Command }).Count | Should -Be 1
            }
            finally {
                Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
