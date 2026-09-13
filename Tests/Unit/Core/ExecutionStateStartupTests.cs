using System;
using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// The startup window of a proxy-launched console. A heartbeat gap there is
/// startup work still running; reporting it as a user command made the proxy
/// auto-route away from a console it had spawned seconds earlier and start a
/// second one. The window closes on the first heartbeat after the first prompt,
/// and must still expire, so a console wedged before that ends up reported as
/// busy.
/// </summary>
[Collection("McpServerState")]
public class ExecutionStateStartupTests : IDisposable
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public ExecutionStateStartupTests()
    {
        ExecutionState.StartupClock = () => _now;
    }

    public void Dispose()
    {
        ExecutionState.StartupClock = () => DateTime.UtcNow;
        ExecutionState.MarkStartupComplete();
        ExecutionState.Heartbeat();
    }

    // Stale by more than the 10s user-command threshold, so WaitForHeartbeat
    // decides on the spot instead of polling for a fresh tick.
    private static void StallHeartbeat() =>
        ExecutionState.SetLastHeartbeatForTests(DateTime.UtcNow.AddSeconds(-20));

    [Fact]
    public void StaleHeartbeat_OutsideStartup_IsStillReportedAsBusy()
    {
        ExecutionState.MarkStartupComplete();
        StallHeartbeat();

        Assert.False(ExecutionState.IsRunspaceAvailable);
        Assert.False(ExecutionState.WaitForHeartbeat());
    }

    [Fact]
    public void StaleHeartbeat_DuringStartup_IsNotReportedAsAUserCommand()
    {
        ExecutionState.BeginStartup();
        StallHeartbeat();

        Assert.True(ExecutionState.IsStartingUp);
        Assert.True(ExecutionState.IsRunspaceAvailable);
        Assert.True(ExecutionState.WaitForHeartbeat());
    }

    [Fact]
    public void HeartbeatsBeforeThePrompt_DoNotEndTheWindow()
    {
        // The engine also ticks at pumping points inside the startup command.
        ExecutionState.BeginStartup();
        ExecutionState.Heartbeat();
        StallHeartbeat();

        Assert.True(ExecutionState.IsStartingUp);
        Assert.True(ExecutionState.WaitForHeartbeat());
    }

    [Fact]
    public void PromptRender_AloneDoesNotEndTheWindow()
    {
        // After rendering the prompt the host still enters ReadLine, where
        // PSReadLine loads its history; a stall there is startup work too.
        ExecutionState.BeginStartup();
        ExecutionState.MarkFirstPromptRendered();
        StallHeartbeat();

        Assert.True(ExecutionState.IsStartingUp);
        Assert.True(ExecutionState.WaitForHeartbeat());
    }

    [Fact]
    public void HeartbeatAfterTheFirstPrompt_EndsTheWindow()
    {
        ExecutionState.BeginStartup();
        ExecutionState.MarkFirstPromptRendered();
        ExecutionState.Heartbeat();
        StallHeartbeat();

        Assert.False(ExecutionState.IsStartingUp);
        Assert.False(ExecutionState.WaitForHeartbeat());
    }

    [Fact]
    public void BeginStartup_ForgetsAPromptFromAnEarlierImport()
    {
        // A profile that imported the module first, then the launcher's -Force
        // re-import: the earlier console's prompt must not close the new window.
        ExecutionState.MarkFirstPromptRendered();
        ExecutionState.BeginStartup();
        ExecutionState.Heartbeat();

        Assert.True(ExecutionState.IsStartingUp);
    }

    [Fact]
    public void ImportByHand_StartsOutComplete()
    {
        ExecutionState.BeginStartup();
        ExecutionState.MarkStartupComplete();
        StallHeartbeat();

        Assert.False(ExecutionState.IsStartingUp);
        Assert.False(ExecutionState.WaitForHeartbeat());
    }

    [Fact]
    public void StartupWindow_StaysOpenUntilTheGraceRunsOut()
    {
        ExecutionState.BeginStartup();
        _now = _now.AddMilliseconds(ExecutionState.StartupGraceMs - 1000);
        StallHeartbeat();

        Assert.True(ExecutionState.IsStartingUp);
        Assert.True(ExecutionState.WaitForHeartbeat());
    }

    [Fact]
    public void StartupWindow_ExpiresForALaunchThatNeverReachesAPrompt()
    {
        ExecutionState.BeginStartup();
        _now = _now.AddMilliseconds(ExecutionState.StartupGraceMs + 1000);
        StallHeartbeat();

        Assert.False(ExecutionState.IsStartingUp);
        Assert.False(ExecutionState.IsRunspaceAvailable);
        Assert.False(ExecutionState.WaitForHeartbeat());
    }
}
