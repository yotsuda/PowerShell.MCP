using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

// Regression tests for GitHub issue #45: broken '' quoting on macOS.
// The original bug produced `$global:PowerShellMCPAgentId = ''default''` which zsh
// collapsed to a bareword `default`, so pwsh failed with "command not recognized".
// These tests lock in the Base64 -EncodedCommand approach so the regression cannot come back.
public class PwshLauncherMacOSTests
{
    private const string DefaultAgentId = "default";
    private const int DefaultPid = 12345;

    [Fact]
    public void BuildInitCommand_DefaultAgent_ContainsNoDoubleSingleQuotes()
    {
        var cmd = PwshLauncherMacOS.BuildInitCommand(DefaultPid, DefaultAgentId, null, null);

        Assert.DoesNotContain("''default''", cmd);
        Assert.Contains("$global:PowerShellMCPAgentId = 'default'", cmd);
    }

    [Fact]
    public void BuildInitCommand_StartLocationWithSingleQuote_IsEscapedForPowerShell()
    {
        // PowerShell single-quoted string escape is '' (doubling). This is a *PowerShell* concern,
        // not a shell concern, because the whole command is Base64-encoded before reaching zsh.
        var cmd = PwshLauncherMacOS.BuildInitCommand(DefaultPid, DefaultAgentId, null, "/Users/o'brien");

        Assert.Contains("Set-Location -LiteralPath '/Users/o''brien';", cmd);
    }

    [Fact]
    public void BuildInitCommand_AgentIdWithSingleQuote_IsEscapedForPowerShell()
    {
        var cmd = PwshLauncherMacOS.BuildInitCommand(DefaultPid, "te'st", null, null);

        Assert.Contains("$global:PowerShellMCPAgentId = 'te''st'", cmd);
    }

    [Fact]
    public void BuildInitCommand_IncludesModuleCaseFixBeforeImport()
    {
        // Parity with Linux: case-sensitive APFS volumes can surface the same lowercase
        // 'powershell.mcp' directory that Install-PSResource creates on Linux.
        var cmd = PwshLauncherMacOS.BuildInitCommand(DefaultPid, DefaultAgentId, null, null);

        var caseFixIdx = cmd.IndexOf("Rename-Item $lc $uc", StringComparison.Ordinal);
        var importIdx = cmd.IndexOf("Import-Module PowerShell.MCP -Force", StringComparison.Ordinal);

        Assert.True(caseFixIdx >= 0, "case-fix snippet must be present");
        Assert.True(caseFixIdx < importIdx, "case-fix must run before Import-Module");
    }

    [Fact]
    public void EncodeCommand_ProducesOnlyBase64SafeCharacters()
    {
        // A Base64 string contains only [A-Za-z0-9+/=] — none of which are shell-sensitive
        // in zsh or AppleScript string context. This is the whole point of using -EncodedCommand.
        var cmd = PwshLauncherMacOS.BuildInitCommand(DefaultPid, DefaultAgentId, null, null);
        var encoded = PwshLauncherMacOS.EncodeCommand(cmd);

        Assert.Matches("^[A-Za-z0-9+/=]+$", encoded);
    }
}
