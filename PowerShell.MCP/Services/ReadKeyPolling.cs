using System.Reflection;

namespace PowerShell.MCP.Services;

/// <summary>
/// On Linux and macOS, makes PSReadLine wait for a key without holding the
/// console's input lock, so that its idle loop -- which carries the engine's
/// heartbeat and every AI command -- does not stop until someone presses a key.
/// </summary>
/// <remarks>
/// <para>
/// PSReadLine reads keys on a thread of its own, blocked in Console.ReadKey.
/// On Unix, in a console under remote control, once Esc has been pressed (on
/// its own, or to close MenuComplete's menu) that thread waits holding .NET's
/// stdin lock (the SyncTextReader), and goes on doing so for the life of the
/// process; before the first Esc it waits with the lock free. In a console the
/// local proxy drives (a user-opened one, over SSH, same terminal) the same
/// keys did not lead to it in three tries; what remote control adds that makes
/// the difference was not found. Meanwhile PSReadLine's idle loop (ReadKey's 300 ms timeout,
/// which runs the engine's timer event) reads Console.CursorTop before and
/// after processing events. When .NET's cached cursor position is invalid --
/// and any escape sequence, non-ASCII byte, tab, a write over 255 bytes or a
/// write into the last column invalidates it, so practically every AI command
/// does -- CursorTop asks the terminal and reads the reply from stdin, under
/// the same lock. The idle loop then waits for the key thread, which waits for
/// a key: the heartbeat stops, the console reads "busy (user command)", and the
/// AI command that comes next is refused until the user presses a key.
/// Measured 2026-11-21 on RHEL 8.5, pwsh 7.7.0, over SSH: the cache stayed
/// invalid (s_cursorLeft == -1) and the heartbeat stale for 100 s after an AI
/// command; the query completed (CursorLeft 38, the prompt's length) the
/// instant Enter released the lock. A probe of the lock (Monitor.TryEnter)
/// showed it free while waiting after Enter, a typed command and Tab
/// completion, and held from the first Esc on (remote control, three of three);
/// with this class installed it stayed free after Esc too, and the console
/// never stalled. The fix does not depend on what Esc changes: whatever state
/// it leaves, the key thread no longer waits inside ReadKey.
/// </para>
/// <para>
/// The key method PSReadLine calls is replaced by one that polls
/// Console.KeyAvailable (which takes the lock only for the check) and calls
/// Console.ReadKey only once a key is there, so the lock is never held while
/// waiting. PowerShell Editor Services did the same on Unix in the PowerShell 6
/// era for the same deadlock. PSReadLine's own idle logic is untouched -- it
/// still reads CursorTop and moves its input anchor below output the engine
/// wrote -- it just no longer waits for a key to do so.
/// </para>
/// <para>
/// The hook is PSReadLine's VirtualTerminal._readKeyMethod, a Lazy that reads
/// _readKeyOverride (the field PSReadLine keeps for PSES) on first use. By the
/// time this module loads it has long been evaluated, so the Lazy itself is
/// replaced. Nothing is changed when PSES has set _readKeyOverride (the VS Code
/// integrated console), on Windows (the console input APIs do not have this
/// lock), or when the fields are missing in the loaded PSReadLine: the console
/// then behaves as it did before.
/// </para>
/// </remarks>
public static class ReadKeyPolling
{
    // Key latency is at most this; a pasted line is drained by PSReadLine's
    // own KeyAvailable loop once the first key is in.
    internal const int PollIntervalMs = 15;

    private static FieldInfo? _methodField;
    private static object? _originalMethod;
    private static object? _installedMethod;

    /// <summary>What Install did, for diagnostics.</summary>
    public static string Status { get; private set; } = "not installed";

    /// <summary>
    /// Installs the polling key method. Call on the home thread while no key
    /// is being read (from a command, or before the first prompt): a ReadKey
    /// already waiting keeps the old method until that key arrives.
    /// </summary>
    public static string Install()
    {
        if (OperatingSystem.IsWindows())
            return Status = "not needed on Windows";

        try
        {
            if (_installedMethod != null && _methodField?.GetValue(null) == _installedMethod)
                return Status = "installed";

            var vt = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Microsoft.PowerShell.Internal.VirtualTerminal", throwOnError: false))
                .FirstOrDefault(t => t != null);
            if (vt == null)
                return Status = "skipped: PSReadLine is not loaded";

            var overrideField = vt.GetField("_readKeyOverride", BindingFlags.Static | BindingFlags.NonPublic);
            var methodField = vt.GetField("_readKeyMethod", BindingFlags.Static | BindingFlags.NonPublic);
            if (overrideField?.FieldType != typeof(Func<bool, ConsoleKeyInfo>)
                || methodField?.FieldType != typeof(Lazy<Func<bool, ConsoleKeyInfo>>)
                || methodField.IsInitOnly)
                return Status = "skipped: this PSReadLine has no replaceable key method";

            if (overrideField.GetValue(null) != null)
                return Status = "skipped: another host (PowerShell Editor Services) reads the keys";

            var polling = new Lazy<Func<bool, ConsoleKeyInfo>>(() => ReadKey);
            _originalMethod = methodField.GetValue(null);
            methodField.SetValue(null, polling);
            _methodField = methodField;
            _installedMethod = polling;
            return Status = "installed";
        }
        catch (Exception ex)
        {
            return Status = $"failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Puts PSReadLine's original key method back, if ours is still the one in place.</summary>
    public static void Uninstall()
    {
        try
        {
            if (_methodField != null && _installedMethod != null && _methodField.GetValue(null) == _installedMethod)
                _methodField.SetValue(null, _originalMethod);
        }
        catch { }
        _methodField = null;
        _originalMethod = null;
        _installedMethod = null;
        Status = "not installed";
    }

    internal static ConsoleKeyInfo ReadKey(bool intercept) => ReadKey(intercept, () => Console.KeyAvailable, Console.ReadKey, Thread.Sleep);

    // Seams for the tests: the wait loop with the console taken out.
    internal static ConsoleKeyInfo ReadKey(bool intercept, Func<bool> keyAvailable, Func<bool, ConsoleKeyInfo> readKey, Action<int> sleep)
    {
        while (!keyAvailable())
            sleep(PollIntervalMs);
        return readKey(intercept);
    }
}
