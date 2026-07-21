using System.Diagnostics;

namespace PowerShell.MCP.Services;

/// <summary>
/// Result of an idle-standby reap evaluation. Drives the polling engine:
/// <see cref="Warn"/> prints the one-minute warning, <see cref="Close"/> means
/// the grace elapsed with no use and the console should close itself.
/// </summary>
public enum ReapAction { None, Warn, Close }

/// <summary>
/// Cross-process liveness coordination for standby-console auto-reaping.
///
/// <para>Each OWNED console publishes its own last-activity timestamp into a
/// small per-(proxy, agent) directory of marker files — one file per console
/// PID, whose content is <c>DateTime.UtcNow.Ticks</c>. A console only ever
/// writes its OWN file and reads the others', so there is no write contention
/// and no lock file is needed. This shared directory is the "shared memory":
/// the ONLY channel used to coordinate reaping — there is no round-trip to the
/// proxy.</para>
///
/// <para>The polling engine uses <see cref="EvaluateReap"/> to decide, purely
/// locally, whether an idle standby console should close itself. The console
/// whose marker is the most recent (the de-facto active one) is the "keeper"
/// and always survives; the stale siblings reap. This guarantees exactly one
/// survivor per session with no proxy involvement.</para>
///
/// <para>The marker directory is resolved under the per-user
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/> (writable
/// without admin on every OS), NOT the system temp dir or the current
/// directory. Everything here is best-effort: any I/O failure leaves the
/// console alive (<see cref="EvaluateReap"/> never returns Close when the
/// group directory can't be read), so a locked-down or unwritable data dir
/// degrades to "never auto-reap" rather than to a wrongful close.</para>
/// </summary>
public static class ConsoleLiveness
{
    private static readonly object _lock = new();
    private static readonly int _pid = Process.GetCurrentProcess().Id;

    // Clock seam: overridable only from tests (via InternalsVisibleTo) so idle
    // and grace timing can be advanced deterministically instead of with real
    // waits. Production always runs on the real UTC clock. Declared before any
    // field whose initializer reads it so static init order stays correct.
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
    private static long NowTicks() => UtcNow().Ticks;

    private static long _lastActivityTicks = NowTicks();

    // Identity of the owning session; null when this console is unowned.
    // Unowned consoles (user-started, or released after a disconnect) never
    // publish a marker and are never reaped.
    private static int? _proxyPid;
    private static string? _agentId;
    private static string? _markerPath;

    // Whether the one-minute close warning has already been shown for the
    // current idle stretch, and when it was shown. Single console per process,
    // so one flag suffices. The grace is measured from _warnedAtTicks rather
    // than from the idle threshold: a console can first become eligible to reap
    // long after the threshold has passed (it was busy, or holding undrained
    // output, the whole time), and measuring from the threshold would then
    // close it on the very next poll — the user would see "closing in 60s"
    // and lose the window a second later.
    private static bool _warned;
    private static long _warnedAtTicks;

    /// <summary>
    /// Stamps "used just now" and refreshes this console's marker. Called for
    /// every deliberate, console-targeting request (execute_command,
    /// get_current_location, cancel) and once per user command via the polling
    /// engine's prompt hook. NOT called for get_status / consume_output — those
    /// are the proxy's discovery probes, which hit every sibling and would
    /// otherwise keep a standby console alive forever.
    /// </summary>
    public static void RecordActivity()
    {
        long ticks = NowTicks();
        string? path;
        lock (_lock)
        {
            _lastActivityTicks = ticks;
            path = _markerPath;
        }
        if (path != null) TryWriteMarker(path, ticks);
    }

    /// <summary>Seconds since the last recorded activity.</summary>
    public static double IdleSeconds
    {
        get
        {
            long ticks;
            lock (_lock) { ticks = _lastActivityTicks; }
            return (NowTicks() - ticks) / (double)TimeSpan.TicksPerSecond;
        }
    }

    /// <summary>True once the close warning has been shown (i.e. in the grace
    /// window); the engine uses this to arm keypress-cancel only then.</summary>
    public static bool IsReapPending { get { lock (_lock) { return _warned; } } }

    /// <summary>
    /// Marks this console as owned by a proxy session and starts publishing a
    /// marker. Called on the owned import path and on every claim/reclaim.
    /// </summary>
    public static void SetOwned(int proxyPid, string? agentId)
    {
        string? path = null;
        long ticks = NowTicks();
        lock (_lock)
        {
            _proxyPid = proxyPid;
            _agentId = agentId;
            _lastActivityTicks = ticks;
            _warned = false;
            try
            {
                var dir = GroupDir(proxyPid, agentId);
                Directory.CreateDirectory(dir);
                _markerPath = Path.Combine(dir, _pid.ToString());
                path = _markerPath;
            }
            catch { _markerPath = null; }
        }
        if (path != null) TryWriteMarker(path, ticks);
        // Opportunistically sweep marker directories left by prior proxy
        // sessions whose process is gone, so LocalApplicationData doesn't
        // accumulate one dead group dir per proxy restart.
        PruneDeadGroups(proxyPid);
    }

    /// <summary>
    /// Marks this console unowned (released after a disconnect, or never
    /// owned): deletes its marker and takes it out of reaping entirely.
    /// </summary>
    public static void SetUnowned()
    {
        string? path;
        lock (_lock)
        {
            path = _markerPath;
            _proxyPid = null;
            _agentId = null;
            _markerPath = null;
            _warned = false;
        }
        TryDelete(path);
    }

    /// <summary>
    /// Decides whether this idle standby console should warn or close itself.
    /// Only an OWNED, idle, genuinely-standby console with nothing typed at its
    /// prompt that is NOT its group's most-recently-active member is ever reaped.
    /// Returns <see cref="ReapAction.Close"/> only after the warning has been
    /// shown and the grace has elapsed with no intervening activity.
    ///
    /// <para>Survivors are therefore the keeper PLUS any console the user has left
    /// text in — not exactly one. That is intentional: the keeper rule alone could
    /// not protect a console the human is mid-typing in without letting it steal
    /// keeper from the console the AI is actually using.</para>
    /// </summary>
    /// <param name="warnSeconds">Idle seconds before warning (0 disables reaping).</param>
    /// <param name="graceSeconds">Seconds after the warning before closing.</param>
    /// <param name="runspaceAvailable"><c>ExecutionState.IsRunspaceAvailable</c> — false while a user command runs.</param>
    /// <param name="statusStandby">True when <c>ExecutionState.Status == "standby"</c> (not busy, no undrained output).</param>
    /// <param name="typedTextPresent">
    /// True when the user has text sitting at the prompt, unsubmitted. This is a
    /// LOCAL veto only: it keeps this console alive without touching the marker,
    /// so a half-typed line can no longer win the keeper election and reap every
    /// sibling — including the console the AI is actively working in. Contrast
    /// with <see cref="RecordActivity"/>, which both resets idle AND re-stamps
    /// the marker; the buffer check deliberately wants the former, not the latter.
    /// </param>
    public static ReapAction EvaluateReap(int warnSeconds, int graceSeconds, bool runspaceAvailable, bool statusStandby, bool typedTextPresent = false)
    {
        if (warnSeconds <= 0) { lock (_lock) { _warned = false; } return ReapAction.None; }

        bool owned;
        lock (_lock) { owned = _markerPath != null; }

        // Only reap an owned console that is idle AND truly standby: a running
        // user command (runspace unavailable), a busy AI pipeline, awaiting
        // input, undrained completed output, or text half-typed at the prompt
        // all keep it alive.
        if (!owned || !runspaceAvailable || !statusStandby || typedTextPresent)
        {
            lock (_lock) { _warned = false; }
            return ReapAction.None;
        }

        if (IdleSeconds < warnSeconds)
        {
            lock (_lock) { _warned = false; }
            return ReapAction.None;
        }

        // Idle past the threshold. The most-recently-active console (or the
        // only one, or any console when the marker dir can't be read) is the
        // keeper and never reaps.
        if (IsKeeper())
        {
            lock (_lock) { _warned = false; }
            return ReapAction.None;
        }

        long warnedAt;
        lock (_lock)
        {
            if (!_warned)
            {
                _warned = true;
                _warnedAtTicks = NowTicks();
                return ReapAction.Warn;
            }
            warnedAt = _warnedAtTicks;
        }

        // Grace runs from the warning, not from the idle threshold, so the user
        // always gets the full window they were promised.
        if ((NowTicks() - warnedAt) / (double)TimeSpan.TicksPerSecond >= graceSeconds)
            return ReapAction.Close;

        return ReapAction.None; // warned, still within grace
    }

    /// <summary>Deletes this console's own marker — called just before self-close.</summary>
    public static void DeleteOwnMarker()
    {
        string? path;
        lock (_lock) { path = _markerPath; }
        TryDelete(path);
    }

    /// <summary>
    /// True if this console should survive: it has the most recent activity of
    /// its group, it is the only member, or the group directory can't be read
    /// (fail-safe — never reap on an I/O error). False only when a peer marker
    /// is strictly newer, i.e. we are a stale standby safe to reap.
    /// </summary>
    private static bool IsKeeper()
    {
        int proxyPid; string? agentId; long myTicks; string? myPath;
        lock (_lock)
        {
            if (_markerPath == null) return true; // unowned -> never reaped
            proxyPid = _proxyPid!.Value;
            agentId = _agentId;
            myTicks = _lastActivityTicks;
            myPath = _markerPath;
        }

        try
        {
            var dir = GroupDir(proxyPid, agentId);
            long bestTicks = myTicks;
            int bestPid = _pid;

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (string.Equals(file, myPath, StringComparison.OrdinalIgnoreCase)) continue;

                var name = Path.GetFileName(file);
                if (!int.TryParse(name, out var otherPid)) continue;

                // Sweep markers left by processes that are gone (hard-killed, or
                // closed via close_console) so a dead peer never blocks reaping
                // or clutters the group directory.
                if (!IsProcessAlive(otherPid)) { TryDelete(file); continue; }

                long otherTicks;
                try
                {
                    if (!long.TryParse(File.ReadAllText(file).Trim(), out otherTicks)) continue;
                }
                catch { continue; }

                // Newest wins; deterministic tie-break on PID so every process
                // in the group elects the same keeper.
                if (otherTicks > bestTicks || (otherTicks == bestTicks && otherPid > bestPid))
                {
                    bestTicks = otherTicks;
                    bestPid = otherPid;
                }
            }

            return bestPid == _pid;
        }
        catch
        {
            return true; // fail-safe: unreadable group dir -> keep this console
        }
    }

    /// <summary>
    /// Per-user root holding all sessions' marker directories. Uses
    /// LocalApplicationData (%LOCALAPPDATA% on Windows, $XDG_DATA_HOME /
    /// ~/.local/share on Unix) — writable without admin on every platform,
    /// deliberately NOT the system temp dir or the current directory. Falls
    /// back to temp only if the per-user path is unavailable.
    /// </summary>
    // Test seam: an isolated marker root. Tests point this at a private temp dir
    // so concurrent test processes never share — and so PruneDeadGroups (which
    // deletes any group whose proxy PID is dead) can't reap a peer test's markers,
    // whose synthetic proxy PIDs always look dead. Null in production.
    internal static string? LiveRootForTests;

    private static string LiveRoot()
    {
        if (LiveRootForTests != null) return LiveRootForTests;

        string root;
        try { root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        catch { root = ""; }
        if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
        return Path.Combine(root, "PowerShell.MCP", "live");
    }

    private static string GroupDir(int proxyPid, string? agentId)
    {
        var agent = string.IsNullOrEmpty(agentId) ? "default" : Sanitize(agentId);
        return Path.Combine(LiveRoot(), $"{proxyPid}.{agent}");
    }

    /// <summary>
    /// Deletes marker directories whose owning proxy process is gone. Skips the
    /// current proxy and any still-alive one. Best-effort; never throws.
    /// </summary>
    private static void PruneDeadGroups(int currentProxyPid)
    {
        try
        {
            var root = LiveRoot();
            if (!Directory.Exists(root)) return;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                var dot = name.IndexOf('.');
                var pidPart = dot >= 0 ? name.Substring(0, dot) : name;
                if (!int.TryParse(pidPart, out var groupProxyPid)) continue;
                if (groupProxyPid == currentProxyPid) continue;
                if (IsProcessAlive(groupProxyPid)) continue;
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
        catch { }
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

    private static void TryWriteMarker(string path, long ticks)
    {
        try { File.WriteAllText(path, ticks.ToString()); } catch { }
    }

    private static void TryDelete(string? path)
    {
        if (path == null) return;
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool IsProcessAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }

    /// <summary>Test-only: the on-disk marker directory for a session, so a test
    /// can plant sibling markers or clean up after itself.</summary>
    internal static string GroupDirForTests(int proxyPid, string? agentId) => GroupDir(proxyPid, agentId);

    /// <summary>Test-only: restore pristine static state and the real clock so
    /// one test's ownership / warn flag / fake time can't leak into the next.</summary>
    internal static void ResetForTests()
    {
        lock (_lock)
        {
            _proxyPid = null;
            _agentId = null;
            _markerPath = null;
            _warned = false;
            _warnedAtTicks = 0;
        }
        UtcNow = () => DateTime.UtcNow;
        lock (_lock) { _lastActivityTicks = NowTicks(); }
    }
}
