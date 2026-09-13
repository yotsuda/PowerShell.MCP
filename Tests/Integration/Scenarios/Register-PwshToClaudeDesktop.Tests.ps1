# Register-PwshToClaudeDesktop edits claude_desktop_config.json in place. It
# could always repoint the command, but it used to assign a fresh
# @{ command = ... } over the whole entry, which dropped the "args" and "env"
# the user had configured on it (proxy flags, POWERSHELL_MCP_TIMEOUT_CEILING).
#
# %APPDATA% and %LOCALAPPDATA% are redirected to a temp directory for the
# duration, so these never read or write the real Claude Desktop config.

Describe "Register-PwshToClaudeDesktop" {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
        $psm1 = Join-Path $repoRoot 'Staging' 'PowerShell.MCP.psm1'

        $parseErrors = $null
        $moduleAst = [System.Management.Automation.Language.Parser]::ParseFile(
            $psm1, [ref]$null, [ref]$parseErrors)
        if ($parseErrors) { throw "PowerShell.MCP.psm1 has $($parseErrors.Count) parse error(s)." }

        $functionAst = $moduleAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Register-PwshToClaudeDesktop'
        }, $true) | Select-Object -First 1

        if (-not $functionAst) { throw 'Register-PwshToClaudeDesktop was not found in the module.' }

        . ([scriptblock]::Create($functionAst.Extent.Text))
        function Get-MCPProxyPath { 'C:\NEW\PowerShell.MCP.Proxy.exe' }

        # Helpers are defined here in BeforeAll. One defined in the Describe body
        # is created at discovery time and is gone by the time an It runs, and a
        # global one would outlive the run and shadow the module's own commands
        # in the console that ran the tests.
        function Set-Config {
            param([hashtable]$Config)
            New-Item -ItemType Directory -Path (Split-Path $global:DesktopConfigPath -Parent) -Force | Out-Null
            $Config | ConvertTo-Json -Depth 10 |
                Set-Content -LiteralPath $global:DesktopConfigPath -Encoding UTF8 -NoNewline
        }
        function Get-Config {
            Get-Content -LiteralPath $global:DesktopConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json -AsHashtable
        }

        $script:RealAppData = $env:APPDATA
        $script:RealLocalAppData = $env:LOCALAPPDATA
    }

    AfterAll {
        $env:APPDATA = $script:RealAppData
        $env:LOCALAPPDATA = $script:RealLocalAppData

        Remove-Variable -Name DesktopConfigPath -Scope Global -ErrorAction SilentlyContinue
    }

    BeforeEach {
        $script:sandbox = Join-Path ([System.IO.Path]::GetTempPath()) "mcp-desktop-$(Get-Random)"
        New-Item -ItemType Directory -Path $script:sandbox | Out-Null

        # Both are redirected: the Windows branch prefers an MSIX install under
        # LOCALAPPDATA\Packages\Claude_*, and a developer with the Store build
        # would otherwise have the test write to their real config.
        $env:APPDATA = $script:sandbox
        $env:LOCALAPPDATA = $script:sandbox

        $global:DesktopConfigPath = Join-Path $script:sandbox 'Claude' 'claude_desktop_config.json'
    }

    AfterEach {
        $env:APPDATA = $script:RealAppData
        $env:LOCALAPPDATA = $script:RealLocalAppData
        Remove-Item -LiteralPath $script:sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }

    Context "when there is no config file yet" {
        It "creates one with the entry" {
            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).mcpServers.pwsh.command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }
    }

    Context "when the entry was left by an older install" {
        BeforeEach {
            Set-Config @{
                mcpServers = @{
                    pwsh = @{ command = 'C:\OLD\v1.13.0\PowerShell.MCP.Proxy.exe' }
                }
            }
        }

        It "repoints it at the current proxy" {
            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).mcpServers.pwsh.command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }
    }

    Context "when the entry was configured" {
        BeforeEach {
            Set-Config @{
                mcpServers = @{
                    pwsh = @{
                        command = 'C:\OLD\PowerShell.MCP.Proxy.exe'
                        args    = @('--no-profile')
                        env     = @{ POWERSHELL_MCP_TIMEOUT_CEILING = '120' }
                    }
                }
            }
        }

        It "keeps the proxy flags" {
            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).mcpServers.pwsh.args | Should -Be @('--no-profile')
        }

        It "keeps the environment variables" {
            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).mcpServers.pwsh.env.POWERSHELL_MCP_TIMEOUT_CEILING | Should -BeExactly '120'
        }
    }

    Context "when the file holds other settings" {
        BeforeEach {
            Set-Config @{
                globalShortcut = 'Alt+Space'
                mcpServers     = @{
                    filesystem = @{ command = 'npx'; args = @('-y', 'server-filesystem') }
                }
            }
        }

        It "leaves other servers alone" {
            Register-PwshToClaudeDesktop 6>$null

            $config = Get-Config
            $config.mcpServers.filesystem.command | Should -BeExactly 'npx'
            $config.mcpServers.filesystem.args | Should -Be @('-y', 'server-filesystem')
        }

        It "leaves unrelated top-level settings alone" {
            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).globalShortcut | Should -BeExactly 'Alt+Space'
        }
    }

    Context "when a legacy PowerShell entry exists" {
        It "removes one that points at our proxy" {
            Set-Config @{
                mcpServers = @{ PowerShell = @{ command = 'C:\OLD\PowerShell.MCP.Proxy.exe' } }
            }

            Register-PwshToClaudeDesktop 6>$null

            $config = Get-Config
            $config.mcpServers.ContainsKey('PowerShell') | Should -BeFalse
            $config.mcpServers.pwsh.command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }

        It "leaves someone else's PowerShell server alone" {
            Set-Config @{
                mcpServers = @{ PowerShell = @{ command = 'C:\Other\some-other-server.exe' } }
            }

            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).mcpServers.PowerShell.command | Should -BeExactly 'C:\Other\some-other-server.exe'
        }
    }
}
