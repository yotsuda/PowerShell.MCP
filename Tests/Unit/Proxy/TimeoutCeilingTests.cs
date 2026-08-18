using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// The ceiling decision has to stay below whatever the connected client is
/// willing to wait for, and it is the only thing standing between a
/// slow-but-successful pipeline and an output nobody receives. These tests
/// exercise the pure resolver rather than ObserveClient so they never touch
/// the process-wide ceiling other tests read.
/// </summary>
public class TimeoutCeilingTests
{
    [Fact]
    public void Resolve_ClaudeCode_UsesRelaxedCeiling()
    {
        // Measured: Claude Code does not abort a long call in its default
        // configuration, and sends notifications/cancelled when it does.
        Assert.Equal(TimeoutCeiling.RelaxedSeconds, TimeoutCeiling.Resolve("claude-code", null));
    }

    [Theory]
    [InlineData("Claude-Code")]
    [InlineData("CLAUDE-CODE")]
    public void Resolve_ClientNameMatchIsCaseInsensitive(string clientName)
    {
        Assert.Equal(TimeoutCeiling.RelaxedSeconds, TimeoutCeiling.Resolve(clientName, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("claude-desktop")]
    [InlineData("cursor")]
    [InlineData("some-future-client")]
    public void Resolve_UnknownOrAbsentClient_FallsBackToConservativeCeiling(string? clientName)
    {
        // The failure direction matters: an unrecognised client costs one
        // extra round trip, never a lost result.
        Assert.Equal(TimeoutCeiling.ConservativeSeconds, TimeoutCeiling.Resolve(clientName, null));
    }

    [Fact]
    public void Resolve_ConservativeCeilingSitsBelowTheSdkDefaultTimeout()
    {
        // The MCP TypeScript SDK ships DEFAULT_REQUEST_TIMEOUT_MSEC = 60000,
        // so any client that has not overridden it gives up at 60s. The
        // default ceiling must leave room for console claim/spawn before the
        // command starts and response assembly after it ends.
        Assert.True(TimeoutCeiling.ConservativeSeconds <= 50,
            "The default ceiling must stay far enough under the 60s SDK default to absorb per-call overhead.");
    }

    [Fact]
    public void Resolve_EnvOverride_WinsOverClientDetection()
    {
        Assert.Equal(90, TimeoutCeiling.Resolve("claude-code", "90"));
        Assert.Equal(90, TimeoutCeiling.Resolve("some-future-client", "90"));
    }

    [Fact]
    public void Resolve_EnvOverride_IsHeldToTheDllHardMaximum()
    {
        Assert.Equal(TimeoutCeiling.HardMaxSeconds, TimeoutCeiling.Resolve(null, "9999"));
    }

    [Fact]
    public void Remaining_ChargesConsoleStartupAgainstTheCeiling()
    {
        // A cold spawn was measured at ~15s before the pipeline even starts.
        // Left uncharged, a 50s-capped command answered at 65s — past the 60s
        // an unconfigured client waits.
        Assert.Equal(35, TimeoutCeiling.Remaining(170, TimeSpan.FromSeconds(15), 50));
    }

    [Fact]
    public void Remaining_KeepsARequestSmallerThanTheBudget()
    {
        Assert.Equal(10, TimeoutCeiling.Remaining(10, TimeSpan.FromSeconds(15), 50));
    }

    [Fact]
    public void Remaining_ZeroRequestStaysZero()
    {
        // timeout_seconds: 0 is the "native CLI waiting on stdin" escape and
        // must keep returning at once.
        Assert.Equal(0, TimeoutCeiling.Remaining(0, TimeSpan.Zero, 50));
    }

    [Fact]
    public void Remaining_ExhaustedBudgetStillDispatchesWithTheFloor()
    {
        // Even when startup ate the whole ceiling, the caller asked for a
        // command to run, not for an instant refusal.
        Assert.Equal(TimeoutCeiling.MinimumDispatchSeconds,
            TimeoutCeiling.Remaining(170, TimeSpan.FromSeconds(600), 50));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Resolve_MalformedEnvOverride_IsIgnoredRatherThanTreatedAsZero(string envValue)
    {
        // A typo'd override must not collapse the ceiling to nothing and turn
        // every command into an immediate timeout.
        Assert.Equal(TimeoutCeiling.ConservativeSeconds, TimeoutCeiling.Resolve(null, envValue));
        Assert.Equal(TimeoutCeiling.RelaxedSeconds, TimeoutCeiling.Resolve("claude-code", envValue));
    }
}
