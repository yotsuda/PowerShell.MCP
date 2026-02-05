using System.Text;
using PowerShell.MCP.Proxy.Models;
using PowerShell.MCP.Proxy.Helpers;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Service for discovering and managing PowerShell console pipes
/// </summary>
public class PipeDiscoveryService : IPipeDiscoveryService
{
    private readonly IPowerShellService _powerShellService;
    private readonly ConsoleSessionManager _sessionManager;

    public PipeDiscoveryService(IPowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
        _sessionManager = ConsoleSessionManager.Instance;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DetectClosedConsoles(string agentId)
    {
        var closedMessages = new List<string>();
        var previouslyBusyPids = _sessionManager.ConsumeKnownBusyPids(agentId);
        var currentPipes = _sessionManager.EnumeratePipes(_sessionManager.ProxyPid, agentId).ToList();
        var currentPids = currentPipes
            .Select(ConsoleSessionManager.GetPidFromPipeName)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToHashSet();

        foreach (var pid in previouslyBusyPids)
        {
            if (!currentPids.Contains(pid))
            {
                closedMessages.Add($"  - ⚠ Console PID {pid} was closed");
            }
        }

        return closedMessages;
    }

    /// <inheritdoc />
    public async Task<PipeDiscoveryResult> FindReadyPipeAsync(string agentId, CancellationToken cancellationToken)
    {
        var closedMessages = new List<string>();
        var allPipesStatus = new List<string>();

        // Detect externally closed consoles
        closedMessages.AddRange(DetectClosedConsoles(agentId));

        // Step 1: Check ActivePipeName first
        var activePipe = _sessionManager.GetActivePipeName(agentId);
        if (activePipe != null)
        {
            var status = await _powerShellService.GetStatusFromPipeAsync(activePipe, cancellationToken);

            if (status == null)
            {
                _sessionManager.ClearDeadPipe(agentId, activePipe);
                var pid = ConsoleSessionManager.GetPidFromPipeName(activePipe);
                closedMessages.Add($"  - ⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");
            }
            else if (status.IsReady())
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
                return new PipeDiscoveryResult(activePipe, false, closedMessages, BuildClosedConsoleInfo(closedMessages));
            }
            else // busy
            {
                if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
                allPipesStatus.Add($"  - {activePipe}: {status.Status} (pipeline: {status.Pipeline ?? "unknown"}, duration: {status.Duration:F1}s)");
            }
        }

        // Step 2: Check all other pipes via EnumeratePipes
        var currentPipes = _sessionManager.EnumeratePipes(_sessionManager.ProxyPid, agentId).ToList();
        foreach (var pipeName in currentPipes)
        {
            if (pipeName == activePipe) continue; // Already checked

            var status = await _powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                _sessionManager.ClearDeadPipe(agentId, pipeName);
                var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                closedMessages.Add($"  - ⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");
                continue;
            }

            if (status.IsReady())
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
                _sessionManager.SetActivePipeName(agentId, pipeName);
                return new PipeDiscoveryResult(pipeName, true, closedMessages, BuildClosedConsoleInfo(closedMessages));
            }

            if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
            allPipesStatus.Add($"  - {pipeName}: {status.Status} (pipeline: {status.Pipeline ?? "unknown"}, duration: {status.Duration:F1}s)");
        }

        // Step 3: Check unowned pipes (user-started consoles not yet claimed by any proxy)
        foreach (var pipeName in _sessionManager.EnumerateUnownedPipes())
        {
            var status = await _powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                _sessionManager.ClearDeadPipe(agentId, pipeName);
                continue;
            }

            if (status.IsReady())
            {
                // Claim this console - response may not be received because pipe closes during rename
                var pwshPid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                if (!pwshPid.HasValue) continue;

                // Fire and forget - the pipe will close before response
                _ = _powerShellService.ClaimConsoleAsync(pipeName, _sessionManager.ProxyPid, agentId, cancellationToken);

                // Calculate the expected new pipe name
                var newPipeName = ConsoleSessionManager.GetPipeNameForPids(_sessionManager.ProxyPid, agentId, pwshPid.Value);

                // Wait for the new pipe to become available (retry with short timeout)
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(100, cancellationToken);

                    var newStatus = await _powerShellService.GetStatusFromPipeAsync(newPipeName, cancellationToken);
                    if (newStatus != null)
                    {
                        _sessionManager.SetActivePipeName(agentId, newPipeName);
                        return new PipeDiscoveryResult(newPipeName, true, closedMessages, BuildClosedConsoleInfo(closedMessages));
                    }
                }
            }

            if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
            allPipesStatus.Add($"  - {pipeName} (unowned): {status.Status}");
        }

        // No ready pipe found
        var statusInfo = allPipesStatus.Count > 0
            ? "All pipes status:\n" + string.Join("\n", allPipesStatus)
            : "No pipes found.";
        return new PipeDiscoveryResult(null, false, closedMessages, statusInfo);
    }

    /// <inheritdoc />
    public async Task<CachedOutputResult> CollectAllCachedOutputsAsync(string agentId, string? excludePipeName, CancellationToken cancellationToken)
    {
        var completedOutput = new StringBuilder();
        var busyStatusInfo = new StringBuilder();

        foreach (var pipeName in _sessionManager.EnumeratePipes(_sessionManager.ProxyPid, agentId))
        {
            if (pipeName == excludePipeName) continue;

            var status = await _powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                _sessionManager.ClearDeadPipe(agentId, pipeName);
            }
            else if (status.Status == "completed")
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
                var output = await _powerShellService.ConsumeOutputFromPipeAsync(pipeName, cancellationToken);
                if (!string.IsNullOrEmpty(output))
                {
                    // Replace "Status: Ready" with "Status: Standby" for non-active pipes
                    const string oldValue = "| Status: Ready |";
                    var index = output.IndexOf(oldValue);
                    if (index >= 0)
                    {
                        output = output.Remove(index, oldValue.Length).Insert(index, "| Status: Standby |");
                    }
                    completedOutput.AppendLine(output);
                    completedOutput.AppendLine();
                }
            }
            else if (status.Status == "busy")
            {
                if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
                busyStatusInfo.AppendLine(PipelineHelper.FormatBusyStatus(status.StatusLine, status.Pid, status.Pipeline, status.Duration ?? 0));
            }
            else if (status.Status == "standby")
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
            }
        }

        return new CachedOutputResult(completedOutput.ToString(), busyStatusInfo.ToString());
    }

    private static string? BuildClosedConsoleInfo(List<string> allPipesStatus)
    {
        var closedMessages = allPipesStatus.Where(s => s.Contains("was closed")).ToList();
        if (closedMessages.Count == 0) return null;
        return string.Join("\n", closedMessages);
    }
}