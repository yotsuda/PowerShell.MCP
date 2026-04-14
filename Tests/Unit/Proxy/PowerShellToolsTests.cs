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
    private const string TestAgentId = "default";
    private const string TestPipeName = "PSMCP.1000.default.2000";

    public PowerShellToolsTests()
    {
        _mockPowerShellService = new Mock<IPowerShellService>();
        _mockPipeDiscoveryService = new Mock<IPipeDiscoveryService>();
    }

    #region GetCurrentLocation Tests

    [Fact]
    public async Task GetCurrentLocation_ReadyPipe_ReturnsLocation()
    {
        // Arrange: pipe is ready
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

    #endregion

    #region InvokeExpression Tests

    [Fact]
    public async Task InvokeExpression_Success_ReturnsFormattedOutput()
    {
        // Arrange: pipe ready, command succeeds
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
    public async Task InvokeExpression_ConsoleSwitched_ReturnsLocationAndNotExecutedMessage()
    {
        // Arrange: console was switched (e.g. previous pipe was dead)
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            "Get-Date",
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("Switched to console", result);
        Assert.Contains("Pipeline NOT executed", result);
        Assert.Contains("Location [FileSystem]", result);
    }

    [Fact]
    public async Task InvokeExpression_ScopeWarning_IncludedInOutput()
    {
        // Arrange: command with local variable assignment
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync("default", It.IsAny<CancellationToken>()))
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
            s => s.FindReadyPipeAsync("default", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokeExpression_ClosedConsoleInfo_IncludedInSwitchedResponse()
    {
        // Arrange: console switched with closed console info
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipeDiscoveryResult(
                TestPipeName, true, new List<string>(),
                "  - ⚠ Console PID #5000 was closed"));

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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
}