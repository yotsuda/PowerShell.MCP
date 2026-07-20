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

        var (isTimeout, shouldCache, awaitingInput, _) = PowerShellCommunication.WaitForResult(0);

        Assert.True(isTimeout);
        Assert.False(shouldCache);
        Assert.False(awaitingInput);
    }

    [Fact]
    public void WaitForResult_IsSatisfiedByCompletion()
    {
        PowerShellCommunication.ResetGenerationsForTests();

        // A completion arrives shortly after the wait begins; WaitForResult must
        // wake on it well before the 10s timeout.
        var (isTimeout, _, _, _) = WaitWhileSignaling(10,
            () => PowerShellCommunication.NotifySilentResultReady("done"));

        Assert.False(isTimeout);
        ExecutionState.ConsumeCachedOutputs(); // clear what the notify cached
    }

    [Fact]
    public void WaitForResult_SignalAwaitingInput_ReturnsAwaitingImmediately()
    {
        PowerShellCommunication.ResetGenerationsForTests();

        // The command reaches an interactive prompt shortly after the wait begins;
        // WaitForResult must wake on the signal and report awaitingInput with the
        // prompt text — not isTimeout.
        var (isTimeout, _, awaitingInput, promptText) = WaitWhileSignaling(10,
            () => PowerShellCommunication.SignalAwaitingInput("Read-Host"));

        Assert.False(isTimeout);
        Assert.True(awaitingInput);
        Assert.Equal("Read-Host", promptText);

        PowerShellCommunication.ClearAwaitingInput();
    }

    /// <summary>
    /// Runs WaitForResult on a DEDICATED thread and fires <paramref name="signal"/>
    /// from the (dedicated) test thread after a short head-start, then returns the
    /// wait's result. Both sides deliberately stay off the thread pool: the wait
    /// blocks a thread for up to the timeout, and under a saturated pool — the
    /// default net8.0+net9.0 concurrent run, or CI's parallel jobs — a pool-
    /// scheduled signaler could be injected too late and the wait would time out
    /// spuriously. That thread-pool starvation, not any product bug, is what made
    /// these tests flaky. `new Thread` and the xUnit test thread are scheduled by
    /// the OS, so neither is subject to the pool's slow thread injection. The
    /// head-start lets the waiter enter the wait (and bump the generation counter)
    /// before the signal, preserving the original wait-then-signal ordering.
    /// </summary>
    private static (bool isTimeout, bool shouldCache, bool awaitingInput, string? promptText)
        WaitWhileSignaling(int timeoutSeconds, Action signal)
    {
        (bool isTimeout, bool shouldCache, bool awaitingInput, string? promptText) captured = default;
        var waiter = new Thread(() =>
        {
            var r = PowerShellCommunication.WaitForResult(timeoutSeconds);
            captured = (r.isTimeout, r.shouldCache, r.awaitingInput, r.promptText);
        })
        { IsBackground = true, Name = "WaitForResult-under-test" };
        waiter.Start();

        Thread.Sleep(150);
        signal();

        Assert.True(waiter.Join(TimeSpan.FromSeconds(timeoutSeconds + 5)),
            "WaitForResult did not return after the signal.");
        return captured; // waiter.Join happens-before this read, so captured is visible
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
