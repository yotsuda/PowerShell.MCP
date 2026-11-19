using PowerShell.MCP.Proxy.Tools;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// wait_for_completion's answer (PowerShellTools.BuildWaitResponse). Seen 2026-11-19
/// with four consoles: "Use wait_for_completion tool to wait and retrieve the result."
/// closed every answer that still had a console running, after the collected result,
/// where it read as being about that result.
/// </summary>
public class WaitResponseTests
{
    private const string LocalBusy = "⧗ Pipeline is running | Window: #134244 Tower | Status: Busy | Pipeline: Start-Sleep 14 | Duration: 11.31s";
    private const string RemoteBusy = "[remote console: 20.163.204.163:22]\n\n⧗ Pipeline is running | Status: Busy | Duration: 9.45s\n\nStill running. wait_for_completion waits for it; cancel_remote_command interrupts it.";
    private const string Completed = "[remote console: 127.0.0.1]\n\n✓ Pipeline executed successfully | Status: Ready | Duration: 8.09s\n\nlinux done";

    [Fact]
    public void AResult_IsNotFollowedByAHint_WhileOtherConsolesRun()
    {
        var response = PowerShellTools.BuildWaitResponse([], Completed, LocalBusy, RemoteBusy);

        Assert.EndsWith("linux done", response);
        Assert.DoesNotContain("Use wait_for_completion tool to wait and retrieve the result", response);
    }

    [Fact]
    public void TheLocalHint_FollowsTheLocalConsoles_BeforeAnyResult()
    {
        var response = PowerShellTools.BuildWaitResponse([], Completed, LocalBusy, RemoteBusy);

        var local = response.IndexOf(LocalBusy, StringComparison.Ordinal);
        var hint = response.IndexOf(PowerShellTools.LocalStillRunningHint, StringComparison.Ordinal);
        var remote = response.IndexOf(RemoteBusy, StringComparison.Ordinal);
        var result = response.IndexOf(Completed, StringComparison.Ordinal);
        Assert.True(local >= 0 && local < hint && hint < remote && remote < result, response);
    }

    [Fact]
    public void OnlyRemoteConsolesRunning_GetNoExtraHint_TheirBlocksCarryTheirOwn()
    {
        var response = PowerShellTools.BuildWaitResponse([], Completed, "", RemoteBusy);

        Assert.DoesNotContain(PowerShellTools.LocalStillRunningHint, response);
        Assert.Contains("Still running. wait_for_completion waits for it", response);
    }

    [Fact]
    public void TheLastResult_StandsAlone()
    {
        Assert.Equal(Completed, PowerShellTools.BuildWaitResponse([], Completed, "", ""));
    }

    [Fact]
    public void ClosedConsoles_ComeAfterTheRunningOnes_AndBeforeTheResults()
    {
        var response = PowerShellTools.BuildWaitResponse(["Console #1 Alpha was closed."], Completed, LocalBusy, "");

        Assert.True(response.IndexOf(PowerShellTools.LocalStillRunningHint, StringComparison.Ordinal)
                    < response.IndexOf("Console #1 Alpha was closed.", StringComparison.Ordinal));
        Assert.True(response.IndexOf("Console #1 Alpha was closed.", StringComparison.Ordinal)
                    < response.IndexOf(Completed, StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_IsTheNothingToWaitForMessage()
    {
        Assert.Equal(PowerShellTools.NothingToWaitForMessage, PowerShellTools.BuildWaitResponse([], "", "", ""));
    }
}
