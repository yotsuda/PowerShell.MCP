using Moq;
using PowerShell.MCP.Proxy.Models;
using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

public class PipeDiscoveryServiceTests
{
    private readonly Mock<IPowerShellService> _mockPowerShellService;
    private readonly PipeDiscoveryService _service;
    // Allocate a unique sub-agent id per test instance (xunit creates one per test
    // method) so concurrent test classes touching the static ConsoleSessionManager.Instance
    // — which keys all state by agent id — can't trample each other. A previous run hit
    // a flaky failure on Windows where another suite''s "default" entry had cleared this
    // class''s active-pipe registration mid-test. The variable name stays for diff clarity.
    private readonly string DefaultAgentId;

    public PipeDiscoveryServiceTests()
    {
        _mockPowerShellService = new Mock<IPowerShellService>();
        _service = new PipeDiscoveryService(_mockPowerShellService.Object);
        DefaultAgentId = ConsoleSessionManager.Instance.AllocateSubAgentId();
    }

    #region DetectClosedConsoles Tests

    [Fact]
    public void DetectClosedConsoles_NoPreviouslyBusyPids_ReturnsEmptyList()
    {
        // When no PIDs were previously marked as busy, should return empty
        var result = _service.DetectClosedConsoles(DefaultAgentId);
        Assert.Empty(result);
    }

    #endregion

    #region FindReadyPipeAsync Tests

    [Fact]
    public async Task FindReadyPipeAsync_NoPipes_ReturnsNullPipeName()
    {
        // Setup: No pipes available
        var result = await _service.FindReadyPipeAsync(DefaultAgentId, CancellationToken.None);

        Assert.Null(result.ReadyPipeName);
        Assert.False(result.ConsoleSwitched);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeStandby_ReturnsActivePipe()
    {
        // Setup: Active pipe is in standby state
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PSMCP.{proxyPid}.{DefaultAgentId}.99999";

        // Set the active pipe name (this requires the session manager to be aware of it)
        sessionManager.SetActivePipeName(DefaultAgentId, testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "standby", Pid = 99999 });

        var result = await _service.FindReadyPipeAsync(DefaultAgentId, CancellationToken.None);

        Assert.Equal(testPipeName, result.ReadyPipeName);
        Assert.False(result.ConsoleSwitched);

        // Cleanup
        sessionManager.ClearDeadPipe(DefaultAgentId, testPipeName);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeCompleted_ReturnsActivePipe()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PSMCP.{proxyPid}.{DefaultAgentId}.99998";

        sessionManager.SetActivePipeName(DefaultAgentId, testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "completed", Pid = 99998 });

        var result = await _service.FindReadyPipeAsync(DefaultAgentId, CancellationToken.None);

        Assert.Equal(testPipeName, result.ReadyPipeName);
        Assert.False(result.ConsoleSwitched);

        // Cleanup
        sessionManager.ClearDeadPipe(DefaultAgentId, testPipeName);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeDead_ReturnsClosedWithConsoleDisplayName()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        const int testPid = 99997;
        var testPipeName = $"PSMCP.{proxyPid}.{DefaultAgentId}.{testPid}";

        sessionManager.SetActivePipeName(DefaultAgentId, testPipeName);
        sessionManager.TryAssignNameToPid(testPid);
        var expectedDisplayName = sessionManager.GetConsoleDisplayName(testPid);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetStatusResponse?)null);

        var result = await _service.FindReadyPipeAsync(DefaultAgentId, CancellationToken.None);

        // Should have detected the closed console with display name, not raw pipe name
        Assert.Contains(result.ClosedConsoleMessages, m => m.Contains("was closed"));
        Assert.Contains(result.ClosedConsoleMessages, m => m.Contains(expectedDisplayName));
        Assert.DoesNotContain(result.ClosedConsoleMessages, m => m.Contains("PSMCP."));

        // Cleanup is automatic since ClearDeadPipe was called
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeBusy_RecordsStatusInfo()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PSMCP.{proxyPid}.{DefaultAgentId}.99996";

        sessionManager.SetActivePipeName(DefaultAgentId, testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse
            {
                Status = "busy",
                Pid = 99996,
                Pipeline = "Get-Process",
                Duration = 5.5
            });

        var result = await _service.FindReadyPipeAsync(DefaultAgentId, CancellationToken.None);

        // No ready pipe found since only pipe is busy
        Assert.Null(result.ReadyPipeName);
        Assert.NotNull(result.AllPipesStatusInfo);
        Assert.Contains("Get-Process", result.AllPipesStatusInfo);

        // Cleanup
        sessionManager.ClearDeadPipe(DefaultAgentId, testPipeName);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActiveSaysUserCommand_IsAskedOnceMore_BeforeBeingPassedOver()
    {
        // "(user command)" is the console's heartbeat guess, wrong for a moment
        // after a long command. A second look 150 ms later finds it free, so
        // the AI keeps its console instead of getting a new one.
        var sessionManager = ConsoleSessionManager.Instance;
        var testPipeName = $"PSMCP.{sessionManager.ProxyPid}.{DefaultAgentId}.99960";
        sessionManager.SetActivePipeName(DefaultAgentId, testPipeName);

        _mockPowerShellService
            .SetupSequence(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "busy", Pipeline = "(user command)", Pid = 99960, Duration = 11.06 })
            .ReturnsAsync(new GetStatusResponse { Status = "standby", Pid = 99960 });

        var result = await _service.FindReadyPipeAsync(DefaultAgentId, CancellationToken.None);

        Assert.Equal(testPipeName, result.ReadyPipeName);
        Assert.Null(result.ActiveNotFree);
        _mockPowerShellService.Verify(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()), Times.Exactly(2));

        sessionManager.ClearDeadPipe(DefaultAgentId, testPipeName);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActiveBusy_SaysSoInActiveNotFree_ByDisplayName()
    {
        // The AI's own command is still running there: no second look (that
        // is an observation, not a guess), and the result carries the reason
        // the tools put next to their "started a new console" notice.
        var sessionManager = ConsoleSessionManager.Instance;
        var testPipeName = $"PSMCP.{sessionManager.ProxyPid}.{DefaultAgentId}.99961";
        sessionManager.SetActivePipeName(DefaultAgentId, testPipeName);
        sessionManager.TryAssignNameToPid(99961);
        var displayName = sessionManager.GetConsoleDisplayName(99961);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "busy", Pipeline = "Start-Sleep 60", Pid = 99961, Duration = 3.2, StatusLine = "⧗ Pipeline is running | Status: Busy | Pipeline: Start-Sleep 60 | Duration: 3.20s" });

        var result = await _service.FindReadyPipeAsync(DefaultAgentId, CancellationToken.None);

        Assert.Null(result.ReadyPipeName);
        Assert.Equal($"{displayName} was busy (Start-Sleep 60, 3.2s)", result.ActiveNotFree);
        _mockPowerShellService.Verify(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()), Times.Once());

        sessionManager.ClearDeadPipe(DefaultAgentId, testPipeName);
    }

    [Fact]
    public void DescribeNotFree_SaysWhatTheConsoleWasDoing_InOneClause()
    {
        Assert.Equal("PID #1 Oak was busy (user command, 11.1s)",
            PipeDiscoveryService.DescribeNotFree("PID #1 Oak", new GetStatusResponse { Status = "busy", Pipeline = "(user command)", Duration = 11.06 }));
        Assert.Equal("PID #1 Oak was busy (Start-Sleep 25; \"long one d..., 9.5s)",
            PipeDiscoveryService.DescribeNotFree("PID #1 Oak", new GetStatusResponse { Status = "busy", Pipeline = "Start-Sleep 25; \"long one d...", Duration = 9.5 }));
        Assert.Equal("PID #1 Oak was waiting for input (Enter your name)",
            PipeDiscoveryService.DescribeNotFree("PID #1 Oak", new GetStatusResponse { Status = "awaiting_input", Message = "Enter your name", Duration = 4 }));
        Assert.Equal("PID #1 Oak was busy (busy)",
            PipeDiscoveryService.DescribeNotFree("PID #1 Oak", new GetStatusResponse { Status = "busy" }));
    }

    #endregion

    #region CollectAllCachedOutputsAsync Tests

    [Fact]
    public async Task CollectAllCachedOutputsAsync_NoPipes_ReturnsEmptyResults()
    {
        var result = await _service.CollectAllCachedOutputsAsync(DefaultAgentId, null, CancellationToken.None);

        Assert.Equal("", result.CompletedOutput);
        Assert.Equal("", result.BusyStatusInfo);
    }

    [Fact]
    public async Task CollectAllCachedOutputsAsync_ExcludesPipeName_SkipsExcluded()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PSMCP.{proxyPid}.{DefaultAgentId}.99995";

        sessionManager.SetActivePipeName(DefaultAgentId, testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "completed", Pid = 99995 });

        _mockPowerShellService
            .Setup(s => s.ConsumeOutputFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test output");

        // Exclude the pipe we just set up
        var result = await _service.CollectAllCachedOutputsAsync(DefaultAgentId, testPipeName, CancellationToken.None);

        // Should be empty because we excluded the only pipe
        Assert.Equal("", result.CompletedOutput);

        // Cleanup
        sessionManager.ClearDeadPipe(DefaultAgentId, testPipeName);
    }

    #endregion
}
