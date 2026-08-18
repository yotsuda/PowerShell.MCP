namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// The longest a blocking tool call may run before the DLL returns a timeout
/// response instead of the result.
///
/// This has to stay BELOW the connected client's own tool timeout. If the
/// client gives up first, a pipeline that then finishes normally has its
/// output drained into a response nobody is listening for
/// (NamedPipeServer's "success" branch consumes the console's cache), and
/// only clients that send notifications/cancelled let us put it back.
///
/// The client's timeout cannot be discovered — it lives in client config we
/// cannot read, and no protocol message carries it. So the ceiling is chosen
/// from what the client identifies itself as in the MCP handshake:
///
/// - <b>50s</b> by default. The MCP TypeScript SDK ships
///   DEFAULT_REQUEST_TIMEOUT_MSEC = 60000, so a client that has not
///   overridden it aborts at 60s. 50 leaves room for the pre-command
///   overhead the ceiling does not cover (console claim or spawn, measured
///   at ~3s) plus response assembly afterwards.
/// - <b>170s</b> for Claude Code, which was measured not to abort at all in
///   its default configuration (it moves a long call to a background task at
///   120s and still delivers the result), and which does send
///   notifications/cancelled when it does abort — so the re-cache path
///   catches what the ceiling would otherwise have to prevent.
///
/// An unrecognised or absent client name falls to 50: the failure direction
/// is one extra round trip, never a lost result.
/// </summary>
public static class TimeoutCeiling
{
    /// <summary>Ceiling for clients whose tolerance we have not measured.</summary>
    public const int ConservativeSeconds = 50;

    /// <summary>Ceiling for clients measured to tolerate a long blocking call.</summary>
    public const int RelaxedSeconds = 170;

    /// <summary>The DLL's own upper bound; nothing may exceed it.</summary>
    public const int HardMaxSeconds = 170;

    /// <summary>Escape hatch for an operator who knows their client's timeout.</summary>
    public const string EnvVarName = "POWERSHELL_MCP_TIMEOUT_CEILING";

    /// <summary>
    /// clientInfo.name as sent in the MCP initialize request. Verified
    /// against Claude Code 2.1.234, which reports
    /// name="claude-code" / title="Claude Code".
    /// </summary>
    private const string ClaudeCodeClientName = "claude-code";

    private static volatile int _current = ConservativeSeconds;

    /// <summary>The ceiling in force for the connected client.</summary>
    public static int Seconds => _current;

    /// <summary>
    /// Records the client identity from the MCP handshake and fixes the
    /// ceiling. Called on every blocking tool call; the answer cannot change
    /// within a session, so repeat calls are just cheap re-resolution.
    /// </summary>
    public static void ObserveClient(string? clientName)
    {
        _current = Resolve(clientName, Environment.GetEnvironmentVariable(EnvVarName));
    }

    /// <summary>
    /// The ceiling decision, as a pure function of the two inputs, so it can
    /// be tested without a live server or a mutated environment.
    /// </summary>
    public static int Resolve(string? clientName, string? envValue)
    {
        // An explicit operator override wins over anything we infer, but is
        // still held to the DLL's hard maximum. A malformed value is ignored
        // rather than treated as zero.
        if (int.TryParse(envValue, out var configured) && configured > 0)
            return Math.Min(configured, HardMaxSeconds);

        if (string.Equals(clientName, ClaudeCodeClientName, StringComparison.OrdinalIgnoreCase))
            return RelaxedSeconds;

        return ConservativeSeconds;
    }

    /// <summary>
    /// Clamps a caller-requested timeout to the ceiling in force. The ceiling
    /// wins over the caller's floor: Math.Clamp throws when min exceeds max,
    /// and a future ceiling lower than some caller's minimum must not turn a
    /// tool call into a crash.
    /// </summary>
    public static int Clamp(int requestedSeconds, int minimumSeconds)
        => Math.Clamp(requestedSeconds, Math.Min(minimumSeconds, _current), _current);

    /// <summary>
    /// Never dispatch with less than this, even when the ceiling is already
    /// spent: a pipeline given zero budget would time out before it could
    /// possibly finish, and the caller asked for a command, not a refusal.
    /// </summary>
    public const int MinimumDispatchSeconds = 5;

    /// <summary>
    /// How long the pipeline itself may run, given how much of the ceiling
    /// this call has already spent getting to the point of dispatch.
    ///
    /// The ceiling is a deadline measured from when the client started
    /// waiting, not a duration for the pipeline alone. Console discovery,
    /// claiming, and above all a cold console spawn happen first and are
    /// invisible to the DLL's own timer — a spawn was measured at ~15s, which
    /// is enough to push a 50s-capped command's response past a 60s client
    /// timeout. Subtracting the elapsed time keeps the whole call inside the
    /// ceiling instead of just its last leg.
    /// </summary>
    public static int RemainingFor(int requestedSeconds, TimeSpan alreadyElapsed)
        => Remaining(requestedSeconds, alreadyElapsed, _current);

    /// <summary>
    /// <see cref="RemainingFor"/> against an explicit ceiling, so the budget
    /// arithmetic can be tested without the process-wide ceiling.
    /// </summary>
    public static int Remaining(int requestedSeconds, TimeSpan alreadyElapsed, int ceilingSeconds)
    {
        var budget = ceilingSeconds - (int)alreadyElapsed.TotalSeconds;
        if (budget < MinimumDispatchSeconds)
            budget = MinimumDispatchSeconds;

        return Math.Clamp(requestedSeconds, 0, budget);
    }
}
