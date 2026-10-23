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
        // Busy PIDs are consumed (the slow path re-marks the ones still busy);
        // owned PIDs stay until the console is seen to be gone. Both are
        // checked: a standby console that closed itself was in neither the
        // busy set nor the active slot, and so used to vanish unreported.
        var candidates = _sessionManager.ConsumeKnownBusyPids(agentId);
        candidates.UnionWith(_sessionManager.GetKnownOwnedPids(agentId));
        var currentPids = _sessionManager.EnumeratePipes(_sessionManager.ProxyPid, agentId)
            .Select(ConsoleSessionManager.GetPidFromPipeName)
            .OfType<int>()
            .ToHashSet();

        foreach (var pid in candidates.OrderBy(p => p))
        {
            if (pid == excludePid) continue;
            if (!currentPids.Contains(pid))
            {
                var reason = ClosedConsoleNotices.TakeReason(_sessionManager.ProxyPid, agentId, pid);
                closedMessages.Add(ReportClosed(agentId, pid, reason));
            }
        }

        return closedMessages;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DetectVanishedConsoles(string agentId, int? excludePid = null)
    {
        var closedMessages = new List<string>();
        var notes = ClosedConsoleNotices.TakeAll(_sessionManager.ProxyPid, agentId);
        var candidates = _sessionManager.GetKnownOwnedPids(agentId);
        candidates.UnionWith(notes.Keys);

        foreach (var pid in candidates.OrderBy(p => p))
        {
            if (pid == excludePid) continue;
            if (notes.TryGetValue(pid, out var reason) || !IsProcessAlive(pid))
            {
                closedMessages.Add(ReportClosed(agentId, pid, reason));
            }
        }

        return closedMessages;
    }

    /// <summary>
    /// The closed-console line for one PID, and the bookkeeping that goes with
    /// it: the display name is read BEFORE ClearDeadPipe forgets it, and the
    /// console leaves every tracking set so it is reported exactly once.
    /// </summary>
    private string ReportClosed(string agentId, int pid, string? reason)
    {
        var name = _sessionManager.GetConsoleDisplayName(pid);
        _sessionManager.ClearDeadPipe(agentId, ConsoleSessionManager.GetPipeNameForPids(_sessionManager.ProxyPid, agentId, pid));
        return "  - " + ClosedConsoleNotices.Describe(name, reason, reason == null && IsProcessAlive(pid));
    }

    private static bool IsProcessAlive(int pid) => ClosedConsoleNotices.IsProcessAlive(pid);

    /// <summary>
    /// The one clause an AI needs about a console it was using that was not
    /// free: its name, what it was doing (its own pipeline, already shortened
    /// by the console; "user command"; a host prompt) and for how long.
    /// Not the full status line: the name would repeat, "Status: Busy" says
    /// nothing "not free" did not, and the same console's fresh status is
    /// usually listed in the response anyway.
    /// </summary>
    internal static string DescribeNotFree(string displayName, GetStatusResponse status)
    {
        var seconds = status.Duration is > 0 ? $", {status.Duration:F1}s" : "";
        if (status.Status == PipeStatus.AwaitingInput)
            return $"{displayName} was waiting for input" + (string.IsNullOrEmpty(status.Message) ? "" : $" ({status.Message})");
        var doing = status.Pipeline == "(user command)" ? "user command" : status.Pipeline ?? status.Status ?? "busy";
        return $"{displayName} was busy ({doing}{seconds})";
    }

    /// <summary>
    /// The closed-console line for a pipe that stopped answering, with the
    /// console's own reason when it left one, or the fact that its process
    /// is still there when it is. Reads the display name before the caller
    /// clears the pipe.
    /// </summary>
    private string DescribeDeadPipe(string agentId, string pipeName)
    {
        var name = _sessionManager.GetConsoleDisplayName(pipeName);
        var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
        var reason = pid.HasValue ? ClosedConsoleNotices.TakeReason(_sessionManager.ProxyPid, agentId, pid.Value) : null;
        var alive = reason == null && pid.HasValue && IsProcessAlive(pid.Value);
        return "  - " + ClosedConsoleNotices.Describe(name, reason, alive);
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

            // "(user command)" is not an observation but a guess from the
            // console's heartbeat (no tick for 10 s). The engine stamps a
            // heartbeat when a command completes, but there are still moments
            // right after a long command when the guess is wrong, and a
            // genuine user command is not going to finish in 150 ms. Looking
            // once more is far cheaper than the console that would otherwise
            // be started — and than explaining it.
            if (activePipeStatus != null && activePipeStatus.Status == PipeStatus.Busy && activePipeStatus.Pipeline == "(user command)")
            {
                await Task.Delay(150, cancellationToken);
                activePipeStatus = await _powerShellService.GetStatusFromPipeAsync(activePipe, cancellationToken);
            }

            if (activePipeStatus != null && activePipeStatus.IsReady())
            {
                if (activePipeStatus.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, activePipeStatus.Pid);
                // Cheap check only (no pipe enumeration): a sibling that closed
                // itself while this console stayed ready would otherwise never
                // be reported — this path is the one every routine call takes.
                var vanished = DetectVanishedConsoles(agentId, activePipeStatus.Pid > 0 ? activePipeStatus.Pid : ConsoleSessionManager.GetPidFromPipeName(activePipe));
                return new PipeDiscoveryResult(activePipe, false, vanished, null, activePipeStatus.Cwd);
            }
        }

        // Slow path: active pipe is not ready (or doesn't exist) - full discovery
        var closedMessages = new List<string>();
        var allPipesStatus = new List<string>();
        string? activeNotFree = null;

        // Detect externally closed consoles (exclude active pipe PID - it's checked separately below)
        var activePipePid = activePipe != null ? ConsoleSessionManager.GetPidFromPipeName(activePipe) : null;
        closedMessages.AddRange(DetectClosedConsoles(agentId, activePipePid));

        // Process cached active pipe status for closed/busy tracking
        if (activePipe != null)
        {
            if (activePipeStatus == null)
            {
                var line = DescribeDeadPipe(agentId, activePipe);
                _sessionManager.ClearDeadPipe(agentId, activePipe);
                closedMessages.Add(line);
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
                // What the AI is told if this ends in another console: which
                // console, and why, as of this moment — nothing more.
                activeNotFree = DescribeNotFree(_sessionManager.GetConsoleDisplayName(activePipe), activePipeStatus);
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
                var line = DescribeDeadPipe(agentId, pipeName);
                _sessionManager.ClearDeadPipe(agentId, pipeName);
                closedMessages.Add(line);
                continue;
            }

            if (status.IsReady())
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
                _sessionManager.SetActivePipeName(agentId, pipeName);
                return new PipeDiscoveryResult(pipeName, true, closedMessages, null, status.Cwd, activeNotFree);
            }

            if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
            allPipesStatus.Add($"  - {pipeName}: {status.Status} (pipeline: {status.Pipeline ?? "unknown"}, duration: {status.Duration:F1}s)");
        }

        // Step 3: Check unowned pipes (user-started consoles not yet claimed
        // by any proxy). Caller can opt out via includeUnowned=false — see
        // start_console's no-start_location branch for the rationale.
        if (!includeUnowned)
        {
            return new PipeDiscoveryResult(null, false, closedMessages, null, null, activeNotFree);
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
                        return new PipeDiscoveryResult(newPipeName, true, closedMessages, null, newStatus.Cwd, activeNotFree);
                    }
                }
            }

            // Not ours: an unowned console that is not ready (or whose claim
            // did not come back in time) is left alone and NOT entered in
            // this agent's busy list. That list is what wait_for_completion
            // and the closed-console notices are built from, and a console
            // this session never owned has no business there — it made
            // wait_for_completion report "Console #… was closed" for a
            // stranger's console and return early.
            allPipesStatus.Add($"  - {pipeName} (unowned): {status.Status}");
        }

        // No ready pipe found
        var statusInfo = allPipesStatus.Count > 0
            ? "All pipes status:\n" + string.Join("\n", allPipesStatus)
            : "No pipes found.";
        return new PipeDiscoveryResult(null, false, closedMessages, statusInfo, null, activeNotFree);
    }

    /// <inheritdoc />
    public async Task<CachedOutputResult> CollectAllCachedOutputsAsync(string agentId, string? excludePipeName, CancellationToken cancellationToken)
    {
        var completedOutputs = new List<string>();
        var busyStatusLines = new List<string>();
        var closedLines = new List<string>();
        // With an excluded pipe, the caller's own console is left out and
        // everything collected here belongs to other consoles, so mark it.
        // wait_for_completion passes null: every console is one it is waiting
        // on, none is "other".
        string Mark(string block) => excludePipeName != null ? PipelineHelper.MarkOtherConsole(block) : block;

        foreach (var pipeName in _sessionManager.EnumeratePipes(_sessionManager.ProxyPid, agentId))
        {
            if (pipeName == excludePipeName) continue;

            var status = await _powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                // A console that died between discovery and this collection
                // used to be dropped from tracking here in silence, and so was
                // never reported at all. Say it now, like everywhere else.
                var line = DescribeDeadPipe(agentId, pipeName);
                _sessionManager.ClearDeadPipe(agentId, pipeName);
                closedLines.Add(line);
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
                    // execute_command sees the moved cwd as drift and
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
                    completedOutputs.Add(Mark(output));
                }
            }
            else if (PipeStatus.IsBusy(status.Status))
            {
                // IsBusy includes AwaitingInput (a command parked at a host
                // prompt). Surface it in the busy list so a wait_for_completion
                // that times out still tells the AI the console is occupied
                // (its StatusLine carries the answer-or-close_console guidance).
                if (status.Pid > 0) _sessionManager.MarkPipeBusy(agentId, status.Pid);
                busyStatusLines.Add(Mark(PipelineHelper.FormatBusyStatus(status.StatusLine, status.Pid, status.Pipeline, status.Duration ?? 0)));
            }
            else if (status.Status == PipeStatus.Standby)
            {
                if (status.Pid > 0) _sessionManager.UnmarkPipeBusy(agentId, status.Pid);
            }
        }

        // One blank line between consoles, in both lists.
        return new CachedOutputResult(
            PipelineHelper.JoinBlocks([.. completedOutputs]),
            PipelineHelper.JoinBlocks([.. busyStatusLines]),
            string.Join("\n", closedLines));
    }

}
