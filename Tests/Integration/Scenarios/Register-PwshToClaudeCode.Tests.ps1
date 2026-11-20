# Register-PwshToClaudeCode has to be able to point Claude Code at a NEW proxy.
# 'claude mcp add' refuses a name already registered at the same scope, so an
# entry written by an older install could never be updated: the add failed and
# Claude Code kept launching the old install's proxy.
#
# The claude CLI is replaced by a shim that models what the real one keeps:
# user-scope entries, local/project entries tied to the directory they were
# added from, 'mcp get' reporting only the entry that wins for the current
# directory, and the refusal. These run offline and never touch the developer's
# own MCP configuration.
#
# Every helper, the shim included, is defined inside BeforeAll, so it lives in
# this block's scope and is gone when the block ends. A global function named
# claude or Get-MCPProxyPath would outlive the run and shadow the real CLI and
# the module's own commands in the console that ran the tests.

Describe "Register-PwshToClaudeCode" {
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
            $node.Name -eq 'Register-PwshToClaudeCode'
        }, $true) | Select-Object -First 1

        if (-not $functionAst) { throw 'Register-PwshToClaudeCode was not found in the module.' }

        # The module-private helper that applies -ConsoleLaunch / -NoProfile.
        $helperAst = $moduleAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
            $node.Name -eq 'Set-ProxyArgument'
        }, $true) | Select-Object -First 1
        if (-not $helperAst) { throw 'Set-ProxyArgument was not found in the module.' }

        . ([scriptblock]::Create($functionAst.Extent.Text))
        . ([scriptblock]::Create($helperAst.Extent.Text))
        function Get-MCPProxyPath { 'C:\NEW\PowerShell.MCP.Proxy.exe' }

        # Stands in for the prompt about the built-in shell tools: answers with
        # $global:ShimShellChoice and records that it was asked.
        function Read-BuiltInShellToolsChoice {
            param([string]$SettingsPath)
            $global:ShellPromptCount++
            $global:ShimShellChoice
        }

        # Claude Code's settings live under CLAUDE_CONFIG_DIR, pointed at a
        # scratch directory so the developer's own settings.json is never read
        # or written.
        $script:savedConfigDir = $env:CLAUDE_CONFIG_DIR
        function Get-SettingsPath { Join-Path $env:CLAUDE_CONFIG_DIR 'settings.json' }
        function Set-Settings([string]$Json) { Set-Content -LiteralPath (Get-SettingsPath) -Value $Json -NoNewline }
        function Get-Settings { Get-Content -LiteralPath (Get-SettingsPath) -Raw | ConvertFrom-Json -AsHashtable }

        function Get-ShimKey {
            param([string]$Scope, [string]$Name, [string]$Directory)
            if ($Scope -eq 'User') { "User|$Name" } else { "$Scope|$Directory|$Name" }
        }

        # User-scope entries are kept where the real CLI keeps them: under
        # "mcpServers" in .claude.json in CLAUDE_CONFIG_DIR, as JSON. With
        # $global:ShimUserConfigElsewhere set, the shim keeps that file
        # somewhere the cmdlet does not look, as a future Claude Code might,
        # and the cmdlet has to fall back to asking 'claude mcp get'.
        function Get-UserConfigPath {
            if ($global:ShimUserConfigElsewhere) { Join-Path $env:CLAUDE_CONFIG_DIR 'elsewhere' '.claude.json' }
            else { Join-Path $env:CLAUDE_CONFIG_DIR '.claude.json' }
        }
        function Read-UserConfig {
            $path = Get-UserConfigPath
            if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable }
            else { [ordered]@{ numStartups = 3 } }
        }
        function Get-UserServer([string]$Name) {
            $servers = (Read-UserConfig)['mcpServers']
            if ($servers -and $servers.Contains($Name)) { $servers[$Name] }
        }
        function Set-UserServer([string]$Name, $Entry) {
            $config = Read-UserConfig
            if (-not $config.Contains('mcpServers')) { $config['mcpServers'] = [ordered]@{} }
            if ($null -eq $Entry) { $config['mcpServers'].Remove($Name) } else { $config['mcpServers'][$Name] = $Entry }
            $path = Get-UserConfigPath
            New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
            $config | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -NoNewline
        }

        function Set-McpEntry {
            param([string]$Scope, [string]$Name, [string]$Command, [string[]]$ProxyArgs = @(), [hashtable]$Env = @{}, [string]$Directory = $global:ShimProjectDir)
            if ($Scope -eq 'User') {
                Set-UserServer $Name ([ordered]@{ type = 'stdio'; command = $Command; args = @($ProxyArgs); env = $Env })
            }
            else {
                $global:McpState[(Get-ShimKey $Scope $Name $Directory)] = @{ Command = $Command; Args = @($ProxyArgs); Env = $Env }
            }
        }

        # Every scope in one shape: Command, Args, Env.
        function Get-McpEntry {
            param([string]$Scope, [string]$Name, [string]$Directory = $global:ShimProjectDir)
            if ($Scope -ne 'User') { return $global:McpState[(Get-ShimKey $Scope $Name $Directory)] }
            $entry = Get-UserServer $Name
            if (-not $entry) { return $null }
            @{
                Command = $entry['command']
                Args    = if ($entry.Contains('args')) { @($entry['args']) } else { @() }
                Env     = if ($entry['env']) { $entry['env'] } else { @{} }
            }
        }

        function claude {
            $global:ClaudeCalls += , ([string[]]$args)
            $verb = "$($args[0]) $($args[1])"
            $cwd = (Get-Location).Path
            $stdin = @($input) -join "`n"

            # PowerShell strips '--' before a FUNCTION sees it (the real claude is
            # a native command and does receive it), so everything that is not an
            # option is simply positional here.
            $scope = $null; $positional = @()
            for ($i = 2; $i -lt $args.Count; $i++) {
                $token = [string]$args[$i]
                if ($token -eq '--') { continue }
                elseif ($token -in '-s', '--scope') { $i++; $scope = (Get-Culture).TextInfo.ToTitleCase([string]$args[$i]) }
                else { $positional += $token }
            }
            $name = $positional[0]
            $winningScope = @('Local', 'Project', 'User') |
                Where-Object { Get-McpEntry $_ $name $cwd } |
                Select-Object -First 1

            switch ($verb) {
                'mcp get' {
                    $global:ShimGetDirectories += $cwd
                    if (-not $winningScope) { $global:LASTEXITCODE = 1; return "No MCP server named ""$name""." }
                    $entry = Get-McpEntry $winningScope $name $cwd
                    # As the real CLI prints it: the arguments joined by spaces.
                    $lines = @(
                        "${name}:"
                        "  Scope: $winningScope config"
                        '  Status: x Failed to connect'
                        '  Type: stdio'
                        "  Command: $($entry.Command)"
                        "  Args: $($entry.Args -join ' ')"
                    )
                    if ($entry.Env.Count) {
                        $lines += '  Environment:'
                        foreach ($key in $entry.Env.Keys) { $lines += "    $key=$($entry.Env[$key])" }
                    }
                    $global:LASTEXITCODE = 0
                    return $lines
                }
                'mcp remove' {
                    $target = if ($scope) { $scope } else { $winningScope }
                    if (-not $target -or -not (Get-McpEntry $target $name $cwd) -or $global:ShimFailRemove -contains $name) {
                        $global:LASTEXITCODE = 1
                        return "No MCP server named ""$name"" in $scope scope"
                    }
                    if ($target -eq 'User') { Set-UserServer $name $null }
                    else { $global:McpState.Remove((Get-ShimKey $target $name $cwd)) }
                    $global:LASTEXITCODE = 0
                    return "Removed MCP server $name"
                }
                'mcp add-json' {
                    if (-not $scope) { $scope = 'Local' }
                    $json = if ($positional[1] -eq '-') { $stdin } else { $positional[1] }
                    $entry = $json | ConvertFrom-Json -AsHashtable
                    # The refusal this cmdlet had to be taught to work around.
                    if (Get-McpEntry $scope $name $cwd) {
                        $global:LASTEXITCODE = 1
                        return "MCP server $name already exists in $($scope.ToLower()) config"
                    }
                    if ($global:ShimFailAddCommand -and $entry['command'] -eq $global:ShimFailAddCommand) {
                        $global:LASTEXITCODE = 1
                        return "simulated failure adding $($entry['command'])"
                    }
                    if ($scope -eq 'User') { Set-UserServer $name $entry }
                    else { $global:McpState[(Get-ShimKey $scope $name $cwd)] = @{ Command = $entry['command']; Args = @($entry['args']); Env = $entry['env'] } }
                    $global:LASTEXITCODE = 0
                    return "Added stdio MCP server $name to $($scope.ToLower()) config"
                }
                default {
                    $global:LASTEXITCODE = 1
                    return 'unknown command'
                }
            }
        }
    }

    AfterAll {
        Remove-Variable -Name McpState, ClaudeCalls, ShimProjectDir, ShimFailAddCommand, ShimFailRemove, ShimShellChoice, ShellPromptCount, ShimUserConfigElsewhere, ShimGetDirectories -Scope Global -ErrorAction SilentlyContinue
        $env:CLAUDE_CONFIG_DIR = $script:savedConfigDir
    }

    BeforeEach {
        $global:McpState = @{}
        $global:ClaudeCalls = @()
        $global:ShimProjectDir = (Get-Location).Path
        $global:ShimFailAddCommand = $null
        $global:ShimFailRemove = @()
        $global:ShimShellChoice = 'CannotAsk'
        $global:ShellPromptCount = 0
        $global:ShimUserConfigElsewhere = $false
        $global:ShimGetDirectories = @()
        $env:CLAUDE_CONFIG_DIR = Join-Path $TestDrive "claude-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $env:CLAUDE_CONFIG_DIR | Out-Null
    }

    Context "when nothing is registered yet" {
        It "adds the entry at user scope" {
            Register-PwshToClaudeCode 6>$null

            (Get-McpEntry User pwsh).Command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }

        It "removes nothing" {
            Register-PwshToClaudeCode 6>$null

            @($global:ClaudeCalls | Where-Object { $_[1] -eq 'remove' }).Count | Should -Be 0
        }

        It "tells the user to restart Claude Code" {
            $output = Register-PwshToClaudeCode 6>&1 | Out-String

            $output | Should -Match 'Restart Claude Code'
        }
    }

    Context "when an older install left an entry behind" {
        BeforeEach {
            Set-McpEntry User pwsh 'C:\OLD\v1.13.0\PowerShell.MCP.Proxy.exe'
        }

        It "repoints it at the current proxy" {
            Register-PwshToClaudeCode 6>$null

            (Get-McpEntry User pwsh).Command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }

        It "removes the stale entry before adding, so the add is not refused" {
            Register-PwshToClaudeCode 6>$null

            $order = @($global:ClaudeCalls | Where-Object { $_[2] -eq 'pwsh' -and $_[1] -in 'remove', 'add-json' } | ForEach-Object { $_[1] })
            $order | Should -Contain 'remove'
            $order | Should -Contain 'add-json'
            $order.IndexOf('remove') | Should -BeLessThan $order.IndexOf('add-json')
        }
    }

    Context "when the old entry was configured" {
        BeforeEach {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--no-profile' -Env @{ POWERSHELL_MCP_TIMEOUT_CEILING = '120'; FOO = 'bar' }
        }

        It "carries the proxy flags over to the new entry" {
            Register-PwshToClaudeCode 6>$null

            (Get-McpEntry User pwsh).Args | Should -Be @('--no-profile')
        }

        It "carries the environment variables over to the new entry" {
            Register-PwshToClaudeCode 6>$null

            (Get-McpEntry User pwsh).Env['POWERSHELL_MCP_TIMEOUT_CEILING'] | Should -BeExactly '120'
            (Get-McpEntry User pwsh).Env['FOO'] | Should -BeExactly 'bar'
        }

        It "carries them over from 'claude mcp get' when .claude.json is not where it looks" {
            $global:ShimUserConfigElsewhere = $true
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--no-profile' -Env @{ POWERSHELL_MCP_TIMEOUT_CEILING = '120' }

            Register-PwshToClaudeCode 6>$null

            $entry = Get-McpEntry User pwsh
            $entry.Command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
            $entry.Args | Should -Be @('--no-profile')
            $entry.Env['POWERSHELL_MCP_TIMEOUT_CEILING'] | Should -BeExactly '120'
        }
    }

    Context "when the old entry is read from .claude.json" {
        It "keeps an argument that contains a space as one argument" {
            # 'claude mcp get' prints the arguments joined by spaces, so this
            # one used to come back as two.
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--log', 'C:\My Logs\mcp.log'

            Register-PwshToClaudeCode 6>$null

            (Get-McpEntry User pwsh).Args | Should -Be @('--log', 'C:\My Logs\mcp.log')
        }

        It "keeps fields it does not know about" {
            Set-UserServer pwsh ([ordered]@{ type = 'stdio'; command = 'C:\OLD\PowerShell.MCP.Proxy.exe'; args = @(); env = @{}; timeout = 30 })

            Register-PwshToClaudeCode 6>$null

            $entry = Get-UserServer pwsh
            $entry['command'] | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
            $entry['timeout'] | Should -Be 30
        }

        It "leaves the rest of .claude.json alone" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe'
            Set-McpEntry User other 'C:\Other\server.exe'

            Register-PwshToClaudeCode 6>$null

            (Read-UserConfig)['numStartups'] | Should -Be 3
            (Get-McpEntry User other).Command | Should -BeExactly 'C:\Other\server.exe'
        }

        It "does not ask 'claude mcp get' for it, which would launch the old proxy to check it" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe'

            Register-PwshToClaudeCode 6>$null

            $global:ShimGetDirectories | Where-Object { $_ -match 'PowerShell\.MCP\.Register' } | Should -BeNullOrEmpty
        }
    }

    Context "proxy flags given as parameters" {
        It "adds --console-launch to a new entry" {
            Register-PwshToClaudeCode -ConsoleLaunch Minimized -KeepBuiltInShellTools 6>$null

            (Get-McpEntry User pwsh).Args | Should -Be @('--console-launch', 'minimized')
        }

        It "adds it to an existing entry and keeps the other flags and environment" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--no-profile' -Env @{ KEEP = '1' }

            Register-PwshToClaudeCode -ConsoleLaunch Minimized -KeepBuiltInShellTools 6>$null

            $entry = Get-McpEntry User pwsh
            $entry.Command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
            $entry.Args | Should -Be @('--no-profile', '--console-launch', 'minimized')
            $entry.Env['KEEP'] | Should -BeExactly '1'
        }

        It "replaces a value already set, in either form" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--console-launch', 'handoff', '--console-launch=auto'

            Register-PwshToClaudeCode -ConsoleLaunch Minimized -KeepBuiltInShellTools 6>$null

            (Get-McpEntry User pwsh).Args | Should -Be @('--console-launch', 'minimized')
        }

        It "-ConsoleLaunch Auto removes the option" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--console-launch', 'minimized', '--no-profile'

            Register-PwshToClaudeCode -ConsoleLaunch Auto -KeepBuiltInShellTools 6>$null

            (Get-McpEntry User pwsh).Args | Should -Be @('--no-profile')
        }

        It "-NoProfile adds --no-profile once, and -NoProfile:`$false removes it" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--no-profile'
            Register-PwshToClaudeCode -NoProfile -KeepBuiltInShellTools 6>$null
            (Get-McpEntry User pwsh).Args | Should -Be @('--no-profile')

            Register-PwshToClaudeCode -NoProfile:$false -KeepBuiltInShellTools 6>$null
            @((Get-McpEntry User pwsh).Args).Count | Should -Be 0
        }

        It "a later plain re-registration keeps the option" {
            Register-PwshToClaudeCode -ConsoleLaunch Minimized -KeepBuiltInShellTools 6>$null

            Register-PwshToClaudeCode -KeepBuiltInShellTools 6>$null

            (Get-McpEntry User pwsh).Args | Should -Be @('--console-launch', 'minimized')
        }

        It "rejects an unknown value" {
            { Register-PwshToClaudeCode -ConsoleLaunch Hidden 6>$null } | Should -Throw
        }

        It "restores the previous entry with its own flags when the add fails" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--no-profile'
            $global:ShimFailAddCommand = 'C:\NEW\PowerShell.MCP.Proxy.exe'

            Register-PwshToClaudeCode -ConsoleLaunch Minimized -ErrorAction SilentlyContinue 6>$null

            $entry = Get-McpEntry User pwsh
            $entry.Command | Should -BeExactly 'C:\OLD\PowerShell.MCP.Proxy.exe'
            $entry.Args | Should -Be @('--no-profile')
        }
    }

    Context "when the new entry cannot be added" {
        BeforeEach {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--no-profile' -Env @{ POWERSHELL_MCP_TIMEOUT_CEILING = '120' }
            $global:ShimFailAddCommand = 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }

        It "puts the previous entry back, flags and environment included" {
            Register-PwshToClaudeCode -ErrorAction SilentlyContinue 6>$null

            $entry = Get-McpEntry User pwsh
            $entry.Command | Should -BeExactly 'C:\OLD\PowerShell.MCP.Proxy.exe'
            $entry.Args | Should -Be @('--no-profile')
            $entry.Env['POWERSHELL_MCP_TIMEOUT_CEILING'] | Should -BeExactly '120'
        }

        It "reports the failure as an error that says the entry was restored" {
            Register-PwshToClaudeCode -ErrorVariable failures -ErrorAction SilentlyContinue 6>$null

            $failures | Should -Not -BeNullOrEmpty
            ($failures | Out-String) | Should -Match 'restored'
        }
    }

    Context "when a legacy PowerShell entry exists" {
        It "removes one that points at our proxy" {
            Set-McpEntry User PowerShell 'C:\OLD\PowerShell.MCP.Proxy.exe'

            Register-PwshToClaudeCode 6>$null

            Get-McpEntry User PowerShell | Should -BeNullOrEmpty
        }

        It "leaves someone else's PowerShell server alone" {
            Set-McpEntry User PowerShell 'C:\Other\some-other-server.exe'

            Register-PwshToClaudeCode 6>$null

            (Get-McpEntry User PowerShell).Command | Should -BeExactly 'C:\Other\some-other-server.exe'
        }

        It "does not claim a removal that failed" {
            Set-McpEntry User PowerShell 'C:\OLD\PowerShell.MCP.Proxy.exe'
            $global:ShimFailRemove = @('PowerShell')

            $output = Register-PwshToClaudeCode -WarningVariable warnings -WarningAction SilentlyContinue 6>&1 | Out-String

            $output | Should -Not -Match 'Removed legacy'
            ($warnings | Out-String) | Should -Match 'Could not remove the legacy'
        }
    }

    Context "when this directory has its own entry at local scope" {
        It "still updates the user-scope entry that the local one hides from 'mcp get' here" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe' -ProxyArgs '--no-profile' -Env @{ KEEP = '1' }
            Set-McpEntry Local pwsh 'C:\LOCAL\PowerShell.MCP.Proxy.exe'

            Register-PwshToClaudeCode -WarningAction SilentlyContinue 6>$null

            $user = Get-McpEntry User pwsh
            $user.Command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
            $user.Args | Should -Be @('--no-profile')
            $user.Env['KEEP'] | Should -BeExactly '1'
        }

        It "leaves the local entry alone and warns that it wins here" {
            Set-McpEntry User pwsh 'C:\OLD\PowerShell.MCP.Proxy.exe'
            Set-McpEntry Local pwsh 'C:\LOCAL\PowerShell.MCP.Proxy.exe'

            Register-PwshToClaudeCode -WarningVariable warnings -WarningAction SilentlyContinue 6>$null

            (Get-McpEntry Local pwsh).Command | Should -BeExactly 'C:\LOCAL\PowerShell.MCP.Proxy.exe'
            ($warnings | Out-String) | Should -Match 'local scope'
        }

        It "adds the user-scope entry when only the local one exists" {
            Set-McpEntry Local pwsh 'C:\LOCAL\PowerShell.MCP.Proxy.exe'

            Register-PwshToClaudeCode -WarningAction SilentlyContinue 6>$null

            (Get-McpEntry User pwsh).Command | Should -BeExactly 'C:\NEW\PowerShell.MCP.Proxy.exe'
        }
    }

    Context "built-in shell tools, when no one can be asked" {
        It "changes no settings" {
            Register-PwshToClaudeCode 6>$null

            Test-Path (Get-SettingsPath) | Should -BeFalse
        }

        It "explains the parameters instead" {
            $output = Register-PwshToClaudeCode 6>&1 | Out-String

            $output | Should -Match '-DisableBuiltInShellTools'
            $output | Should -Match '-KeepBuiltInShellTools'
        }
    }

    Context "built-in shell tools, when the user is asked" {
        It "adds Bash and PowerShell to permissions.deny on yes" {
            $global:ShimShellChoice = 'Disable'

            Register-PwshToClaudeCode 6>$null

            (Get-Settings).permissions.deny | Should -Be @('Bash', 'PowerShell')
        }

        It "keeps every other setting and deny entry" {
            Set-Settings '{ "permissions": { "allow": ["Read"], "deny": ["WebFetch"] }, "model": "opus", "env": { "A": "1" } }'
            $global:ShimShellChoice = 'Disable'

            Register-PwshToClaudeCode 6>$null

            $settings = Get-Settings
            $settings.permissions.deny | Should -Be @('WebFetch', 'Bash', 'PowerShell')
            $settings.permissions.allow | Should -Be @('Read')
            $settings.model | Should -BeExactly 'opus'
            $settings.env.A | Should -BeExactly '1'
            # Key order is kept, so the file does not reshuffle.
            @($settings.Keys) | Should -Be @('permissions', 'model', 'env')
        }

        It "changes nothing on no, and says how to do it later" {
            Set-Settings '{ "model": "opus" }'
            $global:ShimShellChoice = 'Keep'

            $output = Register-PwshToClaudeCode 6>&1 | Out-String

            Get-Content -LiteralPath (Get-SettingsPath) -Raw | Should -BeExactly '{ "model": "opus" }'
            $output | Should -Match '-DisableBuiltInShellTools'
        }

        It "does not ask when both are already denied, but says so" {
            Set-Settings '{ "permissions": { "deny": ["PowerShell", "Bash"] } }'
            $global:ShimShellChoice = 'Disable'

            $output = Register-PwshToClaudeCode 6>&1 | Out-String

            $global:ShellPromptCount | Should -Be 0
            $output | Should -Match 'already disabled'
        }

        It "adds only the missing one" {
            Set-Settings '{ "permissions": { "deny": ["Bash"] } }'
            $global:ShimShellChoice = 'Disable'

            Register-PwshToClaudeCode 6>$null

            (Get-Settings).permissions.deny | Should -Be @('Bash', 'PowerShell')
        }
    }

    Context "built-in shell tools, with an explicit parameter" {
        It "-DisableBuiltInShellTools disables them without asking" {
            Register-PwshToClaudeCode -DisableBuiltInShellTools 6>$null

            $global:ShellPromptCount | Should -Be 0
            (Get-Settings).permissions.deny | Should -Be @('Bash', 'PowerShell')
        }

        It "-KeepBuiltInShellTools neither asks nor changes anything" {
            $output = Register-PwshToClaudeCode -KeepBuiltInShellTools 6>&1 | Out-String

            $global:ShellPromptCount | Should -Be 0
            Test-Path (Get-SettingsPath) | Should -BeFalse
            $output | Should -Not -Match 'BuiltInShellTools'
        }

        It "cannot be given both" {
            { Register-PwshToClaudeCode -DisableBuiltInShellTools -KeepBuiltInShellTools 6>$null } | Should -Throw
        }
    }

    Context "built-in shell tools, when it is not safe to touch them" {
        It "leaves an unreadable settings.json alone and warns" {
            Set-Settings '{ "permissions": '

            Register-PwshToClaudeCode -DisableBuiltInShellTools -WarningVariable warnings -WarningAction SilentlyContinue 6>$null

            Get-Content -LiteralPath (Get-SettingsPath) -Raw | Should -BeExactly '{ "permissions": '
            ($warnings | Out-String) | Should -Match 'permissions.deny'
        }

        It "does not deny them when registration failed, so Claude Code keeps a shell" {
            $global:ShimFailAddCommand = 'C:\NEW\PowerShell.MCP.Proxy.exe'
            $global:ShimShellChoice = 'Disable'

            Register-PwshToClaudeCode -DisableBuiltInShellTools -ErrorAction SilentlyContinue 6>$null

            $global:ShellPromptCount | Should -Be 0
            Test-Path (Get-SettingsPath) | Should -BeFalse
        }
    }
}
