using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// Tests the wait/notify protocol that hands a command result from the pipe
/// thread back to the waiting tool call, including the generation-counter guard
/// that stops a late completion from a previously timed-out command from falsely
/// satisfying a brand-new wait. Each test resets the counters first so order
/// cannot leak state.
/// </summary>
[Collection("McpServerState")]
public class PowerShellCommunicationTests
{
    [Fact]
    public void WaitForResult_NoCompletion_TimesOut()
    {
        PowerShellCommunication.ResetGenerationsForTests();

        var (isTimeout, shouldCache) = PowerShellCommunication.WaitForResult(0);

        Assert.True(isTimeout);
        Assert.False(shouldCache);
    }

    [Fact]
    public async Task WaitForResult_IsSatisfiedByCompletion()
    {
        PowerShellCommunication.ResetGenerationsForTests();

        // A completion arrives shortly after the wait begins.
        var notifier = Task.Run(() =>
        {
            Thread.Sleep(150);
            PowerShellCommunication.NotifySilentResultReady("done");
        });

        // WaitForResult blocks the calling thread, so run it off the test thread.
        var (isTimeout, _) = await Task.Run(() => PowerShellCommunication.WaitForResult(10));
        await notifier;

        Assert.False(isTimeout);
        ExecutionState.ConsumeCachedOutputs(); // clear what the notify cached
    }

    [Fact]
    public void GenerationCounter_StaleCompletion_DoesNotSatisfyNewWait()
    {
        PowerShellCommunication.ResetGenerationsForTests();

        // 1) A command's wait times out.
        Assert.True(PowerShellCommunication.WaitForResult(0).isTimeout);

        // 2) That timed-out command completes late (a stray completion lands).
        PowerShellCommunication.NotifySilentResultReady("late");
        ExecutionState.ConsumeCachedOutputs();

        // 3) A brand-new wait must NOT be satisfied by the stale completion —
        //    the generation counter requires a completion for *this* generation.
        Assert.True(PowerShellCommunication.WaitForResult(0).isTimeout);
    }
}
