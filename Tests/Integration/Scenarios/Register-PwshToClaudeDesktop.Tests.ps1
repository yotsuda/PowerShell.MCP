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

        # The module-private helper that applies -ConsoleLaunch / -NoProfile.
        $helperAst = $moduleAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Set-ProxyArgument'
        }, $true) | Select-Object -First 1
        if (-not $helperAst) { throw 'Set-ProxyArgument was not found in the module.' }

        # The module-private helper that finds the config file.
        $pathAst = $moduleAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Get-ClaudeDesktopConfigPath'
        }, $true) | Select-Object -First 1
        if (-not $pathAst) { throw 'Get-ClaudeDesktopConfigPath was not found in the module.' }

        . ([scriptblock]::Create($functionAst.Extent.Text))
        . ([scriptblock]::Create($helperAst.Extent.Text))
        . ([scriptblock]::Create($pathAst.Extent.Text))
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

    Context "proxy flags given as parameters" {
        It "adds --console-launch to a new entry" {
            Register-PwshToClaudeDesktop -ConsoleLaunch Minimized 6>$null

            (Get-Config).mcpServers.pwsh.args | Should -Be @('--console-launch', 'minimized')
        }

        It "adds it to an existing entry and keeps the other flags and environment" {
            Set-Config @{
                mcpServers = @{
                    pwsh = @{ command = 'C:\OLD\PowerShell.MCP.Proxy.exe'; args = @('--no-profile'); env = @{ KEEP = '1' } }
                }
            }

            Register-PwshToClaudeDesktop -ConsoleLaunch Minimized 6>$null

            $entry = (Get-Config).mcpServers.pwsh
            $entry.args | Should -Be @('--no-profile', '--console-launch', 'minimized')
            $entry.env.KEEP | Should -BeExactly '1'
        }

        It "-ConsoleLaunch Auto removes the option, and an emptied args with it" {
            Set-Config @{
                mcpServers = @{ pwsh = @{ command = 'C:\OLD\PowerShell.MCP.Proxy.exe'; args = @('--console-launch=handoff') } }
            }

            Register-PwshToClaudeDesktop -ConsoleLaunch Auto 6>$null

            (Get-Config).mcpServers.pwsh.ContainsKey('args') | Should -BeFalse
        }

        It "-NoProfile adds --no-profile" {
            Register-PwshToClaudeDesktop -NoProfile 6>$null

            (Get-Config).mcpServers.pwsh.args | Should -Be @('--no-profile')
        }

        It "replaces a value already set, in either form" {
            Set-Config @{
                mcpServers = @{ pwsh = @{ command = 'C:\OLD\PowerShell.MCP.Proxy.exe'; args = @('--console-launch', 'handoff', '--console-launch=auto') } }
            }

            Register-PwshToClaudeDesktop -ConsoleLaunch Minimized 6>$null

            (Get-Config).mcpServers.pwsh.args | Should -Be @('--console-launch', 'minimized')
        }

        It "-NoProfile adds --no-profile once, and -NoProfile:`$false removes it and keeps the rest" {
            Set-Config @{
                mcpServers = @{ pwsh = @{ command = 'C:\OLD\PowerShell.MCP.Proxy.exe'; args = @('--no-profile', '--console-launch', 'minimized') } }
            }
            Register-PwshToClaudeDesktop -NoProfile 6>$null
            (Get-Config).mcpServers.pwsh.args | Should -Be @('--no-profile', '--console-launch', 'minimized')

            Register-PwshToClaudeDesktop -NoProfile:$false 6>$null
            (Get-Config).mcpServers.pwsh.args | Should -Be @('--console-launch', 'minimized')
        }

        It "rejects an unknown value and leaves the file alone" {
            Set-Config @{ mcpServers = @{ pwsh = @{ command = 'C:\OLD\PowerShell.MCP.Proxy.exe' } } }

            { Register-PwshToClaudeDesktop -ConsoleLaunch Hidden 6>$null } | Should -Throw

            (Get-Config).mcpServers.pwsh.command | Should -BeExactly 'C:\OLD\PowerShell.MCP.Proxy.exe'
        }

        It "a later plain re-registration keeps the option" {
            Register-PwshToClaudeDesktop -ConsoleLaunch Minimized 6>$null

            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).mcpServers.pwsh.args | Should -Be @('--console-launch', 'minimized')
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

    Context "when the file cannot be read as a config" {
        BeforeEach {
            New-Item -ItemType Directory -Path (Split-Path $global:DesktopConfigPath -Parent) -Force | Out-Null
        }

        It "leaves invalid JSON unchanged and reports an error" {
            Set-Content -LiteralPath $global:DesktopConfigPath -Value '{ "mcpServers": ' -NoNewline

            Register-PwshToClaudeDesktop -ErrorVariable failures -ErrorAction SilentlyContinue 6>$null

            Get-Content -LiteralPath $global:DesktopConfigPath -Raw | Should -BeExactly '{ "mcpServers": '
            ($failures | Out-String) | Should -Match 'left unchanged'
        }

        It "leaves a file whose mcpServers is not an object unchanged" {
            Set-Content -LiteralPath $global:DesktopConfigPath -Value '{ "mcpServers": [] }' -NoNewline

            Register-PwshToClaudeDesktop -ErrorAction SilentlyContinue 6>$null

            Get-Content -LiteralPath $global:DesktopConfigPath -Raw | Should -BeExactly '{ "mcpServers": [] }'
        }

        It "treats an empty file as an empty config" {
            Set-Content -LiteralPath $global:DesktopConfigPath -Value '' -NoNewline

            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).mcpServers.pwsh.command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }
    }

    Context "writing the file" {
        It "keeps settings nested deeper than ConvertTo-Json's default depth" {
            New-Item -ItemType Directory -Path (Split-Path $global:DesktopConfigPath -Parent) -Force | Out-Null
            Set-Content -LiteralPath $global:DesktopConfigPath -NoNewline -Value '{ "preferences": { "a": { "b": { "c": { "d": { "e": { "f": { "g": { "h": { "i": { "j": { "k": "deep" } } } } } } } } } } } }'

            Register-PwshToClaudeDesktop 6>$null

            (Get-Config).preferences.a.b.c.d.e.f.g.h.i.j.k | Should -BeExactly 'deep'
        }

        It "leaves no temporary file behind" {
            Register-PwshToClaudeDesktop 6>$null

            @(Get-ChildItem -LiteralPath (Split-Path $global:DesktopConfigPath -Parent) -Filter '*.tmp').Count | Should -Be 0
        }

        It "writes UTF-8 without a BOM" {
            Register-PwshToClaudeDesktop 6>$null

            [System.IO.File]::ReadAllBytes($global:DesktopConfigPath)[0] | Should -Be ([byte][char]'{')
        }
    }

    Context "-ConfigPath" {
        It "edits the file given instead of the one it would find" {
            $global:DesktopConfigPath = Join-Path $script:sandbox 'Somewhere' 'claude_desktop_config.json'

            Register-PwshToClaudeDesktop -ConfigPath $global:DesktopConfigPath 6>$null

            (Get-Config).mcpServers.pwsh.command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
            Test-Path (Join-Path $script:sandbox 'Claude' 'claude_desktop_config.json') | Should -BeFalse
        }
    }
}

# Where Claude Desktop reads the file. The Store version moved it from the
# package's LocalCache to %LOCALAPPDATA%\Claude-Data\claude_<id>\ in 3.1623,
# and the cmdlet kept writing to the old place (#60).
Describe "Get-ClaudeDesktopConfigPath" -Skip:(-not $IsWindows) {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
        $psm1 = Join-Path $repoRoot 'Staging' 'PowerShell.MCP.psm1'
        $moduleAst = [System.Management.Automation.Language.Parser]::ParseFile($psm1, [ref]$null, [ref]$null)
        $pathAst = $moduleAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Get-ClaudeDesktopConfigPath'
        }, $true) | Select-Object -First 1
        . ([scriptblock]::Create($pathAst.Extent.Text))

        function New-ConfigFile([string]$Path) {
            New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
            Set-Content -LiteralPath $Path -Value '{}' -NoNewline
        }

        $script:RealAppData = $env:APPDATA
        $script:RealLocalAppData = $env:LOCALAPPDATA
    }

    AfterAll {
        $env:APPDATA = $script:RealAppData
        $env:LOCALAPPDATA = $script:RealLocalAppData
    }

    BeforeEach {
        $script:sandbox = Join-Path ([System.IO.Path]::GetTempPath()) "mcp-desktop-path-$(Get-Random)"
        $env:APPDATA = Join-Path $script:sandbox 'Roaming'
        $env:LOCALAPPDATA = Join-Path $script:sandbox 'Local'
        New-Item -ItemType Directory -Path $env:APPDATA, $env:LOCALAPPDATA -Force | Out-Null

        $script:dataFile = Join-Path $env:LOCALAPPDATA 'Claude-Data' 'claude_pzs8sxrjxfjjc' 'claude_desktop_config.json'
        $script:localCacheFile = Join-Path $env:LOCALAPPDATA 'Packages' 'Claude_pzs8sxrjxfjjc' 'LocalCache' 'Roaming' 'Claude' 'claude_desktop_config.json'
        $script:appDataFile = Join-Path $env:APPDATA 'Claude' 'claude_desktop_config.json'
    }

    AfterEach {
        $env:APPDATA = $script:RealAppData
        $env:LOCALAPPDATA = $script:RealLocalAppData
        Remove-Item -LiteralPath $script:sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }

    It "prefers Claude-Data over the files older versions left behind" {
        New-ConfigFile $script:dataFile
        New-ConfigFile $script:localCacheFile
        New-ConfigFile $script:appDataFile

        Get-ClaudeDesktopConfigPath | Should -BeExactly $script:dataFile
    }

    It "uses Claude-Data even before the file exists there" {
        New-Item -ItemType Directory -Path (Split-Path $script:dataFile -Parent) -Force | Out-Null
        New-ConfigFile $script:appDataFile

        Get-ClaudeDesktopConfigPath | Should -BeExactly $script:dataFile
    }

    It "prefers the Claude-Data folder that has the file" {
        New-ConfigFile $script:dataFile
        New-Item -ItemType Directory -Path (Join-Path $env:LOCALAPPDATA 'Claude-Data' 'claude_other') -Force | Out-Null

        Get-ClaudeDesktopConfigPath | Should -BeExactly $script:dataFile
    }

    It "ignores what in Claude-Data is not an app's folder" {
        New-ConfigFile (Join-Path $env:LOCALAPPDATA 'Claude-Data' 'README.txt')
        New-Item -ItemType Directory -Path (Split-Path $script:localCacheFile -Parent) -Force | Out-Null

        Get-ClaudeDesktopConfigPath | Should -BeExactly $script:localCacheFile
    }

    It "uses the package's LocalCache for a Store version before 3.1623" {
        New-Item -ItemType Directory -Path (Join-Path $env:LOCALAPPDATA 'Packages' 'Claude_pzs8sxrjxfjjc') -Force | Out-Null

        Get-ClaudeDesktopConfigPath | Should -BeExactly $script:localCacheFile
    }

    It "uses %APPDATA%\Claude when the Store version is not installed" {
        Get-ClaudeDesktopConfigPath | Should -BeExactly $script:appDataFile
    }
}
