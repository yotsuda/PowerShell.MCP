using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

public class ResponseBuilderTests
{
    [Fact]
    public void Build_EmptyBuilder_ReturnsEmptyString()
    {
        var builder = new ResponseBuilder();
        Assert.Equal("", builder.Build());
    }

    [Fact]
    public void BuildOrDefault_EmptyBuilder_ReturnsDefaultMessage()
    {
        var builder = new ResponseBuilder();
        Assert.Equal("No results.", builder.BuildOrDefault("No results."));
    }

    [Fact]
    public void BuildOrDefault_NonEmptyBuilder_ReturnsContent()
    {
        var builder = new ResponseBuilder();
        builder.AddMessage("Hello");
        Assert.Equal("Hello", builder.BuildOrDefault("No results."));
    }

    [Fact]
    public void AddBusyStatus_WithContent_AddsStatusWithNewline()
    {
        var builder = new ResponseBuilder();
        builder.AddBusyStatus("⧗ Pipeline is running");
        var result = builder.Build();
        Assert.StartsWith("⧗ Pipeline is running", result);
        Assert.EndsWith("\n", result);
    }

    [Fact]
    public void AddBusyStatus_NullOrEmpty_DoesNothing()
    {
        var builder = new ResponseBuilder();
        builder.AddBusyStatus(null);
        builder.AddBusyStatus("");
        Assert.Equal("", builder.Build());
    }

    [Fact]
    public void AddClosedConsoleMessages_WithMessages_AddsAllMessages()
    {
        var builder = new ResponseBuilder();
        var messages = new[] { "⚠ Console PID 123 was closed", "⚠ Console PID 456 was closed" };
        builder.AddClosedConsoleMessages(messages);
        var result = builder.Build();
        Assert.Contains("Console PID 123", result);
        Assert.Contains("Console PID 456", result);
    }

    [Fact]
    public void AddClosedConsoleMessages_EmptyList_DoesNothing()
    {
        var builder = new ResponseBuilder();
        builder.AddClosedConsoleMessages(Array.Empty<string>());
        Assert.Equal("", builder.Build());
    }

    [Fact]
    public void AddClosedConsoleMessages_Null_DoesNothing()
    {
        var builder = new ResponseBuilder();
        builder.AddClosedConsoleMessages(null);
        Assert.Equal("", builder.Build());
    }

    [Fact]
    public void AddCompletedOutput_WithContent_AddsOutput()
    {
        var builder = new ResponseBuilder();
        builder.AddCompletedOutput("Output line 1\nOutput line 2");
        var result = builder.Build();
        Assert.Contains("Output line 1", result);
        Assert.Contains("Output line 2", result);
    }

    [Fact]
    public void AddScopeWarning_WithWarning_AddsWarningWithNewlines()
    {
        var builder = new ResponseBuilder();
        builder.AddScopeWarning("⚠️ SCOPE WARNING: Local variable detected");
        var result = builder.Build();
        Assert.Contains("SCOPE WARNING", result);
    }

    [Fact]
    public void AddMessage_AddsRawMessage()
    {
        var builder = new ResponseBuilder();
        builder.AddMessage("Raw message");
        Assert.Equal("Raw message", builder.Build());
    }

    [Fact]
    public void AddLocationResult_AddsLocationData()
    {
        var builder = new ResponseBuilder();
        builder.AddLocationResult("{\"current_location\": \"C:\\\\\"}");
        Assert.Equal("{\"current_location\": \"C:\\\\\"}", builder.Build());
    }

    [Fact]
    public void AddWaitForCompletionHint_AddsHintMessage()
    {
        var builder = new ResponseBuilder();
        builder.AddWaitForCompletionHint();
        var result = builder.Build();
        Assert.Contains("wait_for_completion", result);
    }

    [Fact]
    public void FluentChaining_BuildsCorrectResponse()
    {
        var result = new ResponseBuilder()
            .AddBusyStatus("⧗ Running")
            .AddCompletedOutput("Previous output")
            .AddMessage("Current result")
            .Build();

        Assert.Contains("⧗ Running", result);
        Assert.Contains("Previous output", result);
        Assert.Contains("Current result", result);
    }

    [Fact]
    public void ComplexResponse_MaintainsCorrectOrder()
    {
        var result = new ResponseBuilder()
            .AddBusyStatus("Status: Busy")
            .AddClosedConsoleMessages(new[] { "Closed: 123" })
            .AddCompletedOutput("Completed output")
            .AddScopeWarning("Scope warning")
            .AddMessage("Final message")
            .Build();

        // Verify order: busy status should come before closed messages
        var busyIndex = result.IndexOf("Status: Busy");
        var closedIndex = result.IndexOf("Closed: 123");
        var completedIndex = result.IndexOf("Completed output");
        var warningIndex = result.IndexOf("Scope warning");
        var messageIndex = result.IndexOf("Final message");

        Assert.True(busyIndex < closedIndex, "Busy status should come before closed messages");
        Assert.True(closedIndex < completedIndex, "Closed messages should come before completed output");
        Assert.True(completedIndex < warningIndex, "Completed output should come before warning");
        Assert.True(warningIndex < messageIndex, "Warning should come before final message");
    }
}