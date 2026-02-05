namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Result of pipe discovery operation
/// </summary>
public record PipeDiscoveryResult(
    string? ReadyPipeName,
    bool ConsoleSwitched,
    IReadOnlyList<string> ClosedConsoleMessages,
    string? AllPipesStatusInfo
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
    /// Find a ready pipe for command execution
    /// </summary>
    Task<PipeDiscoveryResult> FindReadyPipeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Collect cached outputs from all pipes except the specified one
    /// </summary>
    Task<CachedOutputResult> CollectAllCachedOutputsAsync(string? excludePipeName, CancellationToken cancellationToken);

    /// <summary>
    /// Detect externally closed consoles
    /// </summary>
    IReadOnlyList<string> DetectClosedConsoles();
}