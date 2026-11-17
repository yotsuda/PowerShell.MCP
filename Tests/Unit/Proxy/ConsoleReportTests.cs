using Moq;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Tools;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// The 📣 reports that tell the user, through the AI, which console it works in:
/// queued when a console gets its name (started here, or an unnamed one taken
/// over) and carried by the agent's next tool response, once.
/// </summary>
public class ConsoleReportTests
{
    private static int _nextPid = 61000;

    [Fact]
    public void Reports_AreTakenOnce_AndPerAgent()
    {
        var mgr = ConsoleSessionManager.Instance;
        var a = mgr.AllocateSubAgentId();
        var b = mgr.AllocateSubAgentId();

        mgr.QueueUserReport(a, "Started a new console #1 Silk.");
        mgr.QueueUserReport(a, "Named an existing console #2 Taxi and attached to it.");

        Assert.Null(PowerShellTools.TakeUserReportBlock(b));
        var block = PowerShellTools.TakeUserReportBlock(a);
        Assert.NotNull(block);
        Assert.StartsWith("📣 Required: tell the user", block);
        Assert.Contains("Started a new console #1 Silk. Named an existing console #2 Taxi and attached to it.", block);
        Assert.Null(PowerShellTools.TakeUserReportBlock(a));
    }

    [Fact]
    public async Task GetCurrentLocation_ClaimingAnUnnamedConsole_ReportsItOnce()
    {
        var agentId = ConsoleSessionManager.Instance.AllocateSubAgentId();
        ConsoleSessionManager.Instance.TryMarkFirstAttach(agentId);
        var pid = Interlocked.Increment(ref _nextPid);
        var pipe = $"PSMCP.1000.{agentId}.{pid}";

        var ps = new Mock<IPowerShellService>();
        ps.Setup(s => s.GetCurrentLocationFromPipeAsync(pipe, It.IsAny<CancellationToken>()))
          .ReturnsAsync("Location [FileSystem]: C:\\test");
        var discovery = new Mock<IPipeDiscoveryService>();
        discovery.Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), pipe, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new CachedOutputResult("", ""));

        // First call: discovery took over an unowned console (ConsoleSwitched).
        discovery.Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                 .ReturnsAsync(new PipeDiscoveryResult(pipe, true, new List<string>(), null));
        var first = await PowerShellTools.GetCurrentLocation(ps.Object, discovery.Object, agent_id: agentId);
        Assert.Contains("📣 Required: tell the user", first);
        Assert.Contains($"Named an existing console #{pid} ", first);

        // Next call on the same console: nothing new to report.
        discovery.Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
                 .ReturnsAsync(new PipeDiscoveryResult(pipe, false, new List<string>(), null));
        var second = await PowerShellTools.GetCurrentLocation(ps.Object, discovery.Object, agent_id: agentId);
        Assert.DoesNotContain("📣", second);
    }
}
