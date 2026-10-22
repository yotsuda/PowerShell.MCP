namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Result of pipe discovery operation
/// </summary>
/// <param name="LiveCwd">
/// The cwd reported by get_status on the chosen ReadyPipeName, or null if
/// the pipe didn't reply with a cwd (older DLL, network drive disconnected,
/// or the discovery path that landed here didn't probe). Used by
/// execute_command's drift check to compare against
/// <c>ConsoleSessionManager.GetLastAiCwd</c> and inject a Set-Location
/// preamble when the user has typed <c>cd</c> in the visible console
/// between AI calls.
/// </param>
public record PipeDiscoveryResult(
    string? ReadyPipeName,
    bool ConsoleSwitched,
    IReadOnlyList<string> ClosedConsoleMessages,
    string? AllPipesStatusInfo,
    string? LiveCwd = null
);

/// <summary>
/// Result of collecting cached outputs from all pipes
/// </summary>
public record CachedOutputResult(
    string CompletedOutput,
    string BusyStatusInfo
);

/// <summary>
/// Service for discovering and managing PowerShell console pipes
/// </summary>
public interface IPipeDiscoveryService
{
    /// <summary>
    /// Find a ready pipe for command execution.
    /// </summary>
    /// <param name="includeUnowned">
    /// When true (default), Step 3 of discovery scans unowned pipes — pwsh
    /// processes the user spawned manually and ran <c>Import-Module
    /// PowerShell.MCP</c> on. The first ready unowned pipe is claimed.
    /// When false, unowned pipes are skipped and the method returns null
    /// instead of claiming. Used by <c>start_console</c> without an
    /// explicit <c>start_location</c>: the AI hasn't pinned its intended
    /// cwd, so claiming an arbitrary user-set cwd would mislead it. A
    /// fresh console at the proxy's default home is the predictable
    /// baseline. <c>execute_command</c> and <c>get_current_location</c>
    /// keep the default <c>true</c> because the AI is actively working
    /// and benefits from the user's existing module-loaded environment.
    /// </param>
    Task<PipeDiscoveryResult> FindReadyPipeAsync(string agentId, CancellationToken cancellationToken, bool includeUnowned = true);

    /// <summary>
    /// Collect cached outputs from all pipes except the specified one
    /// </summary>
    Task<CachedOutputResult> CollectAllCachedOutputsAsync(string agentId, string? excludePipeName, CancellationToken cancellationToken);

    /// <summary>
    /// Detect consoles this agent used that are gone: every previously busy
    /// or owned PID with no pipe left, each reported with the reason the
    /// console noted when it closed itself (see <see cref="ClosedConsoleNotices"/>),
    /// and cleared from tracking. Enumerates the pipe namespace — the full
    /// discovery (slow) path.
    /// </summary>
    IReadOnlyList<string> DetectClosedConsoles(string agentId, int? excludePid = null);

    /// <summary>
    /// The cheap counterpart for the fast path, where the active console
    /// answered and nothing else is probed: takes the self-close notes of
    /// the agent's group and checks each other owned console's process for
    /// liveness. A handful of process lookups and one directory read, no
    /// pipe enumeration. Same reporting and clearing as
    /// <see cref="DetectClosedConsoles"/>.
    /// </summary>
    IReadOnlyList<string> DetectVanishedConsoles(string agentId, int? excludePid = null);
}
