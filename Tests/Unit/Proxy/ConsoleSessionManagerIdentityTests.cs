using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// Unit tests for the multi-console identity / routing primitives on
/// <see cref="ConsoleSessionManager"/> — the pipe-name &lt;-&gt; PID contract,
/// the owned/unowned naming scheme, per-agent isolation of the active pipe
/// and busy set, console-name assignment, dead-pipe cleanup, and agent-id
/// validation. These are the linchpins of "which console does this route to"
/// and were previously only used as test setup, never asserted as the
/// subject under test.
///
/// The manager is a singleton, so each test uses unique high pwsh PIDs and
/// freshly-allocated agent IDs to avoid cross-test contamination.
/// </summary>
public class ConsoleSessionManagerIdentityTests
{
    private readonly ConsoleSessionManager _manager = ConsoleSessionManager.Instance;

    // ---- pipe-name <-> PID contract -------------------------------------

    [Fact]
    public void GetPipeNameForPids_ProducesFourSegmentOwnedName()
    {
        // Owned pipes are PSMCP.{proxyPid}.{agentId}.{pwshPid} = 4 segments.
        // EnumerateUnownedPipes relies on this exact arity to tell a proxy's
        // own consoles apart from user-started (2-segment) ones.
        var name = ConsoleSessionManager.GetPipeNameForPids(1111, "default", 2222);

        Assert.Equal("PSMCP.1111.default.2222", name);
        Assert.Equal(4, name.Split('.').Length);
    }

    [Fact]
    public void GetPidFromPipeName_OwnedName_ReturnsTrailingPwshPid()
    {
        // The pwsh PID is always the LAST segment, regardless of arity.
        var name = ConsoleSessionManager.GetPipeNameForPids(1111, "sa-abcd1234", 2222);

        Assert.Equal(2222, ConsoleSessionManager.GetPidFromPipeName(name));
    }

    [Fact]
    public void GetPidFromPipeName_UnownedTwoSegmentName_ReturnsPwshPid()
    {
        // User-started consoles register as PSMCP.{pwshPid} (2 segments).
        Assert.Equal(2, "PSMCP.4242".Split('.').Length);
        Assert.Equal(4242, ConsoleSessionManager.GetPidFromPipeName("PSMCP.4242"));
    }

    [Fact]
    public void GetPidFromPipeName_NonNumericTail_ReturnsNull()
    {
        // A malformed / unexpected name must not be parsed into a bogus PID
        // that could be used to route to the wrong (or a dead) console.
        Assert.Null(ConsoleSessionManager.GetPidFromPipeName("PSMCP.1111.default.notapid"));
        Assert.Null(ConsoleSessionManager.GetPidFromPipeName("garbage"));
    }

    [Fact]
    public void GetPipeNameForPids_RoundTripsThroughGetPidFromPipeName()
    {
        const int pwshPid = 93010;
        var name = ConsoleSessionManager.GetPipeNameForPids(_manager.ProxyPid, "default", pwshPid);

        Assert.Equal(pwshPid, ConsoleSessionManager.GetPidFromPipeName(name));
    }

    // ---- per-agent active-pipe isolation --------------------------------

    [Fact]
    public void ActivePipeName_IsIsolatedPerAgent()
    {
        var agentA = _manager.AllocateSubAgentId();
        var agentB = _manager.AllocateSubAgentId();
        var pipeA = ConsoleSessionManager.GetPipeNameForPids(_manager.ProxyPid, agentA, 93020);

        _manager.SetActivePipeName(agentA, pipeA);

        Assert.Equal(pipeA, _manager.GetActivePipeName(agentA));
        Assert.Null(_manager.GetActivePipeName(agentB)); // must not bleed across agents
    }

    // ---- per-agent busy-set isolation -----------------------------------

    [Fact]
    public void KnownBusyPids_AreIsolatedPerAgent()
    {
        var agentA = _manager.AllocateSubAgentId();
        var agentB = _manager.AllocateSubAgentId();
        const int busyPid = 93030;

        _manager.MarkPipeBusy(agentA, busyPid);

        // Agent B never saw this pid busy.
        Assert.DoesNotContain(busyPid, _manager.ConsumeKnownBusyPids(agentB));

        // Agent A sees it, and Consume clears it (second consume is empty).
        Assert.Contains(busyPid, _manager.ConsumeKnownBusyPids(agentA));
        Assert.DoesNotContain(busyPid, _manager.ConsumeKnownBusyPids(agentA));
    }

    [Fact]
    public void UnmarkPipeBusy_RemovesBeforeConsume()
    {
        var agentId = _manager.AllocateSubAgentId();
        const int busyPid = 93040;

        _manager.MarkPipeBusy(agentId, busyPid);
        _manager.UnmarkPipeBusy(agentId, busyPid);

        Assert.DoesNotContain(busyPid, _manager.ConsumeKnownBusyPids(agentId));
    }

    // ---- console-name assignment ----------------------------------------

    [Fact]
    public void TryAssignNameToPid_FirstAssignWins_SecondReturnsNull()
    {
        const int pwshPid = 93050;

        var title = _manager.TryAssignNameToPid(pwshPid);

        Assert.NotNull(title);
        Assert.StartsWith($"#{pwshPid} ", title);          // "#93050 <Name>"
        Assert.Null(_manager.TryAssignNameToPid(pwshPid));  // already titled -> null
    }

    [Fact]
    public void GetConsoleDisplayName_TitledVsUntitled()
    {
        const int titledPid = 93060;
        const int untitledPid = 93061;

        var title = _manager.TryAssignNameToPid(titledPid);

        // Titled: "PID #<pid> <Name>" (wraps the assigned title).
        Assert.Equal($"PID {title}", _manager.GetConsoleDisplayName(titledPid));
        // Untitled: bare fallback.
        Assert.Equal($"PID #{untitledPid}", _manager.GetConsoleDisplayName(untitledPid));
    }

    [Fact]
    public void GetConsoleDisplayName_FromPipeName_ResolvesViaTrailingPid()
    {
        const int pwshPid = 93070;
        var title = _manager.TryAssignNameToPid(pwshPid);
        var pipeName = ConsoleSessionManager.GetPipeNameForPids(_manager.ProxyPid, "default", pwshPid);

        Assert.Equal($"PID {title}", _manager.GetConsoleDisplayName(pipeName));
        Assert.Equal("PID #unknown", _manager.GetConsoleDisplayName((string?)null));
    }

    // ---- dead-pipe cleanup (title + busy, beyond cwd) -------------------

    [Fact]
    public void ClearDeadPipe_RemovesTitleAndBusyAndActive()
    {
        var agentId = _manager.AllocateSubAgentId();
        const int pwshPid = 93080;
        var pipeName = ConsoleSessionManager.GetPipeNameForPids(_manager.ProxyPid, agentId, pwshPid);

        _manager.SetActivePipeName(agentId, pipeName);
        _manager.MarkPipeBusy(agentId, pwshPid);
        _ = _manager.TryAssignNameToPid(pwshPid);

        _manager.ClearDeadPipe(agentId, pipeName);

        Assert.Null(_manager.GetActivePipeName(agentId));                       // active cleared
        Assert.DoesNotContain(pwshPid, _manager.ConsumeKnownBusyPids(agentId)); // busy cleared
        Assert.Equal($"PID #{pwshPid}", _manager.GetConsoleDisplayName(pwshPid)); // title cleared -> fallback
    }

    // ---- agent-id validation (trust boundary for sub-agent routing) -----

    [Fact]
    public void IsValidAgentId_DefaultAndAllocatedAreValid_OthersRejected()
    {
        Assert.True(_manager.IsValidAgentId("default"));

        var allocated = _manager.AllocateSubAgentId();
        Assert.True(_manager.IsValidAgentId(allocated));

        // A never-allocated id is rejected, as is junk. ("sa-never-allocated"
        // can't collide with a real allocation: those are sa-{8 hex chars}.)
        Assert.False(_manager.IsValidAgentId("sa-never-allocated"));
        Assert.False(_manager.IsValidAgentId("bogus"));
    }

    [Fact]
    public void AllocateSubAgentId_ReturnsUniquePrefixedIds()
    {
        var a = _manager.AllocateSubAgentId();
        var b = _manager.AllocateSubAgentId();

        Assert.StartsWith("sa-", a);
        Assert.StartsWith("sa-", b);
        Assert.NotEqual(a, b);
    }
}
