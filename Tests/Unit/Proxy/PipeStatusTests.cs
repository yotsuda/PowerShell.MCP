using PowerShell.MCP.Proxy.Models;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// PipeStatus.IsReady is the single string-level definition of "ready" — not
/// busy, so the proxy may route to the console now. It is shared by the
/// new-standby probe (PowerShellProcessManager), the readiness wait
/// (NamedPipeClient.WaitForPipeReadyAsync), and discovery
/// (GetStatusResponseExtensions.IsReady). standby = idle; completed = has
/// undrained output but still routable; anything else (busy / unknown / null)
/// is not ready.
/// </summary>
public class PipeStatusTests
{
    [Theory]
    [InlineData("standby")]
    [InlineData("completed")]
    public void IsReady_TrueForStandbyAndCompleted(string status)
        => Assert.True(PipeStatus.IsReady(status));

    [Theory]
    [InlineData("busy")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Standby")]   // case-sensitive: the wire value is lowercase
    [InlineData("garbage")]
    public void IsReady_FalseForBusyNullOrUnknown(string? status)
        => Assert.False(PipeStatus.IsReady(status));

    [Fact]
    public void IsReady_UsesTheStatusConstants()
    {
        Assert.True(PipeStatus.IsReady(PipeStatus.Standby));
        Assert.True(PipeStatus.IsReady(PipeStatus.Completed));
        Assert.False(PipeStatus.IsReady(PipeStatus.Busy));
    }
}
