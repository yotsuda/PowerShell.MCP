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
    public IReadOnlyList<string> DetectClosedConsoles(string agentId, int? excludePid = null)
    {
        var closedMessages = new List<string>();
        var previouslyBusyPids = _sessionManager.ConsumeKnownBusyPids(agentId);
        var currentPipes = _sessionManager.EnumeratePipes(_sessionManager.ProxyPid, agentId).ToList();
        var currentPids = currentPipes
            .Select(ConsoleSessionManager.GetPidFromPipeName)
            .Where(p => p.HasValue)
            .Select(p => p.Value)
            .ToHashSet();

        foreach (var pid in previouslyBusyPids)
        {
            if (pid == excludePid) continue;
            if (!currentPids.Contains(pid))
            {
                closedMessages.Add($"  - ⚠ Console {ConsoleSessionManager.Instance.GetConsoleDisplayName(pid)} was closed");
            }
        }

        return closedMessages;
    }

    /// <inheritdoc />
    public async Task<PipeDiscoveryResult> FindReadyPipeAsync(string agentId, CancellationToken cancellationToken, bool includeUnowned = true)
    {
        // Fast path: check active pipe before any expensive operations (EnumeratePipes, DetectClosedConsoles)
        var activePipe = _sessionManager.GetActivePipeName(agentId);
        GetStatusResponse? activePipeStatus = null;
        if (activePipe != null)
        {
            activePipeStatus = await _powerShellService.GetStatusFromPipeAsync(activePipe, cancellationToken);
            if (activePipeStatus != null && activePipeStatus.IsReady())
            {
                if (activePipeStatus.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, activePipeStatus.Pid);
                return new PipeDiscoveryResult(activePipe, false, [], null, activePipeStatus.Cwd);
            }
        }

        // Slow path: active pipe is not ready (or doesn't exist) - full discovery
        var closedMessages = new List<string>();
        var allPipesStatus = new List<string>();

        // Detect externally closed consoles (exclude active pipe PID - it's checked separately below)
        var activePipePid = activePipe != null ? ConsoleSessionManager.GetPidFromPipeName(activePipe) : null;
        closedMessages.AddRange(DetectClosedConsoles(agentId, activePipePid));

        // Process cached active pipe status for closed/busy tracking
        if (activePipe != null)
        {
            if (activePipeStatus == null)
            {
                var consoleName = ConsoleSessionManager.Instance.GetConsoleDisplayName(activePipe);
                _sessionManager.ClearDeadPipe(agentId, activePipe);
                closedMessages.Add($"  - ⚠ Console {consoleName} was closed");
            }
            else if (activePipeStatus.IsReady())
            {
                // Became ready between fast path and slow path (after DetectClosedConsoles)
                if (activePipeStatus.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, activePipeStatus.Pid);
                return new PipeDiscoveryResult(activePipe, false, closedMessages, null, activePipeStatus.Cwd);
            }
            else // busy
            {
                if (activePipeStatus.Pid > 0) _sessionManager.MarkPipeBusy(agentId, activePipeStatus.Pid);
                allPipesStatus.Add($"  - {activePipe}: {activePipeStatus.Status} (pipeline: {activePipeStatus.Pipeline ?? "unknown"}, duration: {activePipeStatus.Duration:F1}s)");
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
                var consoleName = ConsoleSessionManager.Instance.GetConsoleDisplayName(pipeName);
                _sessionManager.ClearDeadPipe(agentId, pipeName);
                closedMessages.Add($"  - ⚠ Console {consoleName} was closed");
                continue;
            }

            if (status.IsReady())
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
                _sessionManager.SetActivePipeName(agentId, pipeName);
                return new PipeDiscoveryResult(pipeName, true, closedMessages, null, status.Cwd);
            }

            if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
            allPipesStatus.Add($"  - {pipeName}: {status.Status} (pipeline: {status.Pipeline ?? "unknown"}, duration: {status.Duration:F1}s)");
        }

        // Step 3: Check unowned pipes (user-started consoles not yet claimed
        // by any proxy). Caller can opt out via includeUnowned=false — see
        // start_console's no-start_location branch for the rationale.
        if (!includeUnowned)
        {
            return new PipeDiscoveryResult(null, false, closedMessages, null);
        }

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
                _ = _powerShellService.ClaimConsoleAsync(pipeName, _sessionManager.ProxyPid, agentId, cancellationToken)
                    .ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                            Console.Error.WriteLine($"[WARN] ClaimConsoleAsync failed: {t.Exception?.InnerException?.Message}");
                    }, TaskScheduler.Default);

                // Calculate the expected new pipe name
                var newPipeName = ConsoleSessionManager.GetPipeNameForPids(_sessionManager.ProxyPid, agentId, pwshPid.Value);

                // Wait for the new pipe to become available (retry with short timeout)
                for (int i = 0; i < 20; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(100, cancellationToken);

                    var newStatus = await _powerShellService.GetStatusFromPipeAsync(newPipeName, cancellationToken);
                    if (newStatus != null)
                    {
                        _sessionManager.SetActivePipeName(agentId, newPipeName);
                        return new PipeDiscoveryResult(newPipeName, true, closedMessages, null, newStatus.Cwd);
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
            else if (status.Status == PipeStatus.Completed)
            {
                if (status.Pid > 0)
                {
                    _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
                    // Record where this completed command left the cwd. A
                    // backgrounded AI Set-Location harvested here (e.g. via
                    // wait_for_completion, which never set LastAiCwd) would
                    // otherwise leave LastAiCwd stale, so the next
                    // invoke_expression sees the moved cwd as drift and
                    // misattributes it to the user. Only MCP/AI-initiated
                    // commands ever reach Completed (user interactive commands
                    // return to Standby, never Completed), so this never
                    // records a user-initiated cwd change.
                    if (!string.IsNullOrEmpty(status.Cwd))
                        _sessionManager.SetLastAiCwd(agentId, status.Pid, status.Cwd);
                }
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
            else if (status.Status == PipeStatus.Busy)
            {
                if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
                busyStatusInfo.AppendLine(PipelineHelper.FormatBusyStatus(status.StatusLine, status.Pid, status.Pipeline, status.Duration ?? 0));
            }
            else if (status.Status == PipeStatus.Standby)
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
            }
        }

        return new CachedOutputResult(completedOutput.ToString(), busyStatusInfo.ToString());
    }

}
