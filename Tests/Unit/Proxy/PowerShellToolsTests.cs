using Moq;
using PowerShell.MCP.Proxy.Models;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Tools;
using System.Text.Json;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// Unit tests for PowerShellTools.
/// These tests are possible because IPipeDiscoveryService is now injected via DI,
/// allowing us to mock pipe discovery and test tool method logic in isolation.
/// All tests run cross-platform (Windows/Linux/macOS) without real Named Pipes.
/// </summary>
public class PowerShellToolsTests
{
    private readonly Mock<IPowerShellService> _mockPowerShellService;
    private readonly Mock<IPipeDiscoveryService> _mockPipeDiscoveryService;
    // Allocate a unique sub-agent id per test instance (xunit creates one per test
    // method) so concurrent test classes touching the static ConsoleSessionManager.Instance
    // — which keys all state by agent id — can't trample each other. A previous run hit
    // a flaky failure on Windows where another suite''s "default" entry had cleared this
    // class''s active-pipe registration mid-test. Names kept for diff clarity.
    private readonly string TestAgentId;
    private readonly string TestPipeName;

    public PowerShellToolsTests()
    {
        _mockPowerShellService = new Mock<IPowerShellService>();
        _mockPipeDiscoveryService = new Mock<IPipeDiscoveryService>();
        TestAgentId = ConsoleSessionManager.Instance.AllocateSubAgentId();
        // Pipe name format: PSMCP.{proxyPid}.{agentId}.{pwshPid}. Match the agent id
        // so any code that re-derives agentId from the pipe name (or filters by it)
        // sees consistent values.
        TestPipeName = $"PSMCP.1000.{TestAgentId}.2000";
    }

    #region GetCurrentLocation Tests

    [Fact]
    public async Task GetCurrentLocation_ReadyPipe_ReturnsLocation()
    {
        // Arrange: pipe is ready
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\Users\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Location [FileSystem]", result);
        Assert.Contains("C:\\Users\\test", result);
    }

    [Fact]
    public async Task GetCurrentLocation_ReadyPipe_IncludesCachedOutputs()
    {
        // Arrange: pipe is ready with cached outputs from other pipes
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("Previous command output\n", ""));

        // Act
        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: TestAgentId);

        // Assert: cached output appears before location
        Assert.Contains("Previous command output", result);
        Assert.Contains("Location [FileSystem]", result);
        var cachedIndex = result.IndexOf("Previous command output");
        var locationIndex = result.IndexOf("Location [FileSystem]");
        Assert.True(cachedIndex < locationIndex, "Cached output should appear before location");
    }

    [Fact]
    public async Task GetCurrentLocation_ReadyPipe_IncludesBusyStatus()
    {
        // Arrange: pipe is ready but another pipe is busy
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", "⧗ | pwsh PID: 3000 | Status: Busy | Pipeline: long-task | Duration: 10.00s\n"));

        // Act
        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Status: Busy", result);
        Assert.Contains("long-task", result);
    }

    [Fact]
    public async Task GetCurrentLocation_PipeError_ReturnsErrorMessage()
    {
        // Arrange: pipe found but communication fails
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Pipe communication failed"));

        // Act
        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Failed to get current location", result);
        Assert.Contains("Pipe communication failed", result);
    }

    [Fact]
    public async Task GetCurrentLocation_SubAgentNewAllocation_ReadyPipe_EmitsAgentIdNotice()
    {
        // Sub-agent's first call must include the 🔑 agent_id notice in the response
        // so the AI knows which ID to pass to subsequent tool calls. Success-path
        // regression guard.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: null,
            is_subagent: true);

        Assert.Contains("🔑 Your agent_id is:", result);
    }

    [Fact]
    public async Task GetCurrentLocation_SubAgentNewAllocation_PipeError_StillEmitsAgentIdNotice()
    {
        // Regression: previously the error / timeout / cached-completed branches did
        // not emit 🔑. A sub-agent that hit any of those on its first call lost its
        // allocated ID forever. The wrap-every-return refactor closes that hole.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Pipe communication failed"));

        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: null,
            is_subagent: true);

        Assert.Contains("🔑 Your agent_id is:", result);
        Assert.Contains("Failed to get current location", result);
    }

    [Fact]
    public async Task GetCurrentLocation_DefaultAgent_DoesNotEmitAgentIdNotice()
    {
        // Non-sub-agent (default) calls must NOT carry the 🔑 notice — it would just
        // be noise. Verifies the wrap helper is a no-op when isNewlyAllocated=false.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object);

        Assert.DoesNotContain("🔑", result);
    }

    [Fact]
    public async Task GetCurrentLocation_ProvidedAgentId_DoesNotEmitAgentIdNotice()
    {
        // When the AI passes back a previously-issued agent_id, no allocation happens
        // so the 🔑 notice would be a duplicate. Must stay silent.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Use a valid sa-* shaped ID so IsValidAgentId returns true
        var validSubAgentId = ConsoleSessionManager.Instance.AllocateSubAgentId();
        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: validSubAgentId,
            is_subagent: true);

        Assert.DoesNotContain("🔑", result);
    }

    [Fact]
    public async Task GetCurrentLocation_ConsoleSwitched_SetsWindowTitle()
    {
        // Regression: GetCurrentLocation previously discarded FindReadyPipeAsync's
        // consoleSwitched flag with `_`, so a get_current_location call that
        // claimed an unowned (Import-Module-only) console left the window title
        // at the OnImport placeholder "#PID ____" instead of assigning a name.
        // Arrange: discovery reports a switched (newly-claimed) pipe.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, true, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPowerShellService
            .Setup(s => s.SetWindowTitleAsync(TestPipeName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: TestAgentId);

        // Assert: SetWindowTitleAsync must be invoked at least once for the
        // switched pipe. Title text itself is generated by ConsoleSessionManager
        // and varies per run, so accept any non-empty string.
        _mockPowerShellService.Verify(
            s => s.SetWindowTitleAsync(TestPipeName, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        Assert.Contains("Location [FileSystem]", result);
    }

    [Fact]
    public async Task GetCurrentLocation_NotSwitched_DoesNotSetWindowTitle()
    {
        // Companion guard: an established pipe (consoleSwitched=false) must not
        // re-trigger the title-assignment path. Setting the title every call
        // would either redo the round-trip wastefully (TryAssignNameToPid is
        // idempotent so it returns null) or — if the name dictionary ever
        // changes — risk renaming a console mid-session.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        await PowerShellTools.GetCurrentLocation(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: TestAgentId);

        // Assert
        _mockPowerShellService.Verify(
            s => s.SetWindowTitleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region InvokeExpression Tests

    [Fact]
    public async Task InvokeExpression_Success_ReturnsFormattedOutput()
    {
        // Arrange: pipe ready, command succeeds
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "Get-Date", duration = 0.15 });
        var statusLine = "✓ Pipeline executed successfully | Window: #2000 Cat | Status: Ready | Pipeline: Get-Date | Duration: 0.15s";
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "Get-Date", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n" + statusLine + "\n2025-01-15");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Pipeline executed successfully", result);
        Assert.Contains("2025-01-15", result);
    }

    [Fact]
    public async Task InvokeExpression_Timeout_ReturnsWaitInstruction()
    {
        // Arrange: command times out
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new
        {
            pid = 2000,
            status = "timeout",
            pipeline = "Start-Sleep 300",
            duration = 170.0,
            statusLine = "⧗ Pipeline is still running | Window: #2000 Cat | Status: Busy | Pipeline: Start-Sleep 300 | Duration: 170.00s"
        });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "Start-Sleep 300", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson);

        _mockPowerShellService
            .Setup(s => s.ConsumeOutputFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Start-Sleep 300",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Pipeline is still running", result);
        Assert.Contains("wait_for_completion", result);
    }

    [Fact]
    public async Task InvokeExpression_Completed_DrainsCurrentPipeInline()
    {
        // Arrange: shouldCache fired (DLL returned "completed" without body)
        // but the current invoke_expression handler is still servicing the
        // original MCP request, so we can drain the current pipe's cache
        // via consume_output and return the real content on THIS call.
        // Without this, the client would see a placeholder
        // "Result cached. Will be returned on next tool call." and have
        // to issue a second tool call just to get the result.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new
        {
            pid = 2000,
            status = "completed",
            pipeline = "Get-Process",
            duration = 5.2,
            statusLine = "✓ Pipeline completed (cached) | Window: #2000 Cat | Status: Completed"
        });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "Get-Process", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson);

        // consume_output drains the DLL's cache and returns the real body.
        const string drainedBody = "Idle      0     svchost\nIdle      4     System\n";
        _mockPowerShellService
            .Setup(s => s.ConsumeOutputFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(drainedBody);

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Process",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Pipeline completed (cached)", result);
        Assert.Contains("Idle      0     svchost", result);
        Assert.Contains("Idle      4     System", result);
        // The old placeholder must NOT appear when drain succeeded — it
        // was the UX bug the fix was designed to eliminate.
        Assert.DoesNotContain("Result cached. Will be returned on next tool call.", result);

        // Verify consume_output was actually called (and exactly once).
        _mockPowerShellService.Verify(
            s => s.ConsumeOutputFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeExpression_Completed_EmptyDrainFallsBackToPlaceholder()
    {
        // Arrange: shouldCache fired but by the time the Proxy's
        // consume_output call arrives, another drainer (e.g. a concurrent
        // wait_for_completion call) has already emptied the DLL's cache.
        // The response must still include the status line and a
        // fallback placeholder so the client isn't left staring at an
        // empty response — the drained content has gone somewhere, just
        // not here, and the client can find it on the next call.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new
        {
            pid = 2000,
            status = "completed",
            pipeline = "Get-Process",
            duration = 5.2,
            statusLine = "✓ Pipeline completed (cached) | Window: #2000 Cat | Status: Completed"
        });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "Get-Process", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson);

        // Simulate the race: consume_output returns empty.
        _mockPowerShellService
            .Setup(s => s.ConsumeOutputFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Process",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Pipeline completed (cached)", result);
        Assert.Contains("Result cached. Will be returned on next tool call.", result);
    }

    [Fact]
    public async Task InvokeExpression_ConsoleSwitched_ExecutesPipelineWithSwitchNotice()
    {
        // Arrange: console was switched (e.g. previous pipe was dead) — pre-1.9 this
        // returned early with "Pipeline NOT executed". 1.9+ falls through to execute
        // the pipeline on the new console and surfaces a "Switched to console" notice.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, true, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.SetWindowTitleAsync(TestPipeName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var headerJson = JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "Get-Date", duration = 0.01 });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "Get-Date", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert: pipeline executed (no "NOT executed") + switched notice surfaced
        Assert.Contains("Switched to console", result);
        Assert.DoesNotContain("Pipeline NOT executed", result);
    }

    [Fact]
    public async Task InvokeExpression_ScopeWarning_IncludedInOutput()
    {
        // Arrange: command with local variable assignment
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "$x = 1", duration = 0.01 });
        var statusLine = "✓ Pipeline executed successfully | Window: #2000 Cat | Status: Ready | Pipeline: $x = 1 | Duration: 0.01s";
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "$x = 1", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n" + statusLine);

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "$x = 1",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("SCOPE WARNING", result);
        Assert.Contains("$script:x", result);
    }

    [Fact]
    public async Task InvokeExpression_TimeoutClamped_To170()
    {
        // Arrange
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "test", duration = 0.01 });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "test", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act: pass timeout > 170
        await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "test",
            timeout_seconds: 999,
            agent_id: TestAgentId);

        // Assert: should have been clamped to 170
        _mockPowerShellService.Verify(
            s => s.InvokeExpressionToPipeAsync(TestPipeName, "test", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeExpression_ExecutionError_ReturnsErrorMessage()
    {
        // Arrange: pipe found but execution throws
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "bad-cmd", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection lost"));

        _mockPipeDiscoveryService
            .Setup(s => s.DetectClosedConsoles(It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(new List<string>());

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "bad-cmd",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Command execution failed", result);
        Assert.Contains("Connection lost", result);
    }

    [Fact]
    public async Task InvokeExpression_DefaultAgentId_UsesDefault()
    {
        // Arrange: no agent_id provided (should default to "default")
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync("default", It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "test", duration = 0.01 });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "test", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync("default", TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act: agent_id = null
        await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "test");

        // Assert: FindReadyPipeAsync called with "default"
        _mockPipeDiscoveryService.Verify(
            s => s.FindReadyPipeAsync("default", It.IsAny<CancellationToken>(), It.IsAny<bool>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeExpression_ClosedConsoleInfo_IncludedInSwitchedResponse()
    {
        // Arrange: console switched with closed console info. The closed-console
        // banner from FindReadyPipeAsync must propagate even now that the pipeline
        // is executed in-band on the switched console.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(
                TestPipeName, true, new List<string> { "  - ⚠ Console PID #5000 was closed" },
                null));

        _mockPowerShellService
            .Setup(s => s.SetWindowTitleAsync(TestPipeName, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var headerJson = JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "Get-Date", duration = 0.01 });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "Get-Date", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Console PID #5000 was closed", result);
        Assert.Contains("Switched to console", result);
    }

    [Fact]
    public async Task InvokeExpression_ExecutionError_ShowsConsoleDisplayName()
    {
        // Arrange: pipe found but execution throws — error message should contain console display name, not raw pipe name
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        const int testPid = 88801;
        var pipeName = $"PSMCP.{proxyPid}.{TestAgentId}.{testPid}";

        // Assign a friendly name so we can verify it appears in the output
        sessionManager.SetActivePipeName(TestAgentId, pipeName);
        sessionManager.TryAssignNameToPid(testPid);
        var expectedDisplayName = sessionManager.GetConsoleDisplayName(testPid);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, "bad-cmd", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"PowerShell.MCP module communication to console {expectedDisplayName} failed for command: bad-cmd"));

        _mockPipeDiscoveryService
            .Setup(s => s.DetectClosedConsoles(It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(new List<string>());

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "bad-cmd",
            agent_id: TestAgentId);

        // Assert: response contains console display name, not raw pipe name
        Assert.Contains(expectedDisplayName, result);
        Assert.Contains("was closed", result);
        Assert.DoesNotContain("PSMCP.", result);

        // Cleanup
        sessionManager.ClearDeadPipe(TestAgentId, pipeName);
    }

    [Fact]
    public async Task InvokeExpression_ExecutionError_IncludesOtherClosedConsoles()
    {
        // Arrange: execution fails, and DetectClosedConsoles finds additional closed consoles
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "fail-cmd", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection lost"));

        _mockPipeDiscoveryService
            .Setup(s => s.DetectClosedConsoles(It.IsAny<string>(), It.IsAny<int?>()))
            .Returns(new List<string> { "  - ⚠ Console PID #9999 was closed" });

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "fail-cmd",
            agent_id: TestAgentId);

        // Assert: both the failed console and the other closed console appear
        Assert.Contains("PID #2000", result); // the console that threw
        Assert.Contains("PID #9999 was closed", result); // additional closed console
        Assert.Contains("Please try again", result);

        // Cleanup
        ConsoleSessionManager.Instance.ClearDeadPipe(TestAgentId, TestPipeName);
    }

    [Fact]
    public async Task InvokeExpression_ClosedConsoleMessages_IncludedInNonSwitchedResponse()
    {
        // Arrange: not switched, but ClosedConsoleMessages has entries (detected during discovery)
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(
                TestPipeName, false,
                new List<string> { "  - ⚠ Console PID #7777 was closed" },
                null));

        var headerJson = System.Text.Json.JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "Get-Date", duration = 0.1 });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, "Get-Date", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n✓ done\n2025-01-15");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert: closed console info is included even on success path
        Assert.Contains("PID #7777 was closed", result);
    }

    #endregion

    #region WaitForCompletion Tests

    [Fact]
    public async Task WaitForCompletion_NoBusyConsoles_ReturnsNoCommands()
    {
        // Arrange: no busy consoles, no cached output
        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.WaitForCompletion(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            timeout_seconds: 1,
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("No commands to wait for completion", result);
    }

    [Fact]
    public async Task WaitForCompletion_PreviouslyBusyPidDisappeared_ReturnsClosedMessage()
    {
        // Arrange: a PID was previously marked busy, but EnumeratePipes returns empty (pipe gone)
        // This triggers the "previously busy PID disappeared" detection path
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88802;

        // Use a unique agent ID to avoid race with parallel test classes (e.g. PipeDiscoveryServiceTests)
        // that also call ConsumeKnownBusyPids on the singleton with "default" agent ID
        var isolatedAgentId = sessionManager.AllocateSubAgentId();

        sessionManager.TryAssignNameToPid(testPid);
        var expectedDisplayName = sessionManager.GetConsoleDisplayName(testPid);
        sessionManager.MarkPipeBusy(isolatedAgentId, testPid);

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act: EnumeratePipes returns no real pipes, so busy PID 88802 is detected as gone
        var result = await PowerShellTools.WaitForCompletion(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            timeout_seconds: 1,
            agent_id: isolatedAgentId);

        // Assert: console display name (not raw pipe name) in closed message
        Assert.Contains(expectedDisplayName, result);
        Assert.Contains("was closed", result);
    }

    #endregion

    #region Cwd tracking, drift detection, auto-cd

    [Fact]
    public async Task InvokeExpression_Success_UpdatesLastAiCwd()
    {
        // Arrange: a successful invoke_expression should snapshot jsonResponse.Cwd
        // into ConsoleSessionManager so the next call's drift check has a baseline.
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88810;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var targetCwd = Path.Combine(Path.GetTempPath(), "lastai-success");

        // Clear any stale state from previous test runs
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, null));

        var headerJson = JsonSerializer.Serialize(new { pid = testPid, status = "success", pipeline = "Get-Date", duration = 0.01, cwd = targetCwd });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, "Get-Date", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert: LastAiCwd recorded for this pid
        Assert.Equal(targetCwd, sessionManager.GetLastAiCwd(testPid));

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    [Fact]
    public async Task InvokeExpression_Timeout_UpdatesLastAiCwd()
    {
        // Arrange: a timeout response should also snapshot jsonResponse.Cwd —
        // the pipeline is mid-execution but its cwd is still where AI was working.
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88815;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var midExecCwd = Path.Combine(Path.GetTempPath(), "lastai-timeout");

        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, null));

        var headerJson = JsonSerializer.Serialize(new
        {
            pid = testPid,
            status = "timeout",
            pipeline = "Start-Sleep 300",
            duration = 170.0,
            statusLine = "⧗ Pipeline is still running",
            cwd = midExecCwd
        });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, "Start-Sleep 300", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n");

        _mockPowerShellService
            .Setup(s => s.ConsumeOutputFromPipeAsync(pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Start-Sleep 300",
            agent_id: TestAgentId);

        // Assert
        Assert.Equal(midExecCwd, sessionManager.GetLastAiCwd(testPid));

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    [Fact]
    public async Task InvokeExpression_Completed_UpdatesLastAiCwd()
    {
        // Arrange: cached "completed" response should also snapshot jsonResponse.Cwd.
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88816;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var completedCwd = Path.Combine(Path.GetTempPath(), "lastai-completed");

        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, null));

        var headerJson = JsonSerializer.Serialize(new
        {
            pid = testPid,
            status = "completed",
            pipeline = "Get-Process",
            duration = 5.2,
            statusLine = "✓ Pipeline completed (cached)",
            cwd = completedCwd
        });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, "Get-Process", It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n");

        _mockPowerShellService
            .Setup(s => s.ConsumeOutputFromPipeAsync(pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Process",
            agent_id: TestAgentId);

        // Assert
        Assert.Equal(completedCwd, sessionManager.GetLastAiCwd(testPid));

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    [Fact]
    public async Task InvokeExpression_CwdDrift_BailsWithoutExecuting()
    {
        // Arrange: AI's last cwd != live cwd (user typed `cd` interactively).
        // The proxy must NOT execute the pipeline — running silently at the
        // user's cwd could trigger destructive ops at the wrong place. Instead
        // it bails with a notice, updates LastAiCwd to liveCwd (clearing the
        // user-cd state), and the AI re-issues to either accept the new cwd
        // or prepend Set-Location to revert.
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88811;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var aiCwd = Path.Combine(Path.GetTempPath(), "ai-intended");
        var liveCwd = Path.Combine(Path.GetTempPath(), "user-typed-cd");

        sessionManager.SetLastAiCwd(TestAgentId, testPid, aiCwd);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, liveCwd));

        // Track whether InvokeExpressionToPipeAsync was called — it must NOT be
        var executed = false;
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback(() => executed = true)
            .ReturnsAsync("");

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Remove-Item *.tmp",
            agent_id: TestAgentId);

        // Assert: pipeline NOT executed, notice describes the change
        Assert.False(executed, "Pipeline must not execute when user-cd drift is detected");
        Assert.Contains("Pipeline NOT executed", result);
        Assert.Contains("User changed cwd", result);
        Assert.Contains(aiCwd, result);
        Assert.Contains(liveCwd, result);
        Assert.Contains("Re-issue", result);

        // LastAiCwd updated to liveCwd so a re-issue runs without re-detecting drift
        Assert.Equal(liveCwd, sessionManager.GetLastAiCwd(testPid));

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    [Fact]
    public async Task InvokeExpression_NoCwdDrift_ExecutesPipelineVerbatim()
    {
        // Arrange: live cwd == LastAiCwd → no drift, pipeline runs as sent
        // and no bail-out notice surfaces.
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88812;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var sameCwd = Path.Combine(Path.GetTempPath(), "no-drift");

        sessionManager.SetLastAiCwd(TestAgentId, testPid, sameCwd);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, sameCwd));

        string? sentPipeline = null;
        var headerJson = JsonSerializer.Serialize(new { pid = testPid, status = "success", pipeline = "Get-Date", duration = 0.01, cwd = sameCwd });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>?, int, CancellationToken>((_, p, _, _, _) => sentPipeline = p)
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert: pipeline sent verbatim, no drift bail-out
        Assert.Equal("Get-Date", sentPipeline);
        Assert.DoesNotContain("Pipeline NOT executed", result);
        Assert.DoesNotContain("User changed cwd", result);

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    [Fact]
    public async Task InvokeExpression_NoLastAiCwd_ExecutesPipelineVerbatim()
    {
        // Arrange: fresh console — no LastAiCwd recorded yet. Even if liveCwd
        // is reported, drift detection must skip the bail-out (no baseline to
        // compare against).
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88813;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var liveCwd = Path.Combine(Path.GetTempPath(), "fresh-console");

        sessionManager.SetLastAiCwd(TestAgentId, testPid, null); // ensure no stale state

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, liveCwd));

        string? sentPipeline = null;
        var headerJson = JsonSerializer.Serialize(new { pid = testPid, status = "success", pipeline = "Get-Date", duration = 0.01, cwd = liveCwd });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>?, int, CancellationToken>((_, p, _, _, _) => sentPipeline = p)
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert: no preamble — first call has nothing to compare against
        Assert.Equal("Get-Date", sentPipeline);

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    [Fact]
    public async Task InvokeExpression_CwdDriftReissue_ExecutesAtLiveCwd()
    {
        // Arrange: simulate the second invoke_expression after a drift bail.
        // The first bail updated LastAiCwd to liveCwd, so on this re-issue
        // liveCwd == LastAiCwd → no drift → pipeline runs verbatim at the
        // user's new cwd. Verifies the user-cd state is properly cleared.
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88815;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var liveCwd = Path.Combine(Path.GetTempPath(), "user-set-workspace");

        // Simulate post-bail state: LastAiCwd was just updated to liveCwd
        sessionManager.SetLastAiCwd(TestAgentId, testPid, liveCwd);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, liveCwd));

        string? sentPipeline = null;
        var headerJson = JsonSerializer.Serialize(new { pid = testPid, status = "success", pipeline = "Get-Date", duration = 0.01, cwd = liveCwd });
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .Callback<string, string, Dictionary<string, string>?, int, CancellationToken>((_, p, _, _, _) => sentPipeline = p)
            .ReturnsAsync(headerJson + "\n\n✓ done");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), pipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert: pipeline ran verbatim, no second bail
        Assert.Equal("Get-Date", sentPipeline);
        Assert.DoesNotContain("Pipeline NOT executed", result);
        Assert.DoesNotContain("User changed cwd", result);

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    [Fact]
    public async Task InvokeExpression_CwdDriftNotice_EscapesSingleQuotesInRevertHint()
    {
        // Arrange: directory name with apostrophe in AI's intended cwd. The
        // bail notice tells the AI how to Set-Location back via a single-
        // quoted literal path, so apostrophes must be doubled or the AI
        // would copy a parse-error inducing snippet.
        var sessionManager = ConsoleSessionManager.Instance;
        const int testPid = 88814;
        var pipeName = $"PSMCP.{sessionManager.ProxyPid}.{TestAgentId}.{testPid}";
        var aiCwd = Path.Combine(Path.GetTempPath(), "o'brien");
        var liveCwd = Path.Combine(Path.GetTempPath(), "elsewhere");

        sessionManager.SetLastAiCwd(TestAgentId, testPid, aiCwd);

        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(pipeName, false, new List<string>(), null, liveCwd));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert: apostrophe doubled inside the literal-path argument.
        // Build the expected substring from aiCwd itself so the test passes on every
        // platform — Path.GetTempPath() returns /tmp/... on Linux and /var/folders/...
        // on macOS, neither of which starts with "C:". The escape rule (' -> '') is
        // what we actually want to lock in.
        var expectedRevertHint = $"Set-Location -LiteralPath '{aiCwd.Replace("'", "''")}'";
        Assert.Contains("Pipeline NOT executed", result);
        Assert.Contains(expectedRevertHint, result);
        Assert.Contains("o''brien", result);

        // Cleanup
        sessionManager.SetLastAiCwd(TestAgentId, testPid, null);
    }

    #endregion

    #region StartConsole unowned-claim gating

    [Fact]
    public async Task StartConsole_NoStartLocation_SkipsUnownedClaim()
    {
        // Arrange: no start_location → caller hasn't pinned a target cwd, so
        // claiming an arbitrary user-set unowned console would mislead the AI.
        // FindReadyPipeAsync must be invoked with includeUnowned=false.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act: no start_location
        await PowerShellTools.StartConsole(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            agent_id: TestAgentId);

        // Assert: includeUnowned=false in the discovery call
        _mockPipeDiscoveryService.Verify(
            s => s.FindReadyPipeAsync(TestAgentId, It.IsAny<CancellationToken>(), false),
            Times.Once);
    }

    [Fact]
    public async Task StartConsole_WithStartLocation_AllowsUnownedClaim()
    {
        // Arrange: start_location pinned → AI has expressed an intended cwd, so
        // unowned claim is safe. FindReadyPipeAsync must be invoked with
        // includeUnowned=true.
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        _mockPowerShellService
            .Setup(s => s.GetCurrentLocationFromPipeAsync(TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Location [FileSystem]: C:\\test");

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act: with start_location
        await PowerShellTools.StartConsole(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            start_location: Path.GetTempPath(),
            agent_id: TestAgentId);

        // Assert: includeUnowned=true in the discovery call
        _mockPipeDiscoveryService.Verify(
            s => s.FindReadyPipeAsync(TestAgentId, It.IsAny<CancellationToken>(), true),
            Times.Once);
    }

    #endregion
}