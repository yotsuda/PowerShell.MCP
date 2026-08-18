using PowerShell.MCP.Proxy.Models;

namespace PowerShell.MCP.Proxy.Services;

public interface IPowerShellService
{
    Task<string> GetCurrentLocationFromPipeAsync(string pipeName, CancellationToken cancellationToken = default);
    Task<GetStatusResponse?> GetStatusFromPipeAsync(string pipeName, CancellationToken cancellationToken = default);
    Task<string> ConsumeOutputFromPipeAsync(string pipeName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Puts a response the MCP client never received back into the console's
    /// cache so the next tool call delivers it. Takes no CancellationToken by
    /// design: the only caller runs precisely because its token is already
    /// cancelled. Returns false when the console did not acknowledge — the
    /// output is then gone for good, which the caller must report rather than
    /// claim a save.
    /// </summary>
    Task<bool> CacheOutputToPipeAsync(string pipeName, string output);
    Task<string> ExecuteCommandToPipeAsync(string pipeName, string command, Dictionary<string, string>? variables = null, int timeoutSeconds = 170, CancellationToken cancellationToken = default);
    Task<ClaimConsoleResponse?> ClaimConsoleAsync(string pipeName, int proxyPid, string agentId, CancellationToken cancellationToken = default);
    Task SetWindowTitleAsync(string pipeName, string title, CancellationToken cancellationToken = default);
    Task ExecuteSilentAsync(string pipeName, string pipeline, CancellationToken cancellationToken = default);
    Task<CommandAckResponse?> CancelAsync(string pipeName, CancellationToken cancellationToken = default);
}
