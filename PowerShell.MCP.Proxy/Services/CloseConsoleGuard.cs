using System.Collections.Concurrent;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>Outcome of the human-presence check that gates close_console.</summary>
public enum CloseDecision
{
    /// <summary>Close it: nobody appears to be typing, or the AI already confirmed.</summary>
    Proceed,

    /// <summary>A human is mid-typing and this is the first attempt — refuse and arm.</summary>
    Refuse,
}

/// <summary>
/// The human-presence gate for <c>close_console</c>.
///
/// <para>A console with unsubmitted text at its prompt has a human composing a
/// command in it right now. Killing it silently is the worst outcome in the whole
/// console-lifecycle feature, so the first attempt refuses and "arms" that console;
/// calling <c>close_console</c> again for it closes without the check. There is
/// deliberately NO <c>force</c> parameter — the retry itself is the confirmation.</para>
///
/// <para><b>Fail open.</b> Unknown presence (no sample, stale sample, probe failed)
/// always yields <see cref="CloseDecision.Proceed"/>. close_console is the escape
/// hatch for a console that is stuck or unreachable, and those are exactly the
/// states where the probe is least likely to answer. This is the opposite default
/// from the auto-reap path, which fails toward keeping consoles alive — same
/// signal, inverted bias, on purpose.</para>
///
/// <para><b>Never signal a refusal as an error.</b> Because the retry IS the
/// confirmation, an automatic error-retry by the caller would silently become a
/// kill. Refusals must travel as ordinary successful tool results so nothing
/// retries them on the AI's behalf.</para>
/// </summary>
public static class CloseConsoleGuard
{
    /// <summary>
    /// How long a refusal stays armed. Long enough for the AI's immediate retry,
    /// short enough that a refusal from earlier in the session cannot turn a
    /// later first attempt into an instant kill.
    /// </summary>
    public const int ArmTtlSeconds = 60;

    /// <summary>
    /// A typed-text sample older than this is treated as unknown. The engine
    /// samples about every 2s; this leaves room for a slow tick without letting
    /// a long-dead engine's last value keep vetoing closes.
    /// </summary>
    public const int MaxSampleAgeSeconds = 5;

    // Keyed by pipe name, NOT raw pid: the pipe name carries proxy pid + agent id,
    // so one sub-agent's refusal can never arm another's close, and a recycled pid
    // cannot inherit an arm.
    private static readonly ConcurrentDictionary<string, DateTime> _armed = new();

    /// <summary>Clock seam; overridden by tests so the TTL can be crossed without waiting.</summary>
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

    /// <summary>
    /// Decides whether this close may proceed, and arms the console when it refuses.
    /// </summary>
    /// <param name="pipeName">Identifies the console AND its owning agent.</param>
    /// <param name="typedTextPresent">Last sample from the console, if any.</param>
    /// <param name="sampleAgeSeconds">Age of that sample; negative means never sampled.</param>
    /// <param name="probeSucceeded">False when the status probe threw, timed out, or returned nothing.</param>
    public static CloseDecision Evaluate(string pipeName, bool typedTextPresent, double sampleAgeSeconds, bool probeSucceeded)
    {
        // Already confirmed by a previous refused attempt — close without re-checking.
        if (IsArmed(pipeName))
        {
            Disarm(pipeName);
            return CloseDecision.Proceed;
        }

        // Unknown presence -> fail open.
        if (!probeSucceeded) return CloseDecision.Proceed;
        if (sampleAgeSeconds < 0 || sampleAgeSeconds > MaxSampleAgeSeconds) return CloseDecision.Proceed;

        if (!typedTextPresent) return CloseDecision.Proceed;

        _armed[pipeName] = UtcNow();
        return CloseDecision.Refuse;
    }

    /// <summary>
    /// Drops any pending confirmation for this console. Called when the console is
    /// actually closed, and when the AI runs a command in it — using a console
    /// contradicts the intent to abandon it, so the next close starts over.
    /// </summary>
    public static void Disarm(string pipeName) => _armed.TryRemove(pipeName, out _);

    private static bool IsArmed(string pipeName)
    {
        if (!_armed.TryGetValue(pipeName, out var armedAt)) return false;
        if ((UtcNow() - armedAt).TotalSeconds > ArmTtlSeconds)
        {
            _armed.TryRemove(pipeName, out _);
            return false;
        }
        return true;
    }

    /// <summary>Test-only: clear all arms and restore the real clock.</summary>
    internal static void ResetForTests()
    {
        _armed.Clear();
        UtcNow = () => DateTime.UtcNow;
    }
}
