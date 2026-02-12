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
    private const string TestAgentId = "test-agent";
    private const string TestPipeName = "PowerShell.MCP.Communication.1000.test-agent.2000";

    public PowerShellToolsTests()
    {
        _mockPowerShellService = new Mock<IPowerShellService>();
        _mockPipeDiscoveryService = new Mock<IPipeDiscoveryService>();
    }

    #region GenerateAgentId Tests

    [Fact]
    public void GenerateAgentId_ReturnsEightCharHexString()
    {
        var id = PowerShellTools.GenerateAgentId();
        Assert.Equal(8, id.Length);
        Assert.Matches("^[0-9a-f]{8}$", id);
    }

    [Fact]
    public void GenerateAgentId_ReturnsUniqueValues()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => PowerShellTools.GenerateAgentId()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    #endregion

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
    public async Task InvokeExpression_Completed_ReturnsCachedMessage()
    {
        // Arrange: result was cached from a previous run
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
        Assert.Contains("Result cached", result);
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
    public async Task InvokeExpression_MultiLineCommand_IncludesHistoryWarning()
    {
        // Arrange: multi-line command
        var multiLineCmd = "if ($true) {\n    Get-Date\n}";
        _mockPipeDiscoveryService
            .Setup(s => s.FindReadyPipeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PipeDiscoveryResult(TestPipeName, false, new List<string>(), null));

        var headerJson = JsonSerializer.Serialize(new { pid = 2000, status = "success", pipeline = "if ($true) {...", duration = 0.05 });
        var statusLine = "✓ Pipeline executed successfully | Window: #2000 Cat | Status: Ready";
        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(TestPipeName, multiLineCmd, It.IsAny<Dictionary<string, string>?>(), 170, It.IsAny<CancellationToken>()))
            .ReturnsAsync(headerJson + "\n\n" + statusLine);

        _mockPipeDiscoveryService
            .Setup(s => s.CollectAllCachedOutputsAsync(It.IsAny<string>(), TestPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CachedOutputResult("", ""));

        // Act
        var result = await PowerShellTools.InvokeExpression(
            _mockPowerShellService.Object,
            _mockPipeDiscoveryService.Object,
            multiLineCmd,
            agent_id: TestAgentId);

        // Assert
        Assert.Contains("HISTORY NOTE", result);
        Assert.Contains("Multi-line command", result);
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
                "  - ⚠ Console PID 5000 was closed"));

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
        Assert.Contains("Console PID 5000 was closed", result);
        Assert.Contains("Switched to console", result);
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

    #endregion
}