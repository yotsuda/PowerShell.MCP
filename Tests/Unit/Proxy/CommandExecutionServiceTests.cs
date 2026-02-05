using Moq;
using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

public class CommandExecutionServiceTests
{
    private readonly Mock<IPowerShellService> _mockPowerShellService;
    private readonly CommandExecutionService _service;

    public CommandExecutionServiceTests()
    {
        _mockPowerShellService = new Mock<IPowerShellService>();
        _service = new CommandExecutionService(_mockPowerShellService.Object);
    }

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_SuccessResponse_ReturnsSuccessResult()
    {
        var pipeName = "TestPipe";
        var pipeline = "Get-Date";
        var jsonResponse = """{"status":"success","pid":1234,"duration":0.5,"pipeline":"Get-Date"}""";
        var body = "2024-01-15";
        var fullResponse = $"{jsonResponse}\n\n{body}";

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, pipeline, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullResponse);

        var result = await _service.ExecuteAsync(pipeName, pipeline, 170, CancellationToken.None);

        Assert.Equal(ExecutionResultType.Success, result.Type);
        Assert.Equal("2024-01-15", result.Output);
        Assert.Equal(1234, result.Pid);
    }

    [Fact]
    public async Task ExecuteAsync_BusyResponse_ReturnsBusyResult()
    {
        var pipeName = "TestPipe";
        var pipeline = "Get-Process";
        var jsonResponse = """{"status":"busy","pid":1234,"duration":5.5,"pipeline":"Get-Process","reason":"user_command"}""";

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, pipeline, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var result = await _service.ExecuteAsync(pipeName, pipeline, 170, CancellationToken.None);

        Assert.Equal(ExecutionResultType.Busy, result.Type);
        Assert.Equal("user_command", result.BusyReason);
        Assert.Equal(1234, result.Pid);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutResponse_ReturnsTimeoutResult()
    {
        var pipeName = "TestPipe";
        var pipeline = "Start-Sleep 300";
        var jsonResponse = """{"status":"timeout","pid":1234,"duration":170.0,"pipeline":"Start-Sleep 300","statusLine":"⧗ Pipeline is still running"}""";

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, pipeline, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var result = await _service.ExecuteAsync(pipeName, pipeline, 170, CancellationToken.None);

        Assert.Equal(ExecutionResultType.Timeout, result.Type);
        Assert.Equal("⧗ Pipeline is still running", result.StatusLine);
    }

    [Fact]
    public async Task ExecuteAsync_CompletedResponse_ReturnsCompletedResult()
    {
        var pipeName = "TestPipe";
        var pipeline = "Get-Date";
        var jsonResponse = """{"status":"completed","pid":1234,"duration":1.0,"pipeline":"Get-Date"}""";

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, pipeline, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jsonResponse);

        var result = await _service.ExecuteAsync(pipeName, pipeline, 170, CancellationToken.None);

        Assert.Equal(ExecutionResultType.Completed, result.Type);
    }

    [Fact]
    public async Task ExecuteAsync_RawTextResponse_ReturnsSuccessWithRawOutput()
    {
        var pipeName = "TestPipe";
        var pipeline = "echo hello";
        var rawResponse = "hello\nworld";

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, pipeline, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawResponse);

        var result = await _service.ExecuteAsync(pipeName, pipeline, 170, CancellationToken.None);

        Assert.Equal(ExecutionResultType.Success, result.Type);
        Assert.Equal(rawResponse, result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsSuccessWithRawOutput()
    {
        var pipeName = "TestPipe";
        var pipeline = "Get-Date";
        var invalidJson = "{invalid json}";

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, pipeline, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invalidJson);

        var result = await _service.ExecuteAsync(pipeName, pipeline, 170, CancellationToken.None);

        Assert.Equal(ExecutionResultType.Success, result.Type);
        Assert.Equal(invalidJson, result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_Exception_ReturnsErrorResult()
    {
        var pipeName = "TestPipe";
        var pipeline = "Get-Date";

        _mockPowerShellService
            .Setup(s => s.InvokeExpressionToPipeAsync(pipeName, pipeline, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection failed"));

        var result = await _service.ExecuteAsync(pipeName, pipeline, 170, CancellationToken.None);

        Assert.Equal(ExecutionResultType.Error, result.Type);
        Assert.Contains("Connection failed", result.Output);
    }

    #endregion

    #region CheckVariableScopeWarning Tests

    [Fact]
    public void CheckVariableScopeWarning_NoVariables_ReturnsNull()
    {
        var result = _service.CheckVariableScopeWarning("Get-Date");
        Assert.Null(result);
    }

    [Fact]
    public void CheckVariableScopeWarning_LocalVariable_ReturnsWarning()
    {
        var result = _service.CheckVariableScopeWarning("$foo = 123");
        Assert.NotNull(result);
        Assert.Contains("SCOPE WARNING", result);
    }

    [Fact]
    public void CheckVariableScopeWarning_ScriptScoped_ReturnsNull()
    {
        var result = _service.CheckVariableScopeWarning("$script:foo = 123");
        Assert.Null(result);
    }

    #endregion
}