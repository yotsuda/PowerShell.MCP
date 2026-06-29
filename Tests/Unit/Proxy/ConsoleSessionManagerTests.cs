using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// Unit tests for ConsoleSessionManager's LastAiCwd tracking.
/// The instance is a singleton so each test uses unique pwsh PIDs and
/// agent IDs to avoid cross-test contamination.
/// </summary>
public class ConsoleSessionManagerTests
{
    private readonly ConsoleSessionManager _manager = ConsoleSessionManager.Instance;

    [Fact]
    public void SetLastAiCwd_PerPidValueRoundTrips()
    {
        const int pid = 91001;
        var agentId = _manager.AllocateSubAgentId();
        var cwd = Path.Combine(Path.GetTempPath(), "perpid-roundtrip");

        _manager.SetLastAiCwd(agentId, pid, cwd);

        Assert.Equal(cwd, _manager.GetLastAiCwd(pid));

        _manager.SetLastAiCwd(agentId, pid, null);
    }

    [Fact]
    public void SetLastAiCwd_AlsoPopulatesSessionFallback()
    {
        // Same call that records per-pid must seed the agent-level fallback,
        // so a later auto-start (per-pid entry gone with the dead pipe) can
        // still resume at the AI's last cwd.
        const int pid = 91002;
        var agentId = _manager.AllocateSubAgentId();
        var cwd = Path.Combine(Path.GetTempPath(), "session-fallback");

        _manager.SetLastAiCwd(agentId, pid, cwd);

        Assert.Equal(cwd, _manager.GetSessionLastAiCwd(agentId));

        _manager.SetLastAiCwd(agentId, pid, null);
    }

    [Fact]
    public void SetLastAiCwd_NullPreservesSessionFallback()
    {
        // Clearing the per-pid entry (e.g. via ClearDeadPipe path) must NOT
        // wipe the agent-level fallback — that's the whole point of having
        // a session-scoped slot.
        const int pid = 91003;
        var agentId = _manager.AllocateSubAgentId();
        var cwd = Path.Combine(Path.GetTempPath(), "preserve-on-null");

        _manager.SetLastAiCwd(agentId, pid, cwd);
        _manager.SetLastAiCwd(agentId, pid, null);

        Assert.Null(_manager.GetLastAiCwd(pid));
        Assert.Equal(cwd, _manager.GetSessionLastAiCwd(agentId));
    }

    [Fact]
    public void GetSessionLastAiCwd_NoEntry_ReturnsNull()
    {
        var agentId = _manager.AllocateSubAgentId();
        Assert.Null(_manager.GetSessionLastAiCwd(agentId));
    }

    [Fact]
    public void SetLastAiCwd_LatestValueWins()
    {
        // Multiple successful pipelines on the same agent should keep the
        // session fallback advancing to wherever the AI is now.
        const int pid = 91004;
        var agentId = _manager.AllocateSubAgentId();
        var first = Path.Combine(Path.GetTempPath(), "first");
        var second = Path.Combine(Path.GetTempPath(), "second");

        _manager.SetLastAiCwd(agentId, pid, first);
        _manager.SetLastAiCwd(agentId, pid, second);

        Assert.Equal(second, _manager.GetSessionLastAiCwd(agentId));

        _manager.SetLastAiCwd(agentId, pid, null);
    }

    [Fact]
    public void ClearDeadPipe_DefaultAgent_PreservesSessionFallback()
    {
        // Motivating scenario: AI's only console dies (default agent). The
        // per-pid entry is wiped by ClearDeadPipe, but the agent-level
        // fallback must survive so the next execute_command's auto-start
        // can resume at the AI's last known cwd. The "default" agent never
        // gets evicted (it's the long-running session for primary AI use).
        const int pid = 91005;
        const string defaultAgent = "default";
        var cwd = Path.Combine(Path.GetTempPath(), "survive-dead-pipe");
        var pipeName = $"PSMCP.{_manager.ProxyPid}.{defaultAgent}.{pid}";

        _manager.SetActivePipeName(defaultAgent, pipeName);
        _manager.SetLastAiCwd(defaultAgent, pid, cwd);

        _manager.ClearDeadPipe(defaultAgent, pipeName);

        Assert.Null(_manager.GetLastAiCwd(pid));
        Assert.Equal(cwd, _manager.GetSessionLastAiCwd(defaultAgent));
        // Session-level state on "default" leaks across tests, but no other
        // test reads GetSessionLastAiCwd("default"), so the leak is benign.
    }

    [Fact]
    public void ClearDeadPipe_SubAgentLastPipeDies_EvictsSessionFallback()
    {
        // For sub-agents, when their last pipe dies the session is fully
        // evicted (memory cleanup) — including the agent-level cwd
        // fallback. Sub-agent lifecycles are bounded, and a sub-agent with
        // no surviving pipe has no upcoming work to resume; the fallback
        // would just be a leak.
        const int pid = 91006;
        var agentId = _manager.AllocateSubAgentId();
        var cwd = Path.Combine(Path.GetTempPath(), "subagent-evict");
        var pipeName = $"PSMCP.{_manager.ProxyPid}.{agentId}.{pid}";

        _manager.SetActivePipeName(agentId, pipeName);
        _manager.SetLastAiCwd(agentId, pid, cwd);

        _manager.ClearDeadPipe(agentId, pipeName);

        Assert.Null(_manager.GetSessionLastAiCwd(agentId));
    }
}
