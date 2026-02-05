using Moq;
using PowerShell.MCP.Proxy.Models;
using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

public class PipeDiscoveryServiceTests
{
    private readonly Mock<IPowerShellService> _mockPowerShellService;
    private readonly PipeDiscoveryService _service;

    public PipeDiscoveryServiceTests()
    {
        _mockPowerShellService = new Mock<IPowerShellService>();
        _service = new PipeDiscoveryService(_mockPowerShellService.Object);
    }

    #region DetectClosedConsoles Tests

    [Fact]
    public void DetectClosedConsoles_NoPreviouslyBusyPids_ReturnsEmptyList()
    {
        // When no PIDs were previously marked as busy, should return empty
        var result = _service.DetectClosedConsoles();
        Assert.Empty(result);
    }

    #endregion

    #region FindReadyPipeAsync Tests

    [Fact]
    public async Task FindReadyPipeAsync_NoPipes_ReturnsNullPipeName()
    {
        // Setup: No pipes available
        var result = await _service.FindReadyPipeAsync(CancellationToken.None);
        
        Assert.Null(result.ReadyPipeName);
        Assert.False(result.ConsoleSwitched);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeStandby_ReturnsActivePipe()
    {
        // Setup: Active pipe is in standby state
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PowerShell.MCP.Communication.{proxyPid}.99999";
        
        // Set the active pipe name (this requires the session manager to be aware of it)
        sessionManager.SetActivePipeName(testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "standby", Pid = 99999 });

        var result = await _service.FindReadyPipeAsync(CancellationToken.None);

        Assert.Equal(testPipeName, result.ReadyPipeName);
        Assert.False(result.ConsoleSwitched);

        // Cleanup
        sessionManager.ClearDeadPipe(testPipeName);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeCompleted_ReturnsActivePipe()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PowerShell.MCP.Communication.{proxyPid}.99998";
        
        sessionManager.SetActivePipeName(testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "completed", Pid = 99998 });

        var result = await _service.FindReadyPipeAsync(CancellationToken.None);

        Assert.Equal(testPipeName, result.ReadyPipeName);
        Assert.False(result.ConsoleSwitched);

        // Cleanup
        sessionManager.ClearDeadPipe(testPipeName);
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeDead_ReturnsNull()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PowerShell.MCP.Communication.{proxyPid}.99997";
        
        sessionManager.SetActivePipeName(testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetStatusResponse?)null);

        var result = await _service.FindReadyPipeAsync(CancellationToken.None);

        // Should have detected the closed console
        Assert.Contains(result.ClosedConsoleMessages, m => m.Contains("was closed"));

        // Cleanup is automatic since ClearDeadPipe was called
    }

    [Fact]
    public async Task FindReadyPipeAsync_ActivePipeBusy_RecordsStatusInfo()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PowerShell.MCP.Communication.{proxyPid}.99996";
        
        sessionManager.SetActivePipeName(testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse 
            { 
                Status = "busy", 
                Pid = 99996, 
                Pipeline = "Get-Process",
                Duration = 5.5
            });

        var result = await _service.FindReadyPipeAsync(CancellationToken.None);

        // No ready pipe found since only pipe is busy
        Assert.Null(result.ReadyPipeName);
        Assert.NotNull(result.AllPipesStatusInfo);
        Assert.Contains("Get-Process", result.AllPipesStatusInfo);

        // Cleanup
        sessionManager.ClearDeadPipe(testPipeName);
    }

    #endregion

    #region CollectAllCachedOutputsAsync Tests

    [Fact]
    public async Task CollectAllCachedOutputsAsync_NoPipes_ReturnsEmptyResults()
    {
        var result = await _service.CollectAllCachedOutputsAsync(null, CancellationToken.None);

        Assert.Equal("", result.CompletedOutput);
        Assert.Equal("", result.BusyStatusInfo);
    }

    [Fact]
    public async Task CollectAllCachedOutputsAsync_ExcludesPipeName_SkipsExcluded()
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var proxyPid = sessionManager.ProxyPid;
        var testPipeName = $"PowerShell.MCP.Communication.{proxyPid}.99995";
        
        sessionManager.SetActivePipeName(testPipeName);

        _mockPowerShellService
            .Setup(s => s.GetStatusFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetStatusResponse { Status = "completed", Pid = 99995 });

        _mockPowerShellService
            .Setup(s => s.ConsumeOutputFromPipeAsync(testPipeName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test output");

        // Exclude the pipe we just set up
        var result = await _service.CollectAllCachedOutputsAsync(testPipeName, CancellationToken.None);

        // Should be empty because we excluded the only pipe
        Assert.Equal("", result.CompletedOutput);

        // Cleanup
        sessionManager.ClearDeadPipe(testPipeName);
    }

    #endregion
}