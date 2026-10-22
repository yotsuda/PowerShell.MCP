using Moq;
using PowerShell.MCP.Proxy.Models;
using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// A console this session used and that is gone must be reported on the next
/// tool call — on the fast path too (the active console answered, nothing else
/// was probed), which is the path every routine call takes and the one that
/// let a self-closed standby console vanish unnoticed on 2026-10-23.
///
/// PIDs near int.MaxValue are never live processes, so a console tracked under
/// one reads as "process gone"; the current test process stands in for a live
/// sibling. Shares the serial collection of <see cref="ClosedConsoleNoticesTests"/>
/// because it plants notes under the same static root.
/// </summary>
[Collection("ClosedConsoleNotices serial")]
public class PipeDiscoveryClosedConsoleTests : IDisposable
{
    private readonly Mock<IPowerShellService> _ps = new();
    private readonly PipeDiscoveryService _service;
    private readonly ConsoleSessionManager _sm = ConsoleSessionManager.Instance;
    private readonly string _agentId;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerShell.MCP.Tests", Guid.NewGuid().ToString("N"));

    private const int ActivePid = 1_999_990_001;
    private const int GonePid = 1_999_990_002;
    private const int NotedPid = 1_999_990_003;

    public PipeDiscoveryClosedConsoleTests()
    {
        _service = new PipeDiscoveryService(_ps.Object);
        _agentId = _sm.AllocateSubAgentId();
        ClosedConsoleNotices.LiveRootForTests = _root;
    }

    public void Dispose()
    {
        foreach (var pid in new[] { ActivePid, GonePid, NotedPid, Environment.ProcessId })
            _sm.ClearDeadPipe(_agentId, Pipe(pid));
        ClosedConsoleNotices.LiveRootForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Pipe(int pid) => ConsoleSessionManager.GetPipeNameForPids(_sm.ProxyPid, _agentId, pid);

    private void ReadyActiveConsole()
    {
        _sm.SetActivePipeName(_agentId, Pipe(ActivePid));
        _ps.Setup(s => s.GetStatusFromPipeAsync(Pipe(ActivePid), It.IsAny<CancellationToken>()))
           .ReturnsAsync(new GetStatusResponse { Status = "standby", Pid = ActivePid });
    }

    private void PlantNote(int pid, string reason)
    {
        var dir = ClosedConsoleNotices.GroupDir(_sm.ProxyPid, _agentId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, pid + ClosedConsoleNotices.Suffix), reason);
    }

    [Fact]
    public async Task FastPath_ReportsAnOwnedStandbyConsoleWhoseProcessIsGone_Once()
    {
        // The sibling was once this agent's active console (so it is owned), the
        // AI then moved on; its process is gone and it never said why.
        _sm.SetActivePipeName(_agentId, Pipe(GonePid));
        ReadyActiveConsole();

        var first = await _service.FindReadyPipeAsync(_agentId, CancellationToken.None);

        Assert.Equal(Pipe(ActivePid), first.ReadyPipeName);
        var line = Assert.Single(first.ClosedConsoleMessages);
        Assert.Contains($"Console PID #{GonePid} was closed", line);
        Assert.Contains("outside this server", line);
        Assert.DoesNotContain(GonePid, _sm.GetKnownOwnedPids(_agentId));

        var second = await _service.FindReadyPipeAsync(_agentId, CancellationToken.None);
        Assert.Empty(second.ClosedConsoleMessages);
    }

    [Fact]
    public async Task FastPath_ReportsTheConsolesOwnReason_AndConsumesTheNote()
    {
        _sm.MarkPipeBusy(_agentId, NotedPid);
        _sm.UnmarkPipeBusy(_agentId, NotedPid); // standby again, still owned
        PlantNote(NotedPid, "closed itself after 10 min without use (idle standby)");
        ReadyActiveConsole();

        var result = await _service.FindReadyPipeAsync(_agentId, CancellationToken.None);

        var line = Assert.Single(result.ClosedConsoleMessages);
        Assert.Contains($"Console PID #{NotedPid} was closed: it closed itself after 10 min without use (idle standby).", line);
        Assert.Contains("Start-Job jobs it was running ended with it", line);
        Assert.Empty(Directory.GetFiles(ClosedConsoleNotices.GroupDir(_sm.ProxyPid, _agentId)));
    }

    [Fact]
    public async Task FastPath_LeavesALiveSiblingAlone()
    {
        _sm.SetActivePipeName(_agentId, Pipe(Environment.ProcessId));
        ReadyActiveConsole();

        var result = await _service.FindReadyPipeAsync(_agentId, CancellationToken.None);

        Assert.Empty(result.ClosedConsoleMessages);
        Assert.Contains(Environment.ProcessId, _sm.GetKnownOwnedPids(_agentId));
    }

    [Fact]
    public void SlowPath_DetectClosedConsoles_CoversOwnedStandbyConsoles_NotOnlyBusyOnes()
    {
        // No real pipe exists for a test PID, so the enumeration-based check
        // sees it as gone. Before, only PIDs in the busy set were considered.
        _sm.SetActivePipeName(_agentId, Pipe(GonePid));
        _sm.SetActivePipeName(_agentId, null);
        PlantNote(GonePid, "closed itself after 11 min without use");

        var lines = _service.DetectClosedConsoles(_agentId);

        var line = Assert.Single(lines);
        Assert.Contains($"Console PID #{GonePid} was closed: it closed itself after 11 min without use.", line);
        Assert.Empty(_service.DetectClosedConsoles(_agentId));
    }
}
