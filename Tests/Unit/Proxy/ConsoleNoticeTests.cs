using Moq;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Tools;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// Tests the console "AI session connected." notice logic:
/// - BuildStartupCommands: the startup line for a freshly spawned console.
/// - ShowClaimNoticeAsync: the notice pushed to a console on claim.
/// Both follow the same contract: show the caller's banner when given, else the
/// default "AI session connected." line — a banner REPLACES, never doubles, the
/// generic notice. A reason (spawn only) is appended in dark yellow.
/// </summary>
public class ConsoleNoticeTests
{
    private const string Default = "AI session connected.";

    // ---- BuildStartupCommands (spawned console startup line) ----

    [Fact]
    public void BuildStartupCommands_NoBannerNoReason_ShowsDefaultGreenOnly()
    {
        var cmd = PowerShellTools.BuildStartupCommands(null, null);

        Assert.Contains($"Write-Host '{Default}' -ForegroundColor Green", cmd);
        Assert.DoesNotContain("Reason:", cmd);
    }

    [Fact]
    public void BuildStartupCommands_BannerReplacesDefault()
    {
        var cmd = PowerShellTools.BuildStartupCommands("Hello there", null);

        Assert.Contains("Write-Host 'Hello there' -ForegroundColor Green", cmd);
        Assert.DoesNotContain(Default, cmd);
    }

    [Fact]
    public void BuildStartupCommands_ReasonAppendedInDarkYellow_WithDefaultNotice()
    {
        var cmd = PowerShellTools.BuildStartupCommands(null, "fresh console for build");

        Assert.Contains($"Write-Host '{Default}' -ForegroundColor Green", cmd);
        Assert.Contains("Write-Host 'Reason: fresh console for build' -ForegroundColor DarkYellow", cmd);
    }

    [Fact]
    public void BuildStartupCommands_BannerAndReason_BothShown_NoDefault()
    {
        var cmd = PowerShellTools.BuildStartupCommands("Welcome", "manual launch");

        Assert.Contains("Write-Host 'Welcome' -ForegroundColor Green", cmd);
        Assert.Contains("Write-Host 'Reason: manual launch' -ForegroundColor DarkYellow", cmd);
        Assert.DoesNotContain(Default, cmd);
    }

    [Fact]
    public void BuildStartupCommands_EscapesSingleQuotes()
    {
        var cmd = PowerShellTools.BuildStartupCommands("it's ready", "user's call");

        // PowerShell single-quoted strings escape ' as ''.
        Assert.Contains("Write-Host 'it''s ready' -ForegroundColor Green", cmd);
        Assert.Contains("Reason: user''s call", cmd);
    }

    // ---- ShowClaimNoticeAsync (notice pushed on claim) ----

    private static (Mock<IPowerShellService> mock, Func<string?> captured) MockCapturingSilent()
    {
        string? captured = null;
        var mock = new Mock<IPowerShellService>();
        mock.Setup(s => s.ExecuteSilentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, cmd, _) => captured = cmd)
            .Returns(Task.CompletedTask);
        return (mock, () => captured);
    }

    [Fact]
    public async Task ShowClaimNoticeAsync_NoBanner_PushesDefaultGreen()
    {
        var (mock, captured) = MockCapturingSilent();

        await PowerShellTools.ShowClaimNoticeAsync(mock.Object, "PSMCP.1.a.2", null, default);

        Assert.Contains($"Write-Host '{Default}' -ForegroundColor Green", captured());
    }

    [Fact]
    public async Task ShowClaimNoticeAsync_Banner_ReplacesDefault()
    {
        var (mock, captured) = MockCapturingSilent();

        await PowerShellTools.ShowClaimNoticeAsync(mock.Object, "PSMCP.1.a.2", "Custom hi", default);

        Assert.Contains("Write-Host 'Custom hi' -ForegroundColor Green", captured());
        Assert.DoesNotContain(Default, captured());
    }

    [Fact]
    public async Task ShowClaimNoticeAsync_EscapesSingleQuotes()
    {
        var (mock, captured) = MockCapturingSilent();

        await PowerShellTools.ShowClaimNoticeAsync(mock.Object, "PSMCP.1.a.2", "it's mine", default);

        Assert.Contains("Write-Host 'it''s mine' -ForegroundColor Green", captured());
    }
}
