using PowerShell.MCP.Proxy.Models;

namespace PowerShell.MCP.Proxy.Services;

public interface IPowerShellService
{
    Task<string> GetCurrentLocationFromPipeAsync(string pipeName, CancellationToken cancellationToken = default);
    Task<GetStatusResponse?> GetStatusFromPipeAsync(string pipeName, CancellationToken cancellationToken = default);
    Task<string> ConsumeOutputFromPipeAsync(string pipeName, CancellationToken cancellationToken = default);
    Task<string> InvokeExpressionToPipeAsync(string pipeName, string command, int timeoutSeconds = 170, CancellationToken cancellationToken = default);
    Task<ClaimConsoleResponse?> ClaimConsoleAsync(string pipeName, int proxyPid, string agentId, CancellationToken cancellationToken = default);
    Task SetWindowTitleAsync(string pipeName, string title, CancellationToken cancellationToken = default);
}
