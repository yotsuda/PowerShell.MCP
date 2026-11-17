using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

// PwshLauncherShared.BuildInitCommand is the single source of truth for the
// PowerShell init script that all non-Windows launchers run when starting a
// fresh pwsh session. The original regression (GitHub issue #45) was a broken
// '' quoting on macOS that produced `$global:PowerShellMCPAgentId = ''default''`,
// which zsh collapsed to a bareword `default`. After the helper was extracted
// from PwshLauncherMacOS into PwshLauncherShared so PwshLauncherLinux can use
// it too, these tests guard the PowerShell-level quote escaping for every
// platform that delivers the script via pwsh (whether through `-File` /
// `-EncodedCommand` / `-Command`).
public class PwshLauncherSharedTests
{
    private const string DefaultAgentId = "default";
    private const int DefaultPid = 12345;

    [Fact]
    public void BuildInitCommand_DefaultAgent_ContainsNoDoubleSingleQuotes()
    {
        var cmd = PwshLauncherShared.BuildInitCommand(DefaultPid, DefaultAgentId, null, null);

        Assert.DoesNotContain("''default''", cmd);
        Assert.Contains("$global:PowerShellMCPAgentId = 'default'", cmd);
    }

    [Fact]
    public void BuildInitCommand_StartLocationWithSingleQuote_IsEscapedForPowerShell()
    {
        // PowerShell single-quoted string escape is '' (doubling). This is a
        // *PowerShell* concern — independent of how the script body reaches pwsh
        // (temp .ps1 on macOS, Base64 on Linux, ArgumentList on the headless path).
        var cmd = PwshLauncherShared.BuildInitCommand(DefaultPid, DefaultAgentId, null, "/Users/o'brien");

        Assert.Contains("Set-Location -LiteralPath '/Users/o''brien';", cmd);
    }

    [Fact]
    public void BuildInitCommand_AgentIdWithSingleQuote_IsEscapedForPowerShell()
    {
        var cmd = PwshLauncherShared.BuildInitCommand(DefaultPid, "te'st", null, null);

        Assert.Contains("$global:PowerShellMCPAgentId = 'te''st'", cmd);
    }

    [Fact]
    public void BuildInitCommand_IncludesModuleCaseFixBeforeImport()
    {
        // Case-sensitive file systems (Linux ext4/btrfs, case-sensitive APFS)
        // can surface a lowercase 'powershell.mcp' directory that
        // Install-PSResource creates. The case-fix script must run before
        // Import-Module so the PascalCase name resolves.
        var cmd = PwshLauncherShared.BuildInitCommand(DefaultPid, DefaultAgentId, null, null);

        var caseFixIdx = cmd.IndexOf("Rename-Item $lc $uc", StringComparison.Ordinal);
        var importIdx = cmd.IndexOf("Import-Module PowerShell.MCP -Force", StringComparison.Ordinal);

        Assert.True(caseFixIdx >= 0, "case-fix snippet must be present");
        Assert.True(caseFixIdx < importIdx, "case-fix must run before Import-Module");
    }

    // BuildWindowsInitCommand is the Windows counterpart to BuildInitCommand.
    // It deliberately diverges: KEEPS PSReadLine (the real Windows console host
    // uses it), and omits Set-Location + the case-fix (cwd comes from
    // CreateProcessW's lpCurrentDirectory; Windows FS is case-insensitive).
    // It must still escape agentId the same way so a quoted ID can't break the
    // launch line — the asymmetry that previously existed in the inline builder.

    [Fact]
    public void BuildWindowsInitCommand_KeepsPSReadLine_AndSetsGlobals()
    {
        var cmd = PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, DefaultAgentId, null);

        Assert.Contains($"$global:PowerShellMCPProxyPid = {DefaultPid}", cmd);
        Assert.Contains("$global:PowerShellMCPAgentId = 'default'", cmd);
        Assert.Contains("Import-Module PowerShell.MCP -Force", cmd);
        Assert.Contains("Import-Module PSReadLine", cmd);          // kept, not removed
        Assert.DoesNotContain("Remove-Module PSReadLine", cmd);
    }

    [Fact]
    public void BuildWindowsInitCommand_OmitsSetLocationAndCaseFix()
    {
        // Windows establishes cwd via CreateProcessW lpCurrentDirectory and is
        // case-insensitive, so neither belongs in the Windows init script.
        var cmd = PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, DefaultAgentId, null);

        Assert.DoesNotContain("Set-Location", cmd);
        Assert.DoesNotContain("Rename-Item", cmd);
    }

    [Fact]
    public void BuildWindowsInitCommand_AgentIdWithSingleQuote_IsEscaped()
    {
        var cmd = PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, "te'st", null);

        Assert.Contains("$global:PowerShellMCPAgentId = 'te''st'", cmd);
    }

    [Fact]
    public void BuildWindowsInitCommand_AppendsStartupCommandsWhenProvided()
    {
        var without = PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, DefaultAgentId, null);
        var with = PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, DefaultAgentId, "Write-Host 'hi'");

        Assert.DoesNotContain("Write-Host 'hi'", without);
        Assert.EndsWith("; Write-Host 'hi'", with);
    }

    // Interactive launchers (Windows/macOS/Linux) gate -NoProfile on the noProfile
    // flag — default off so the user's $PROFILE loads in these human-facing shells,
    // on only when the operator passes the proxy's `--no-profile` flag.

    [Fact]
    public void BuildWindowsCommandLine_OmitsNoProfileByDefault()
    {
        var commandLine = PwshLauncherShared.BuildWindowsCommandLine("Import-Module PowerShell.MCP", noProfile: false);

        Assert.Equal("pwsh.exe -NoExit -Command \"Import-Module PowerShell.MCP\"", commandLine);
        Assert.DoesNotContain("-NoProfile", commandLine);
    }

    // Free text a banner or reason can carry (it sits in a single-quoted PowerShell string).
    private const string NastyBanner = "a; b \"c\" d''e p\\\"q x\\;y \\x\\ 日本 $HOME `t ;; \"unbalanced end\\";

    [Theory]
    [InlineData("a b", "\"a b\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    [InlineData("a\\", "\"a\\\\\"")]
    [InlineData("a\\b", "\"a\\b\"")]
    [InlineData("", "\"\"")]
    public void QuoteWindowsArgument_FollowsCommandLineToArgvWRules(string value, string expected)
    {
        Assert.Equal(expected, PwshLauncherShared.QuoteWindowsArgument(value));
    }

    [Fact]
    public void BuildWindowsCommandLine_RoundTripsQuotesThroughArgv()
    {
        if (!OperatingSystem.IsWindows()) return;
        var command = PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, DefaultAgentId, $"Write-Host '{NastyBanner}'");
        var argv = SplitArgs(PwshLauncherShared.BuildWindowsCommandLine(command, noProfile: false));
        Assert.Equal(new[] { "pwsh.exe", "-NoExit", "-Command", command }, argv);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildWindowsTerminalCommandLine_SurvivesWtReparse(bool noProfile)
    {
        if (!OperatingSystem.IsWindows()) return;
        const string wt = @"C:\Users\u\AppData\Local\Microsoft\WindowsApps\wt.exe";
        const string pwsh = @"C:\Program Files\PowerShell\7\pwsh.exe";
        var command = PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, "sa-1'2", $"Write-Host '{NastyBanner}'");
        var commandLine = PwshLauncherShared.BuildWindowsTerminalCommandLine(wt, pwsh, command, noProfile);

        // No bare ';' may reach wt.exe: it would start a new wt subcommand.
        Assert.DoesNotMatch(@"(?<!\\);", commandLine);

        // What wt.exe does (measured on Windows Terminal 1.25): split with CommandLineToArgvW,
        // turn \; back into ;, then join the shell's arguments, quoting those with a space
        // but without re-escaping anything inside them.
        var wtArgv = SplitArgs(commandLine).Select(a => a.Replace("\\;", ";")).ToArray();
        Assert.Equal(new[] { wt, "-w", "new", "new-tab", pwsh }, wtArgv[..5]);
        var shellLine = string.Join(' ', wtArgv[4..].Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

        var expected = new List<string> { pwsh };
        if (noProfile) expected.Add("-NoProfile");
        expected.AddRange(["-NoExit", "-Command", command]);
        Assert.Equal(expected, SplitArgs(shellLine));
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static string[] SplitArgs(string commandLine)
    {
        var p = CommandLineToArgvW(commandLine, out var n);
        try
        {
            var r = new string[n];
            for (int i = 0; i < n; i++)
                r[i] = System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(p, i * IntPtr.Size))!;
            return r;
        }
        finally { LocalFree(p); }
    }

    [Fact]
    public void BuildWindowsCommandLine_IncludesNoProfileWhenRequested()
    {
        var commandLine = PwshLauncherShared.BuildWindowsCommandLine("Import-Module PowerShell.MCP", noProfile: true);

        Assert.Equal("pwsh.exe -NoProfile -NoExit -Command \"Import-Module PowerShell.MCP\"", commandLine);
    }

    [Fact]
    public void BuildMacOSDoScriptCommand_OmitsNoProfileByDefault()
    {
        var command = PwshLauncherShared.BuildMacOSDoScriptCommand("/tmp/pwsh-mcp-init-test.ps1", noProfile: false);

        Assert.Equal("pwsh -NoExit -File '/tmp/pwsh-mcp-init-test.ps1'", command);
    }

    [Fact]
    public void BuildMacOSDoScriptCommand_IncludesNoProfileWhenRequested()
    {
        var command = PwshLauncherShared.BuildMacOSDoScriptCommand("/tmp/pwsh-mcp-init-test.ps1", noProfile: true);

        Assert.Equal("pwsh -NoProfile -NoExit -File '/tmp/pwsh-mcp-init-test.ps1'", command);
    }

    [Fact]
    public void BuildLinuxPwshCommand_OmitsNoProfileByDefault()
    {
        var command = PwshLauncherShared.BuildLinuxPwshCommand("encoded", noProfile: false);

        Assert.Equal("exec pwsh -NoExit -EncodedCommand encoded", command);
    }

    [Fact]
    public void BuildLinuxPwshCommand_IncludesNoProfileWhenRequested()
    {
        var command = PwshLauncherShared.BuildLinuxPwshCommand("encoded", noProfile: true);

        Assert.Equal("exec pwsh -NoProfile -NoExit -EncodedCommand encoded", command);
    }

    // The headless / CI launcher always uses -NoProfile, independent of the flag.
    [Fact]
    public void BuildHeadlessPwshArguments_AlwaysIncludesNoProfileBeforeNoExit()
    {
        var arguments = PwshLauncherShared.BuildHeadlessPwshArguments("Import-Module PowerShell.MCP");

        Assert.Equal(4, arguments.Length);
        Assert.Equal("-NoProfile", arguments[0]);
        Assert.Equal("-NoExit", arguments[1]);
        Assert.Equal("-Command", arguments[2]);
        Assert.Equal("Import-Module PowerShell.MCP", arguments[3]);
    }

    // A development server (registered through mcp-sitter) sets
    // POWERSHELL_MCP_MODULE on the proxy; consoles do not inherit the proxy's
    // environment, so the init command must name the dev psd1 itself.
    private static string WithModuleVariable(string? value, Func<string> build)
    {
        var saved = Environment.GetEnvironmentVariable("POWERSHELL_MCP_MODULE");
        try
        {
            Environment.SetEnvironmentVariable("POWERSHELL_MCP_MODULE", value);
            return build();
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERSHELL_MCP_MODULE", saved);
        }
    }

    [Fact]
    public void BuildModuleImport_Unset_ImportsByName()
    {
        var import = WithModuleVariable(null, PwshLauncherShared.BuildModuleImport);
        Assert.Equal("Import-Module PowerShell.MCP -Force", import);
    }

    [Fact]
    public void BuildModuleImport_ModuleVariable_ImportsThatPsd1_Escaped()
    {
        var import = WithModuleVariable(@"C:\dev\o'brien\PowerShell.MCP.psd1", PwshLauncherShared.BuildModuleImport);
        Assert.Equal(@"Import-Module 'C:\dev\o''brien\PowerShell.MCP.psd1' -Force", import);
    }

    [Fact]
    public void BothInitCommands_UseTheModuleVariable()
    {
        const string psd1 = @"C:\dev\PowerShell.MCP\PowerShell.MCP.psd1";
        var unix = WithModuleVariable(psd1, () => PwshLauncherShared.BuildInitCommand(DefaultPid, DefaultAgentId, null, null));
        var windows = WithModuleVariable(psd1, () => PwshLauncherShared.BuildWindowsInitCommand(DefaultPid, DefaultAgentId, null));
        Assert.Contains($"Import-Module '{psd1}' -Force", unix);
        Assert.Contains($"Import-Module '{psd1}' -Force", windows);
        Assert.DoesNotContain("Import-Module PowerShell.MCP", windows);
    }
}
