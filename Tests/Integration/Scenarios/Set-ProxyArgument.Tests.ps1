# Set-ProxyArgument writes the proxy flags into an MCP entry's "args" for
# Register-PwshToClaudeCode and Register-PwshToClaudeDesktop; the proxy reads
# them back (Program.cs, PwshLauncherShared.ParseConsoleLaunch). The two sides
# are tested apart, so a flag renamed or a mode added on one side would pass
# both suites and only fail on a user's machine. The last Describe here reads
# the proxy's source and holds the two sides together.

BeforeAll {
    $script:repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    $psm1 = Join-Path $script:repoRoot 'Staging' 'PowerShell.MCP.psm1'

    $parseErrors = $null
    $script:moduleAst = [System.Management.Automation.Language.Parser]::ParseFile($psm1, [ref]$null, [ref]$parseErrors)
    if ($parseErrors) { throw "PowerShell.MCP.psm1 has $($parseErrors.Count) parse error(s)." }

    function Get-ModuleFunctionAst([string]$Name) {
        $script:moduleAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name
        }, $true) | Select-Object -First 1
    }

    . ([scriptblock]::Create((Get-ModuleFunctionAst 'Set-ProxyArgument').Extent.Text))

    # Set-ProxyArgument returns its array wrapped (, $array) so that a caller
    # assigning it always gets an array. Piped straight into Should, the
    # wrapper arrives as one item; this unwraps it.
    function Get-ProxyArgument([string[]]$Arguments, [hashtable]$BoundParameters) {
        $result = Set-ProxyArgument -Arguments $Arguments -BoundParameters $BoundParameters
        $result
    }
}

Describe "Set-ProxyArgument" {
    Context "flags already there in another case, as the proxy accepts them" {
        It "-NoProfile does not add a second --no-profile next to --No-Profile" {
            Get-ProxyArgument -Arguments '--No-Profile' -BoundParameters @{ NoProfile = $true } |
                Should -Be @('--No-Profile')
        }

        It "-NoProfile:`$false removes --NO-PROFILE" {
            @(Get-ProxyArgument -Arguments '--NO-PROFILE', '--console-launch', 'minimized' -BoundParameters @{ NoProfile = $false }) |
                Should -Be @('--console-launch', 'minimized')
        }

        It "-ConsoleLaunch replaces --Console-Launch HANDOFF" {
            Get-ProxyArgument -Arguments '--Console-Launch', 'HANDOFF' -BoundParameters @{ ConsoleLaunch = 'Minimized' } |
                Should -Be @('--console-launch', 'minimized')
        }

        It "-ConsoleLaunch replaces --CONSOLE-LAUNCH=Auto" {
            Get-ProxyArgument -Arguments '--CONSOLE-LAUNCH=Auto', '--no-profile' -BoundParameters @{ ConsoleLaunch = 'Handoff' } |
                Should -Be @('--no-profile', '--console-launch', 'handoff')
        }
    }

    Context "what it leaves alone" {
        It "keeps arguments it does not know, in order" {
            Get-ProxyArgument -Arguments '--log', 'C:\My Logs\a.log' -BoundParameters @{ ConsoleLaunch = 'Minimized' } |
                Should -Be @('--log', 'C:\My Logs\a.log', '--console-launch', 'minimized')
        }

        It "does not take the next flag for the value of a --console-launch that has none" {
            Get-ProxyArgument -Arguments '--console-launch', '--no-profile' -BoundParameters @{ ConsoleLaunch = 'Minimized' } |
                Should -Be @('--no-profile', '--console-launch', 'minimized')
        }

        It "changes nothing when neither parameter is given" {
            Get-ProxyArgument -Arguments '--no-profile', '--console-launch', 'handoff' -BoundParameters @{} |
                Should -Be @('--no-profile', '--console-launch', 'handoff')
        }

        It "returns an empty array, not null, when nothing is left" {
            $result = Set-ProxyArgument -Arguments '--no-profile' -BoundParameters @{ NoProfile = $false }
            , $result | Should -BeOfType [string[]]
            $result.Count | Should -Be 0
        }
    }
}

Describe "Proxy flags: the cmdlets and the proxy agree" {
    BeforeAll {
        $proxyDir = Join-Path $script:repoRoot 'PowerShell.MCP.Proxy'
        $launcherSource = Get-Content -LiteralPath (Join-Path $proxyDir 'Services' 'PowerShellProcessManager.cs') -Raw
        $programSource = Get-Content -LiteralPath (Join-Path $proxyDir 'Program.cs') -Raw

        $script:consoleLaunchOption = if ($launcherSource -match 'const\s+string\s+ConsoleLaunchOption\s*=\s*"([^"]+)"') { $Matches[1] }
        $script:consoleLaunchModes = if ($launcherSource -match 'enum\s+ConsoleLaunch\s*\{([^}]*)\}') {
            @($Matches[1] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        }
        $script:noProfileFlag = if ($programSource -match 'SuppressProfileOnInteractive\s*=[^;]*?"(--[^"]+)"') { $Matches[1] }

        function Get-ValidateSet([string]$Function, [string]$Parameter) {
            $param = (Get-ModuleFunctionAst $Function).Body.ParamBlock.Parameters |
                Where-Object { $_.Name.VariablePath.UserPath -eq $Parameter }
            $attribute = $param.Attributes | Where-Object { $_.TypeName.Name -eq 'ValidateSet' }
            @($attribute.PositionalArguments | ForEach-Object { $_.Value })
        }
    }

    It "finds the flags in the proxy's source" {
        # If these fail, the source moved: point the patterns above at it again
        # rather than deleting the checks below.
        $script:consoleLaunchOption | Should -Not -BeNullOrEmpty
        $script:consoleLaunchModes | Should -Not -BeNullOrEmpty
        $script:noProfileFlag | Should -Not -BeNullOrEmpty
    }

    It "writes the --console-launch the proxy reads" {
        @(Get-ProxyArgument -Arguments @() -BoundParameters @{ ConsoleLaunch = 'Minimized' })[0] |
            Should -BeExactly $script:consoleLaunchOption
    }

    It "writes the --no-profile the proxy reads" {
        Get-ProxyArgument -Arguments @() -BoundParameters @{ NoProfile = $true } |
            Should -Be @($script:noProfileFlag)
    }

    It "offers exactly the proxy's console-launch modes in <_>" -ForEach 'Register-PwshToClaudeCode', 'Register-PwshToClaudeDesktop' {
        Get-ValidateSet $_ 'ConsoleLaunch' | Sort-Object | Should -Be ($script:consoleLaunchModes | Sort-Object)
    }

    It "writes every mode but auto as a value the proxy parses, and removes the option for auto" {
        foreach ($mode in $script:consoleLaunchModes) {
            $result = @(Get-ProxyArgument -Arguments @() -BoundParameters @{ ConsoleLaunch = $mode })
            if ($mode -eq 'Auto') {
                # Auto is the proxy's default when the option is absent.
                $result.Count | Should -Be 0
            }
            else {
                $result.Count | Should -Be 2
                $result[1] | Should -BeIn $script:consoleLaunchModes
            }
        }
    }
}
