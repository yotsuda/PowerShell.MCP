using PowerShell.MCP.Proxy.Models;

namespace PowerShell.MCP.Proxy.Services;

public interface IPowerShellService
{
    Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken = default);
    Task<string> GetCurrentLocationFromPipeAsync(string pipeName, CancellationToken cancellationToken = default);
    Task<string> InvokeExpressionAsync(string command, bool execute_immediately, CancellationToken cancellationToken = default);
    Task<string> StartNewConsoleAsync(CancellationToken cancellationToken = default);
}
