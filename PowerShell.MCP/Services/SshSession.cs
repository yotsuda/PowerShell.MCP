namespace PowerShell.MCP.Services;

/// <summary>
/// Whether this process runs inside an SSH session on Windows.
/// </summary>
/// <remarks>
/// <para>
/// On Windows, everything sshd starts runs in session 0 under a service, so
/// <see cref="Environment.UserInteractive"/> is false there — the same value a
/// scheduled task or a service gets. On Linux and macOS .NET reports true for
/// every process, so a person who logs in with ssh and runs
/// Enable-MCPRemoteControl at the prompt is let through; this lets the same
/// person through on Windows.
/// </para>
/// <para>
/// Measured in a Windows Sandbox (Win32-OpenSSH 10.4, pwsh 7.7): with a
/// terminal (ssh, ssh -t) pwsh's parent is conhost under sshd-session and
/// input is not redirected; without one (ssh host 'command') its parent is
/// sshd-session and input is redirected; a scheduled task, as the user (S4U)
/// or as SYSTEM, has svchost as parent and input NOT redirected — so the
/// ancestry, not the redirection, is what tells a task from an SSH terminal.
/// The caller still requires non-redirected input.
/// </para>
/// <para>
/// A guard, not a boundary: code already running as the user can start a
/// process under an SSH session of its own. It stops tasks, services and
/// scripts from switching remote control on quietly, as the rest of the check
/// does. For the same reason the walk does not guard against a reused parent
/// id (a parent that exited and an unrelated sshd that took its id): that
/// could only let through a process whose input is still a terminal.
/// </para>
/// </remarks>
public static class SshSession
{
    // sshd-session.exe since OpenSSH 9.8 split it out; sshd.exe before that.
    private static readonly string[] SshdNames = ["sshd-session.exe", "sshd.exe"];

    // Deep enough for pwsh <- cmd <- pwsh <- conhost <- sshd-session; a loop
    // in a stale snapshot cannot run forever.
    private const int MaxDepth = 16;

    /// <summary>True when an ancestor of this process is sshd. Windows only; never throws.</summary>
    public static bool IsAncestor()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var parents = ProcessTree.Snapshot();
            var pid = (uint)Environment.ProcessId;
            for (var depth = 0; depth < MaxDepth && parents.TryGetValue(pid, out var entry); depth++)
            {
                if (!parents.TryGetValue(entry.Parent, out var parent)) return false;
                if (SshdNames.Contains(parent.Name, StringComparer.OrdinalIgnoreCase)) return true;
                pid = entry.Parent;
            }
        }
        catch { }
        return false;
    }
}
