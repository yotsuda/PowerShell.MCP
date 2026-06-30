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

    // ===== IsBusy =====
    // IsBusy is the busy-side counterpart of IsReady, shared by
    // wait_for_completion's poll set and the busy-status collector. The
    // load-bearing case is awaiting_input: a command parked at a host prompt is
    // a sub-state of busy. These pin the regression where the callers switched
    // on the literal statuses and forgot awaiting_input, so wait_for_completion
    // returned "No commands to wait for completion." instead of waiting for the
    // human to answer.

    [Theory]
    [InlineData("busy")]
    [InlineData("awaiting_input")]   // a command parked at a host prompt is busy
    public void IsBusy_TrueForBusyAndAwaitingInput(string status)
        => Assert.True(PipeStatus.IsBusy(status));

    [Theory]
    [InlineData("standby")]
    [InlineData("completed")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Busy")]   // case-sensitive: the wire value is lowercase
    [InlineData("garbage")]
    public void IsBusy_FalseForReadyNullOrUnknown(string? status)
        => Assert.False(PipeStatus.IsBusy(status));

    [Fact]
    public void AwaitingInput_CountsAsBusy_NotReady()
    {
        // The exact contract the bug violated: an awaiting-input console must
        // count as busy (so wait_for_completion polls/waits for it until the
        // prompt is answered or the timeout elapses) and must NOT count as ready
        // (so discovery never routes a fresh command onto a console blocked on a
        // human prompt).
        Assert.True(PipeStatus.IsBusy(PipeStatus.AwaitingInput));
        Assert.False(PipeStatus.IsReady(PipeStatus.AwaitingInput));
    }

    [Fact]
    public void Busy_And_Ready_AreMutuallyExclusive_ForEveryKnownStatus()
    {
        foreach (var status in new[]
                 {
                     PipeStatus.Standby,
                     PipeStatus.Busy,
                     PipeStatus.Completed,
                     PipeStatus.AwaitingInput,
                 })
        {
            Assert.False(
                PipeStatus.IsBusy(status) && PipeStatus.IsReady(status),
                $"Status '{status}' must not be classified as both busy and ready.");
        }
    }
}
