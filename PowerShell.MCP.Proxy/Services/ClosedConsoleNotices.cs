namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// The notes a console leaves behind when it closes itself, and the wording
/// of every "console #N was closed" line the proxy produces.
///
/// <para>An idle standby console reaps itself (the module's
/// <c>ConsoleLiveness</c>): it prints a warning and a farewell in its own
/// window, then exits. The proxy sees none of that — only, on a later tool
/// call, that a pipe is gone, and only if it happens to look (the fast path of
/// discovery never did). The AI then kept waiting on a job that console had
/// taken down with it. So before exiting, the console writes
/// <c>&lt;pid&gt;.closed</c> (content: the reason) next to where its liveness
/// marker was, in the per-(proxy, agent) group directory the module already
/// maintains; and the proxy takes these notes on every tool call, reports them
/// with the reason, and deletes them.</para>
///
/// <para>The directory layout and the suffix mirror the module's
/// <c>ConsoleLiveness</c> (<c>%LOCALAPPDATA%\PowerShell.MCP\live\{proxyPid}.{agent}\</c>);
/// the two must stay in sync. Everything here is best-effort: an unreadable
/// directory means "no notes", never a failure of the tool call.</para>
/// </summary>
public static class ClosedConsoleNotices
{
    /// <summary>Suffix of a self-close note; the rest of the file name is the console's PID.</summary>
    public const string Suffix = ".closed";

    // Test seam: an isolated root so tests never touch (or get swept by) the
    // real consoles on the machine. Null in production.
    internal static string? LiveRootForTests;

    /// <summary>
    /// Takes (returns and deletes) the self-close note of one console, or
    /// null when it left none — it was killed, or its window was closed.
    /// </summary>
    public static string? TakeReason(int proxyPid, string agentId, int pwshPid)
    {
        try
        {
            var path = Path.Combine(GroupDir(proxyPid, agentId), pwshPid + Suffix);
            if (!File.Exists(path)) return null;
            var reason = File.ReadAllText(path).Trim();
            try { File.Delete(path); } catch { }
            return reason.Length > 0 ? reason : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Takes every self-close note in the agent's group: console PID → reason.
    /// </summary>
    public static Dictionary<int, string> TakeAll(int proxyPid, string agentId)
    {
        var notes = new Dictionary<int, string>();
        try
        {
            var dir = GroupDir(proxyPid, agentId);
            if (!Directory.Exists(dir)) return notes;
            foreach (var file in Directory.EnumerateFiles(dir, "*" + Suffix))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(stem, out var pid)) continue;
                string reason;
                try { reason = File.ReadAllText(file).Trim(); } catch { continue; }
                try { File.Delete(file); } catch { }
                if (reason.Length > 0) notes[pid] = reason;
            }
        }
        catch { }
        return notes;
    }

    /// <summary>
    /// One line about a console this session owned that is no longer there.
    /// With a reason (the console's own note) it says why; without one, that
    /// the end came from outside this server. Either way it says what the AI
    /// most needs to know and could not see: whatever that console was running
    /// in the background is gone with it.
    /// </summary>
    public static string Describe(string displayName, string? reason)
    {
        var cause = reason != null
            ? $"it {reason}"
            : "its window was closed or its process ended outside this server";
        return $"⚠ Console {displayName} was closed: {cause}. Any Start-Job jobs it was running ended with it; a process it started with Start-Process may still be running.";
    }

    private static string LiveRoot()
    {
        if (LiveRootForTests != null) return LiveRootForTests;

        string root;
        try { root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
        catch { root = ""; }
        if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
        return Path.Combine(root, "PowerShell.MCP", "live");
    }

    /// <summary>The group directory for one (proxy, agent) pair; exposed for tests.</summary>
    internal static string GroupDir(int proxyPid, string? agentId)
    {
        var agent = string.IsNullOrEmpty(agentId) ? "default" : Sanitize(agentId);
        return Path.Combine(LiveRoot(), $"{proxyPid}.{agent}");
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}
