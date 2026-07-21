using System;
using Xunit;
using PowerShell.MCP.Proxy.Services;

namespace PowerShell.MCP.Tests;

/// <summary>
/// Tests for close_console's human-presence gate. The guard holds process-global
/// arm state and a clock seam, so these reset both in the ctor and Dispose, and
/// drive the TTL through the seam instead of sleeping.
///
/// The guard's bias is the inverse of the auto-reap path: reaping fails toward
/// keeping consoles alive, this fails toward closing them, because close_console
/// is the escape hatch for a console that is stuck or unreachable. Most tests
/// here therefore assert Proceed.
/// </summary>
public class CloseConsoleGuardTests : IDisposable
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string PipeA = "PowerShell.MCP.1234.default.1000";
    private const string PipeB = "PowerShell.MCP.1234.sub-abc.1000";

    public CloseConsoleGuardTests()
    {
        CloseConsoleGuard.ResetForTests();
        CloseConsoleGuard.UtcNow = () => _now;
    }

    public void Dispose() => CloseConsoleGuard.ResetForTests();

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    private static CloseDecision Eval(string pipe, bool typed, double age = 1, bool probeOk = true)
        => CloseConsoleGuard.Evaluate(pipe, typed, age, probeOk);

    // ── The two-step confirmation ────────────────────────────────────────────

    [Fact]
    public void FirstAttemptRefusesWhenSomeoneIsTyping()
    {
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
    }

    [Fact]
    public void SecondAttemptProceeds()
    {
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
        // The retry IS the confirmation — no force parameter exists.
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: true));
    }

    [Fact]
    public void ArmIsConsumed_SoAThirdCloseChecksAgain()
    {
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: true));
        // A fresh console reusing this pipe name must not inherit the old
        // confirmation.
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
    }

    [Fact]
    public void ArmExpiresAfterTtl()
    {
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
        Advance(CloseConsoleGuard.ArmTtlSeconds + 1);
        // A refusal from earlier in the session must not turn a later first
        // attempt into an instant kill.
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
    }

    [Fact]
    public void ArmSurvivesWithinTtl()
    {
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
        Advance(CloseConsoleGuard.ArmTtlSeconds - 1);
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: true));
    }

    [Fact]
    public void DisarmClearsPendingConfirmation()
    {
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
        // e.g. the AI ran a command in it, contradicting the intent to abandon.
        CloseConsoleGuard.Disarm(PipeA);
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
    }

    // ── Agent isolation ──────────────────────────────────────────────────────

    [Fact]
    public void ArmIsScopedPerPipe_NotPerPid()
    {
        // Same pwsh pid, different agent: the pipe name carries the agent id, so
        // one sub-agent's refusal must never confirm another's close.
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
        Assert.Equal(CloseDecision.Refuse, Eval(PipeB, typed: true));
    }

    // ── Fail open: unknown presence must never block the escape hatch ────────

    [Fact]
    public void ProceedsWhenNobodyIsTyping()
    {
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: false));
    }

    [Fact]
    public void ProceedsWhenProbeFailed()
    {
        // Unreachable / mid-teardown console — the very case close_console exists for.
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: true, probeOk: false));
    }

    [Fact]
    public void ProceedsWhenSampleWasNeverTaken()
    {
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: true, age: -1));
    }

    [Fact]
    public void ProceedsWhenSampleIsStale()
    {
        // A long-dead engine's last value must not veto closes forever.
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: true, age: CloseConsoleGuard.MaxSampleAgeSeconds + 1));
    }

    [Fact]
    public void FailOpenPathsDoNotArm()
    {
        // A Proceed that came from fail-open must not leave an arm behind, or the
        // NEXT close would skip a check that never actually ran.
        Assert.Equal(CloseDecision.Proceed, Eval(PipeA, typed: true, probeOk: false));
        Assert.Equal(CloseDecision.Refuse, Eval(PipeA, typed: true));
    }
}
