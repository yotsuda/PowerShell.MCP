using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using PowerShell.MCP.Services;

namespace PowerShell.MCP.Tests;

/// <summary>
/// Tests for the idle-standby auto-reap decision logic (#53). ConsoleLiveness is
/// a process-global static with a real clock and real marker files, so these
/// tests: (a) run serially — the [Collection] plus the fact that they all touch
/// the same statics; (b) drive time through the injected <c>UtcNow</c> seam so
/// warn/grace elapse instantly and deterministically; (c) reset state and delete
/// the on-disk group dir in Dispose. The whole feature is deliberately biased
/// toward NOT reaping (any doubt keeps the console), so most tests assert that a
/// guard returns <see cref="ReapAction.None"/>.
/// </summary>
[Collection("ConsoleLiveness serial")]
public class ConsoleLivenessTests : IDisposable
{
    // Real values, so the tests exercise the same thresholds as production; the
    // clock seam lets us cross them without waiting.
    private const int Warn = 600;
    private const int Grace = 60;

    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly int _ownPid = Process.GetCurrentProcess().Id;
    private readonly string _liveRoot =
        Path.Combine(Path.GetTempPath(), "PowerShell.MCP.Tests", Guid.NewGuid().ToString("N"));
    private string? _groupDir;
    private Process? _sleeper;

    public ConsoleLivenessTests()
    {
        ConsoleLiveness.ResetForTests();
        ConsoleLiveness.UtcNow = () => _now;
        // Isolate marker files in a private root: the shared LocalApplicationData
        // root is scanned and pruned by every SetOwned (ours and any concurrent
        // test process's), which would delete our planted markers mid-test.
        ConsoleLiveness.LiveRootForTests = _liveRoot;
    }

    public void Dispose()
    {
        ConsoleLiveness.SetUnowned();
        ConsoleLiveness.ResetForTests(); // restores the real clock
        ConsoleLiveness.LiveRootForTests = null;
        if (_sleeper != null) { try { if (!_sleeper.HasExited) _sleeper.Kill(); _sleeper.Dispose(); } catch { } }
        try { Directory.Delete(_liveRoot, recursive: true); } catch { }
    }

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    // Claims this console under a throwaway session and records the on-disk group
    // dir for sibling-planting and cleanup. A large random proxyPid guarantees we
    // never share a group dir with a real PowerShell.MCP console on this machine.
    private void Own()
    {
        int proxyPid = 1_000_000_000 + Math.Abs(Guid.NewGuid().GetHashCode() % 100_000_000);
        string agentId = "test-" + Guid.NewGuid().ToString("N");
        ConsoleLiveness.SetOwned(proxyPid, agentId);
        _groupDir = ConsoleLiveness.GroupDirForTests(proxyPid, agentId);
    }

    // Plants a sibling marker that is strictly newer than our last activity and
    // owned by a genuinely-live process, so IsKeeper elects the sibling and this
    // console becomes reapable. We spawn our own sleeper for the live PID rather
    // than borrowing an arbitrary one, so the sibling can't vanish mid-test.
    private void MakeReapableByNewerSibling()
    {
        Assert.NotNull(_groupDir);
        _sleeper = StartSleeper();
        long ticks = _now.Ticks + TimeSpan.TicksPerSecond;
        File.WriteAllText(Path.Combine(_groupDir!, _sleeper.Id.ToString()), ticks.ToString());
    }

    private static Process StartSleeper()
    {
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
            : new ProcessStartInfo("sleep", "60");
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        return Process.Start(psi)!;
    }

    // ── Guards: none of these may ever reap ──────────────────────────────────

    [Fact]
    public void Reap_Disabled_WhenWarnSecondsNonPositive()
    {
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + Grace + 100);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(0, Grace, runspaceAvailable: true, statusStandby: true));
    }

    [Fact]
    public void Reap_None_WhenNotOwned()
    {
        // No Own() call: an unowned (user-started / released) console never reaps.
        Advance(Warn + Grace + 100);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, runspaceAvailable: true, statusStandby: true));
    }

    [Fact]
    public void Reap_None_WhenRunspaceBusy()
    {
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + Grace + 100);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, runspaceAvailable: false, statusStandby: true));
    }

    [Fact]
    public void Reap_None_WhenNotStandby()
    {
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + Grace + 100);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, runspaceAvailable: true, statusStandby: false));
    }

    [Fact]
    public void Reap_None_WhenIdleBelowThreshold()
    {
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn - 1);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, runspaceAvailable: true, statusStandby: true));
    }

    [Fact]
    public void Reap_None_LoneConsoleIsAlwaysKeeper()
    {
        // The single-survivor guarantee: with no siblings, an idle standby console
        // is its own keeper and must never reap itself.
        Own();
        Advance(Warn + Grace + 100);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, runspaceAvailable: true, statusStandby: true));
    }

    [Fact]
    public void Reap_None_WhenTypedTextPresent()
    {
        // A console the user is mid-typing in stays open even though it is idle,
        // standby, and out-ranked by a newer sibling.
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + Grace + 100);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true, typedTextPresent: true));
    }

    [Fact]
    public void Reap_TypedTextVetoesWithoutCountingAsActivity()
    {
        // Regression for the keeper-theft bug: the buffer check used to call
        // RecordActivity, which ALSO re-stamped the marker on every ~2s tick. A
        // console left with a half-typed line therefore became the permanent
        // keeper and reaped every sibling — including the console the AI was
        // actively working in. The veto must keep THIS console alive without
        // making it look recently used.
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + 5);

        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true, typedTextPresent: true));
        Assert.True(ConsoleLiveness.IdleSeconds >= Warn,
            "typed text must not reset the idle clock — that is what would steal the keeper election");
    }

    [Fact]
    public void Reap_ResumesOnceTypedTextIsCleared()
    {
        // The veto is a live condition, not a latch: clearing the line (Esc,
        // Ctrl+C, backspace) makes the console reapable again on the next tick.
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + 5);

        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true, typedTextPresent: true));
        Assert.Equal(ReapAction.Warn, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true, typedTextPresent: false));
    }

    [Fact]
    public void Reap_TypedTextDuringGraceCancelsClose()
    {
        // The warning's own promise — "just start typing to keep it open".
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + 5);

        Assert.Equal(ReapAction.Warn, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));
        Assert.True(ConsoleLiveness.IsReapPending);

        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true, typedTextPresent: true));
        Assert.False(ConsoleLiveness.IsReapPending);

        // Still held open well past what would have been the close deadline.
        Advance(Grace + 1);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true, typedTextPresent: true));
    }

    // ── The warn → grace → close state machine ───────────────────────────────

    [Fact]
    public void Reap_FirstEligibleTickWarns_ThenClosesAfterGrace()
    {
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + 5); // idle past threshold

        Assert.Equal(ReapAction.Warn, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));
        Assert.True(ConsoleLiveness.IsReapPending);

        Advance(Grace - 1);
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true)); // still in grace

        Advance(2); // now past the grace window
        Assert.Equal(ReapAction.Close, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));
    }

    [Fact]
    public void Reap_GraceRunsFromWarning_NotFromThreshold()
    {
        // Regression for the bug fixed in EvaluateReap: a console that only becomes
        // eligible LONG after the idle threshold (it was busy / holding output the
        // whole time) must still get the FULL grace after its warning — not close
        // on the very next tick because idle already exceeds warn+grace.
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + Grace + 10_000); // eligible only now, idle already >> warn+grace

        // First eligible evaluation warns; it must NOT jump straight to Close.
        Assert.Equal(ReapAction.Warn, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));

        // Immediately after the warning, no time has passed since it — still in grace.
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));

        // Only a further `grace` from the WARNING closes it.
        Advance(Grace + 1);
        Assert.Equal(ReapAction.Close, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));
    }

    [Fact]
    public void Reap_ActivityDuringGraceCancelsClose()
    {
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + 5);

        Assert.Equal(ReapAction.Warn, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));
        Assert.True(ConsoleLiveness.IsReapPending);

        // Use the console mid-grace: idle resets, so the next evaluation returns
        // to None and the pending close is cancelled. (Recording activity also
        // makes this the most-recently-active console — the keeper — which is the
        // correct reason a just-used console stops being a reap candidate.)
        ConsoleLiveness.RecordActivity();
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));
        Assert.False(ConsoleLiveness.IsReapPending);
    }

    [Fact]
    public void Reap_LeavingStandbyDuringGraceResetsWarning()
    {
        Own();
        MakeReapableByNewerSibling();
        Advance(Warn + 5);
        Assert.Equal(ReapAction.Warn, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true));

        // A command starts during grace (runspace busy): the warning is cleared,
        // so when the console goes idle again it gets a fresh full warning first.
        Assert.Equal(ReapAction.None, ConsoleLiveness.EvaluateReap(Warn, Grace, runspaceAvailable: false, statusStandby: true));
        Assert.False(ConsoleLiveness.IsReapPending);

        Advance(Grace + 100); // even well past the old grace...
        Assert.Equal(ReapAction.Warn, ConsoleLiveness.EvaluateReap(Warn, Grace, true, true)); // ...it warns, not closes
    }

    // ── Marker file lifecycle ────────────────────────────────────────────────

    [Fact]
    public void Owning_WritesMarker_Unowning_DeletesIt()
    {
        Own();
        var marker = Path.Combine(_groupDir!, _ownPid.ToString());
        Assert.True(File.Exists(marker));
        Assert.Equal(_now.Ticks, long.Parse(File.ReadAllText(marker)));

        ConsoleLiveness.SetUnowned();
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public void RecordActivity_RefreshesMarkerTimestamp()
    {
        Own();
        var marker = Path.Combine(_groupDir!, _ownPid.ToString());
        long first = long.Parse(File.ReadAllText(marker));

        Advance(123);
        ConsoleLiveness.RecordActivity();

        long second = long.Parse(File.ReadAllText(marker));
        Assert.True(second > first);
        Assert.Equal(_now.Ticks, second);
    }

    [Fact]
    public void IdleSeconds_TracksTheInjectedClock()
    {
        Own();
        ConsoleLiveness.RecordActivity();
        Assert.True(ConsoleLiveness.IdleSeconds < 1);

        Advance(300);
        Assert.Equal(300, ConsoleLiveness.IdleSeconds, precision: 1);
    }
}
