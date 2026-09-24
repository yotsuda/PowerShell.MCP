using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Linq;
using System.Text;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Models;
using System.Text.Json;
using PowerShell.MCP.Proxy.Helpers;

namespace PowerShell.MCP.Proxy.Tools;

[McpServerToolType]
public class PowerShellTools
{
    /// <summary>
    /// Finds a ready pipe. Delegates to PipeDiscoveryService.
    /// </summary>
    private static async Task<(string? readyPipeName, bool consoleSwitched, IReadOnlyList<string> closedConsoleMessages, string? allPipesStatusInfo, string? liveCwd)> FindReadyPipeAsync(
        IPipeDiscoveryService pipeDiscoveryService,
        string agentId,
        CancellationToken cancellationToken)
    {
        var result = await pipeDiscoveryService.FindReadyPipeAsync(agentId, cancellationToken);
        return (result.ReadyPipeName, result.ConsoleSwitched, result.ClosedConsoleMessages, result.AllPipesStatusInfo, result.LiveCwd);
    }

    /// <summary>
    /// Checks whether the AI's intended cwd has drifted from the live cwd
    /// (typically because the user typed <c>cd</c> in the visible console
    /// between AI calls). Returns the AI cwd we want to restore to plus
    /// the live cwd we observed, or null when no drift / no LastAiCwd /
    /// missing live cwd. The caller prepends a Set-Location preamble to
    /// the AI's pipeline and surfaces a routing notice on drift.
    /// </summary>
    private static (string AiCwd, string LiveCwd)? DetectCwdDrift(string? readyPipeName, string? liveCwd)
    {
        if (string.IsNullOrEmpty(readyPipeName) || string.IsNullOrEmpty(liveCwd))
            return null;
        var pid = ConsoleSessionManager.GetPidFromPipeName(readyPipeName);
        if (!pid.HasValue) return null;
        var aiCwd = ConsoleSessionManager.Instance.GetLastAiCwd(pid.Value);
        if (string.IsNullOrEmpty(aiCwd)) return null;
        // Normalize separators (GetFullPath) and strip a trailing directory
        // separator (TrimEndingDirectorySeparator — GetFullPath alone keeps it)
        // so "C:\Project" and "C:\Project\" don't trigger a spurious cd. Drive
        // roots ("C:\", "/") keep their separator. Comparison is
        // case-insensitive on Windows, case-sensitive elsewhere.
        var normalizedAi = Path.TrimEndingDirectorySeparator(Path.GetFullPath(aiCwd));
        var normalizedLive = Path.TrimEndingDirectorySeparator(Path.GetFullPath(liveCwd));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedAi, normalizedLive, comparison)
            ? null
            : (aiCwd, liveCwd);
    }

    private static string FormatBusyStatus(GetStatusResponse status)
        => PipelineHelper.FormatBusyStatus(status.StatusLine, status.Pid, status.Pipeline, status.Duration ?? 0);

    /// <summary>
    /// Collects cached outputs and busy status from all pipes. Delegates to PipeDiscoveryService.
    /// </summary>
    private static async Task<(string completedOutput, string busyStatusInfo)> CollectAllCachedOutputsAsync(
        IPipeDiscoveryService pipeDiscoveryService,
        string agentId,
        string? excludePipeName,
        CancellationToken cancellationToken)
    {
        var result = await pipeDiscoveryService.CollectAllCachedOutputsAsync(agentId, excludePipeName, cancellationToken);
        return (result.CompletedOutput, result.BusyStatusInfo);
    }

    private static string GetConsoleName(string? pipeName)
        => ConsoleSessionManager.Instance.GetConsoleDisplayName(pipeName);

    /// <summary>
    /// Sets the console window title if not already set
    /// </summary>
    private static async Task SetConsoleTitleAsync(IPowerShellService powerShellService, string pipeName, CancellationToken cancellationToken)
    {
        var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
        if (pid == null) return;

        var title = ConsoleSessionManager.Instance.TryAssignNameToPid(pid.Value);
        if (title == null) return;
        await powerShellService.SetWindowTitleAsync(pipeName, title, cancellationToken);
    }

    /// <summary>
    /// Shows the green claim notice on a console that was just claimed — the
    /// visible counterpart to the yellow "AI session disconnected" notice the
    /// polling engine prints when a console loses its owner. Uses the
    /// caller-supplied banner when present, otherwise the default
    /// "AI session connected." line, so a custom banner naturally REPLACES
    /// (never doubles) the generic notice. Rendered as a silent command (no
    /// command echo) with a trailing prompt so the console is left ready.
    /// </summary>
    internal static async Task ShowClaimNoticeAsync(IPowerShellService powerShellService, string pipeName, string? banner, CancellationToken cancellationToken)
    {
        var message = string.IsNullOrEmpty(banner) ? "AI session connected." : banner;
        var escaped = message.Replace("'", "''");
        await powerShellService.ExecuteSilentAsync(
            pipeName,
            $"[Console]::WriteLine(); [Console]::WriteLine(); Write-Host '{escaped}' -ForegroundColor Green; [Console]::WriteLine(); try {{ $p = & {{ prompt }}; [Console]::Write($p.TrimEnd(' ').TrimEnd('>') + '> ' + \"`e[0K\") }} catch {{ [Console]::Write(\"PS $((Get-Location).Path)> `e[0K\") }}",
            cancellationToken);
    }

    /// <summary>
    /// Resolves the agent ID from is_subagent and agent_id parameters.
    /// If is_subagent=true and agent_id is empty, allocates a new ID.
    /// Returns (agentId, isNewlyAllocated, errorMessage).
    /// </summary>
    private static (string agentId, bool isNewlyAllocated, string? error) ResolveAgentId(bool isSubAgent, string? agentId)
    {
        if (!string.IsNullOrEmpty(agentId))
        {
            // Validate provided agent_id
            var resolved = agentId!;
            if (!ConsoleSessionManager.Instance.IsValidAgentId(resolved))
                return (resolved, false, $"❌ Invalid agent_id '{resolved}'. Sub-agents must first call start_console with is_subagent=true to obtain a valid agent_id. Do not pass arbitrary strings as agent_id.");
            return (resolved, false, null);
        }

        if (isSubAgent)
        {
            // Allocate new agent ID
            var newId = ConsoleSessionManager.Instance.AllocateSubAgentId();
            return (newId, true, null);
        }

        return ("default", false, null);
    }

    /// <summary>
    /// Prepends the 🔑 agent_id notice to a tool response when a new sub-agent
    /// ID was just allocated. No-op for the common case (isNewlyAllocated=false),
    /// so this can be applied unconditionally to every return path of a tool method.
    /// Centralizing the emit here is what guarantees the notice can't be dropped by
    /// timeout / cached-completed / error / busy-route / drift-bail / auto-start
    /// branches the way it was when each branch had to remember to add it.
    /// </summary>
    private static string PrependAgentIdNoticeIfNew(string body, bool isNewlyAllocated, string agentId)
    {
        if (!isNewlyAllocated) return body;
        return $"🔑 Your agent_id is: {agentId} — pass this in all subsequent tool calls.\n\n{body}";
    }

    [McpServerTool]
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns a JSON document with a system section (OS, PowerShell version, host, user, culture, timezone) and an available_drives array (each drive's name, provider, root, and current path). Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple execute_command commands in succession, calling once at the beginning is sufficient.")]
    public static async Task<string> GetCurrentLocation(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        [Description("Agent ID for sub-agent console isolation. Obtain this by calling start_console with is_subagent=true. Do not pass arbitrary strings.")]
        string? agent_id = null,
        [Description("Set to true if you are a sub-agent. A unique agent_id will be allocated and returned in the response. Use that agent_id for all subsequent tool calls.")]
        bool is_subagent = false,
        CancellationToken cancellationToken = default)
    {
        var (agentId, isNewlyAllocated, error) = ResolveAgentId(is_subagent, agent_id);
        // Wrap every return so the 🔑 notice can never be dropped on a sub-agent's
        // first call regardless of which branch builds the response.
        string Wrap(string r) => PrependAgentIdNoticeIfNew(r, isNewlyAllocated, agentId);
        if (error != null)
            return Wrap(error);

        // Find a ready pipe
        var (readyPipeName, consoleSwitched, closedConsoleMessages, allPipesStatusInfo, liveCwd) = await FindReadyPipeAsync(pipeDiscoveryService, agentId, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start (StartConsole includes busy info collection).
            // Pass is_subagent: false because the sub-agent ID was already allocated
            // here; if we passed it as true the inner StartConsole would skip its own
            // ResolveAgentId allocation but our Wrap would still add the notice. Either
            // shape works; passing false makes the inner call's intent explicit.
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting... Reason: {allPipesStatusInfo}");
            return Wrap(await StartConsole(powerShellService, pipeDiscoveryService, agent_id: agentId, is_subagent: false, cancellationToken: cancellationToken));
        }

        try
        {
            // Set console window title if this was a newly claimed (unowned) console.
            // Without this, get_current_location as the first tool call after the user
            // ran Import-Module PowerShell.MCP would leave the title as the placeholder
            // "#PID ____" until some other tool that handles consoleSwitched runs.
            if (consoleSwitched)
            {
                await SetConsoleTitleAsync(powerShellService, readyPipeName, cancellationToken);
                await ShowClaimNoticeAsync(powerShellService, readyPipeName, null, cancellationToken);
            }

            // First attach for this agent (resume / cold-start). Unlike
            // execute_command, get_current_location is a query — its whole job
            // is to report where the console is, so we PRESERVE the (possibly
            // user-prepared) cwd and do not move to $HOME. We only surface the
            // "new server session" notice so the AI knows prior variables/modules
            // may not be present. Only fires on a genuine first reclaim
            // (consoleSwitched); mid-session switches return false.
            string? newSessionNotice = null;
            if (consoleSwitched && ConsoleSessionManager.Instance.TryMarkFirstAttach(agentId))
            {
                newSessionNotice = BuildNewSessionNotice(GetConsoleName(readyPipeName), liveCwd ?? "the console's current location", null);
            }

            // Get location (DLL will include its own cached outputs automatically)
            var result = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);

            // Collect completed outputs and busy status info from other pipes
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

            // Build response: closedConsoles + busyStatusInfo + completedOutputs + agentId info + result
            return Wrap(PipelineHelper.JoinBlocks(
                string.Join("\n", closedConsoleMessages),
                newSessionNotice,
                busyStatusInfo,
                completedOutputs,
                result));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] GetCurrentLocation failed: {ex.Message}");
            return Wrap($"Failed to get current location: {ex.Message}\n\nPlease try again. A new console will be started automatically if needed.");
        }
    }

    [McpServerTool]
    [Description(@"Interrupt the command currently running in the agent's active console. Stops a runaway/long-running PowerShell pipeline (Start-Sleep, a loop, a slow cmdlet) by stopping the running pipeline, AND sends Ctrl+C to break a native CLI that is looping or waiting on stdin (git, npm, ssh, ping, etc.). A command stuck in a non-cooperative blocking call (e.g. [Threading.Thread]::Sleep, a blocking socket/read) may NOT stop — use close_console for that. It also does NOT cancel a PowerShell host prompt (Read-Host, a missing mandatory parameter, Get-Credential) — those are reported as 'awaiting_input', recovered by answering at the console or close_console. Targets the agent's active console directly — it does NOT route to a different console.")]
    public static async Task<string> Cancel(
        IPowerShellService powerShellService,
        [Description("Agent ID for sub-agent console isolation. Obtain this by calling start_console with is_subagent=true.")]
        string? agent_id = null,
        [Description("Set to true if you are a sub-agent.")]
        bool is_subagent = false,
        CancellationToken cancellationToken = default)
    {
        var (agentId, isNewlyAllocated, resolveError) = ResolveAgentId(is_subagent, agent_id);
        string Wrap(string r) => PrependAgentIdNoticeIfNew(r, isNewlyAllocated, agentId);
        if (resolveError != null)
            return Wrap(resolveError);

        var pipeName = ConsoleSessionManager.Instance.GetActivePipeName(agentId);
        if (pipeName == null)
            return Wrap("No active console to cancel. Run a command first with execute_command.");

        var ack = await powerShellService.CancelAsync(pipeName, cancellationToken);
        if (ack == null || ack.Status != "success")
            return Wrap($"Cancel request to console {GetConsoleName(pipeName)} failed or was not acknowledged. The console may have already finished or been closed.");

        return Wrap($"✓ Interrupt sent to console {GetConsoleName(pipeName)} (Ctrl+C + pipeline stop). A running PowerShell pipeline or native CLI should now be stopped and the console back to ready; a command stuck in a non-cooperative blocking call may still be running (use close_console). Call execute_command (or get_current_location) to confirm and to drain any output the cancelled command produced.");
    }

    /// <summary>
    /// Refusal text for a close_console PID this session holds no pipe for.
    /// "Not owned" used to be the only answer, and it reads as an ownership
    /// fault even when the console is simply gone: typically an idle standby
    /// console that closed itself minutes earlier. The three cases call for
    /// different follow-ups, so each gets its own message.
    /// </summary>
    internal static string BuildCloseRefusal(int pid, string ownedList, bool processRunning, bool isMcpConsole)
    {
        if (!processRunning)
        {
            return $"PID {pid} is no longer running, so there is nothing to close: the console has already exited " +
                   "(for example, an idle standby console that closed itself after a period without use). " +
                   $"Consoles owned by this session: {ownedList}.";
        }

        if (isMcpConsole)
        {
            return $"PID {pid} is a PowerShell.MCP console, but it belongs to another session or agent, or was started by a user " +
                   $"(owned by this session: {ownedList}). Refusing to close it.";
        }

        return $"PID {pid} is not a PowerShell.MCP console owned by this session (owned: {ownedList}). Refusing to close it.";
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [McpServerTool]
    [Description(@"Forcibly close (kill) a PowerShell console by its PID. Use to abandon a console paused at an interactive prompt (see the awaiting-input notice) or stuck on a runaway command. The PID is the number shown in tool output as '#<pid> <name>'. Only consoles owned by this session can be closed. Any undrained output the console still holds from an already-finished command — including on a busy console that is running a new command while an earlier result sits uncollected — is harvested and returned in this response before the console is killed. The next command auto-starts a fresh console.

If the target has unsubmitted text at its prompt, a human is typing in it: the first call REFUSES, closes nothing, and leaves its pending output untouched. Call close_console for the same pid again to close it anyway — the retry is the confirmation, so only repeat it if you actually intend to kill a console someone is using. A console that is merely awaiting_input (a program waiting on Read-Host, not a human mid-keystroke) closes on the first call as usual.")]
    public static async Task<string> CloseConsole(
        IPowerShellService powerShellService,
        [Description("PID of the console to close (the number in '#<pid> <name>').")]
        int pid,
        [Description("Agent ID for sub-agent console isolation. Obtain this by calling start_console with is_subagent=true.")]
        string? agent_id = null,
        [Description("Set to true if you are a sub-agent.")]
        bool is_subagent = false,
        CancellationToken cancellationToken = default)
    {
        var (agentId, isNewlyAllocated, resolveError) = ResolveAgentId(is_subagent, agent_id);
        string Wrap(string r) => PrependAgentIdNoticeIfNew(r, isNewlyAllocated, agentId);
        if (resolveError != null)
            return Wrap(resolveError);

        var sessionManager = ConsoleSessionManager.Instance;

        // Safety: only kill a pwsh that this proxy/agent actually owns a pipe
        // for — never an arbitrary PID.
        var ownedPids = sessionManager.EnumeratePipes(sessionManager.ProxyPid, agentId)
            .Select(ConsoleSessionManager.GetPidFromPipeName)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToHashSet();

        if (!ownedPids.Contains(pid))
        {
            var known = ownedPids.Count > 0 ? string.Join(", ", ownedPids.OrderBy(p => p)) : "none";
            var isMcpConsole = sessionManager.EnumeratePipes()
                .Select(ConsoleSessionManager.GetPidFromPipeName)
                .Any(p => p == pid);
            return Wrap(BuildCloseRefusal(pid, known, IsProcessRunning(pid), isMcpConsole));
        }

        var pipeName = ConsoleSessionManager.GetPipeNameForPids(sessionManager.ProxyPid, agentId, pid);

        // Human-presence gate. Probe with get_status FIRST — it has no side
        // effects, unlike the drain below, which consumes the console's cached
        // output. Draining before a possible refusal would swallow that output
        // on a console we then leave alive, and it would look to the AI like the
        // result simply vanished.
        bool probeOk = false;
        bool typedText = false;
        double sampleAge = -1;
        try
        {
            var st = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);
            if (st != null)
            {
                probeOk = true;
                typedText = st.TypedText;
                sampleAge = st.TypedTextAgeSeconds;
            }
        }
        catch (Exception ex)
        {
            // Fail open — see CloseConsoleGuard: an unreachable console is
            // precisely what close_console exists to deal with.
            Console.Error.WriteLine($"[WARN] CloseConsole presence probe for #{pid} failed: {ex.Message}");
        }

        if (CloseConsoleGuard.Evaluate(pipeName, typedText, sampleAge, probeOk) == CloseDecision.Refuse)
        {
            // Deliberately a normal successful result, never an error: the retry
            // is the confirmation, so anything that auto-retries errors would
            // turn a refusal into a kill.
            return Wrap(
                $"⚠ Console #{pid} has unsubmitted text at its prompt — a human appears to be typing there. " +
                $"NOT closed, and its pending output was left untouched.\n\n" +
                $"If you still need to close it, call close_console for the same pid again within " +
                $"{CloseConsoleGuard.ArmTtlSeconds}s and it will close without this check.");
        }

        // Before killing, harvest any undrained output the console is still
        // holding, REGARDLESS of its current status. A console that finished a
        // command while the AI wasn't listening parks the result in the DLL's
        // local cache — and this is not limited to the "completed" status: a
        // BUSY console (running a new command) can still be holding an earlier
        // command's undrained output, because ExecutionState.Status reports
        // "busy" and masks the pending cache. consume_output only pulls
        // already-cached completed results — it never touches the running
        // command and returns empty when there is nothing — so it is safe in
        // every state. Killing without this would silently discard that output.
        // Best-effort: a console mid-teardown or an unreachable pipe must never
        // block the close, so any failure here just proceeds to kill.
        string? drainedOutput = null;
        try
        {
            var output = await powerShellService.ConsumeOutputFromPipeAsync(pipeName, cancellationToken);
            if (!string.IsNullOrEmpty(output))
                drainedOutput = output;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] CloseConsole drain for #{pid} failed: {ex.Message}");
        }

        try
        {
            System.Diagnostics.Process.GetProcessById(pid).Kill();
        }
        catch (ArgumentException)
        {
            // Process already gone — fall through to clear tracking.
        }
        catch (Exception ex)
        {
            return Wrap($"Failed to close console #{pid}: {ex.Message}");
        }

        sessionManager.ClearDeadPipe(agentId, pipeName);
        CloseConsoleGuard.Disarm(pipeName);

        return Wrap(PipelineHelper.JoinBlocks(
            drainedOutput,
            $"✓ Closed console #{pid}. The next command will auto-start a fresh console."));
    }

    /// <summary>
    /// Carries the resolved agent id back out of a handler that allocates it
    /// internally, so the [McpServerTool] shell knows which console to stash
    /// an undelivered response on.
    /// </summary>
    private sealed class AgentIdBox
    {
        public string? Value;
    }

    // The stash keeps whatever the response was, including a timeout notice
    // whose "use wait_for_completion / cancel / close_console" guidance was
    // true when written and may well be obsolete by the time it is read. It is
    // kept anyway — a timeout response also carries output drained from other
    // consoles, which dropping it would destroy — so the banner has to warn
    // that this is a snapshot rather than the current state.
    private const string UndeliveredResultNotice =
        "⚠ Result of an earlier tool call that the client had already given up on (timeout or cancel), delivered here instead. "
        + "It is a snapshot from that moment: any instruction inside it (wait_for_completion, cancel, close_console) may no longer apply — check the current state before acting on it.";

    /// <summary>
    /// Delivery guard for a response the MCP client may no longer be
    /// listening for.
    ///
    /// When a client cancels a call — Claude Code sends
    /// <c>notifications/cancelled</c> on its tool timeout, which the MCP SDK
    /// turns into the cancellation of this token — the JSON-RPC response we
    /// are about to return is dropped: nothing is registered against its id
    /// any more. For a pipeline that finished normally that is not a delay
    /// but a loss, because the DLL already emptied the console's cache into
    /// this very response (NamedPipeServer's "success" branch calls
    /// ConsumeCachedOutputs). Nobody holds a second copy.
    ///
    /// So push the response back into the console's cache. Every existing
    /// drain point picks it up on the next tool call — execute_command's
    /// Completed branch, wait_for_completion, CollectAllCachedOutputsAsync —
    /// and ExecutionState.Status flips to "completed", so the console
    /// advertises that it is holding something.
    ///
    /// This is a net, not the primary defence: a client that abandons a call
    /// WITHOUT sending notifications/cancelled leaves the token unsignalled
    /// and this never fires. Keeping the DLL-side execute_command timeout
    /// below the client's tool timeout stays the mechanism that actually
    /// guarantees delivery.
    /// </summary>
    private static async Task<string> StashIfCancelledAsync(
        IPowerShellService powerShellService,
        string? agentId,
        string response,
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested || string.IsNullOrWhiteSpace(response))
            return response;

        if (string.IsNullOrEmpty(agentId))
        {
            Console.Error.WriteLine("[WARN] Client cancelled the call before an agent was resolved - response could not be preserved.");
            return response;
        }

        var sessionManager = ConsoleSessionManager.Instance;

        // Any of the agent's live consoles will do. The response can hold
        // output drained from several of them at once (CollectAllCachedOutputs),
        // so it does not belong to one console in particular, and the active
        // pipe name can be stale — the console may have been closed between the
        // pipeline finishing and this pushback, which is exactly the case
        // wait_for_completion's closed-console branch reports. Falling back
        // keeps the text alive on a console that is still there instead of
        // discarding it with the one that went away.
        var active = sessionManager.GetActivePipeName(agentId);
        var candidates = new List<string>();
        if (active != null) candidates.Add(active);
        candidates.AddRange(sessionManager.EnumeratePipes(sessionManager.ProxyPid, agentId)
            .Where(p => p != active));

        var payload = UndeliveredResultNotice + "\n\n" + response;
        foreach (var pipeName in candidates)
        {
            try
            {
                if (await powerShellService.CacheOutputToPipeAsync(pipeName, payload))
                {
                    Console.Error.WriteLine($"[INFO] Client cancelled the call; response re-cached on console {sessionManager.GetConsoleDisplayName(pipeName)} for delivery on the next tool call.");
                    return response;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARN] Re-cache attempt on {pipeName} threw: {ex.Message}");
            }
        }

        // The DLL already emptied its cache into the response we are holding,
        // so with no console left to take it back there is no other copy.
        Console.Error.WriteLine($"[WARN] Client cancelled the call and none of the agent's {candidates.Count} console(s) accepted the pushback - the result is lost.");
        return response;
    }

    [McpServerTool]
    [Description(@"Execute PowerShell cmdlets and CLI tools (e.g., git) in persistent console. Session persists: modules, variables, functions, authentication stay active—no re-authentication. Install any modules and learn them via Get-Help.

📌 This is your primary tool for all command execution tasks: directory navigation, git operations, build/test commands, file system operations, process management, environment variable access, and any shell/terminal task. Sessions persist across calls—variables, modules, functions, and authentication stay active. Install any PowerShell Gallery module to extend capabilities (e.g., Az for Azure, AWS.Tools for AWS, Microsoft.Graph for M365).

💡 API Exploration: Use Invoke-RestMethod to explore Web APIs and Add-Type for Win32 API testing. Verify API behavior before writing production code—get immediate feedback without compilation.

⚠️ CRITICAL - Variable Scope:
Local variables are NOT preserved between execute_command calls. Use $script: or $global: scope to share variables across calls.

⚠️ CRITICAL - String Interpolation:
Double-quoted strings expand variables and subexpressions: ""$var"" becomes the value of $var, ""$(expr)"" evaluates expr. Use single quotes for literal strings: '$var' keeps the text $var as-is.

⚠️ A native command that prints nothing and never returns, with its CPU idle, is not slow — it is queued behind this console's prompt input read and will not return on its own. Re-run it detached with its standard input from NUL:
cmd /d /s /c ""<command> < NUL > out.log 2>&1""
Rare: common CLIs (git, node, npm, python, dotnet, curl, ssh) are unaffected. It hits programs that probe the console while starting up; LilyPond is a known one.

📝 Text File Operations:
ALWAYS use the specialized cmdlets for text file editing: Show-TextFiles, Add-LinesToFile, Update-LinesInFile, Update-MatchInFile, Remove-LinesFromFile.
NEVER use Set-Content, [IO.File]::WriteAllText, or other alternatives—even when source code contains $ or backtick characters. Instead, pass content via var1-var4 parameters.
Create new file: Add-LinesToFile path -Content $var1 (with var1 parameter containing the content)
Edit existing file: Add-LinesToFile, Update-LinesInFile, Update-MatchInFile, Remove-LinesFromFile (use var1-var4 for content with $, backtick, or quotes)
For detailed examples: execute_command('Get-Help <cmdlet-name> -Examples')
Edit cmdlets show changed lines with 2 lines of context. Use Show-TextFiles after editing if you need the full file view.

📌 Prefer these cmdlets over other file read/edit/search tools provided by the host application. They handle special characters ($, backtick, double-quote) safely via var1-var4 parameters, and keep all operations in a single persistent session without context switching.

🤖 AI Cmdlets (requires PromptAI module: Install-Module PromptAI):
Invoke-Claude ""prompt"" — Call Anthropic Claude API. Invoke-GPT ""prompt"" — Call OpenAI API. Invoke-Gemini ""prompt"" — Call Google Gemini API. All support -Model and -SystemPrompt.

🔤 Variables Parameter:
Use var1/var2/var3/var4 parameters to inject literal string values into the pipeline, bypassing the PowerShell parser. Reference them as $var1/$var2/$var3/$var4 in the pipeline.
When editing source code files, ALWAYS use variables for -OldText, -Replacement, -Content parameters to avoid unintended expansion of $, backtick, or double-quote characters.")]
    public static async Task<string> ExecuteCommand(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        McpServer mcpServer,
        [Description("The PowerShell command or pipeline to execute. Multi-line commands (if, loops, try-catch, etc.) are supported.")]
        string pipeline,
        [Description("Timeout in seconds (default: 170). Automatically capped to what the connected MCP client is known to wait for, so a longer value is silently reduced. On timeout, execution continues in background and result is cached for retrieval on next tool call. PowerShell host prompts (Read-Host, Get-Credential, a missing mandatory parameter) return control immediately as awaiting_input regardless of this value. Use 0 for native CLIs that wait on stdin (e.g., cmd /c pause, ssh, npm login) so the call returns at once instead of waiting out the timeout.")]
        int timeout_seconds = 170,
        [Description("Literal string value injected as $var1 in the pipeline, bypassing the PowerShell parser.")]
        string? var1 = null,
        [Description("Literal string value injected as $var2 in the pipeline, bypassing the PowerShell parser.")]
        string? var2 = null,
        [Description("Literal string value injected as $var3 in the pipeline, bypassing the PowerShell parser.")]
        string? var3 = null,
        [Description("Literal string value injected as $var4 in the pipeline, bypassing the PowerShell parser.")]
        string? var4 = null,
        [Description("Agent ID for sub-agent console isolation. Obtain this by calling start_console with is_subagent=true. Do not pass arbitrary strings.")]
        string? agent_id = null,
        [Description("Set to true if you are a sub-agent. A unique agent_id will be allocated and returned in the response. Use that agent_id for all subsequent tool calls.")]
        bool is_subagent = false,
        CancellationToken cancellationToken = default)
    {
        // How long we may block before the DLL has to hand back a timeout
        // depends on how long THIS client is willing to wait, which we can
        // only infer from the identity it gave in the MCP handshake.
        TimeoutCeiling.ObserveClient(mcpServer?.ClientInfo?.Name);

        // Thin delivery-guard shell around the real handler. The busy
        // auto-route recursion inside re-enters ExecuteCommandCore directly,
        // so a nested call never stashes its own slice of a response the
        // outer call is about to stash whole.
        var agentIdBox = new AgentIdBox();
        // Clock the whole call, not just the pipeline: everything before
        // dispatch (console discovery, claim, a cold spawn) is spent out of
        // the same budget the client is counting down. See
        // TimeoutCeiling.RemainingFor.
        var callClock = System.Diagnostics.Stopwatch.StartNew();
        var response = await ExecuteCommandCore(
            powerShellService, pipeDiscoveryService, pipeline, timeout_seconds,
            var1, var2, var3, var4, agent_id, is_subagent, agentIdBox, callClock, cancellationToken);
        return await StashIfCancelledAsync(powerShellService, agentIdBox.Value, response, cancellationToken);
    }

    /// <summary>
    /// The execute_command handler proper. Split out from the
    /// [McpServerTool] entry point above so the busy auto-route recursion
    /// re-enters here rather than through the cancellation stash.
    /// </summary>
    private static async Task<string> ExecuteCommandCore(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        string pipeline,
        int timeout_seconds,
        string? var1,
        string? var2,
        string? var3,
        string? var4,
        string? agent_id,
        bool is_subagent,
        AgentIdBox? agentIdBox,
        System.Diagnostics.Stopwatch callClock,
        CancellationToken cancellationToken)
    {
        // Clamp to the ceiling in force for the connected client (see
        // TimeoutCeiling): blocking past what the client will wait for turns
        // a completed pipeline's output into something only a cancellation
        // notification can rescue.
        timeout_seconds = TimeoutCeiling.Clamp(timeout_seconds, 0);

        // Build variables dictionary from var1/var2/var3/var4 parameters
        Dictionary<string, string>? parsedVariables = null;
        if (var1 != null || var2 != null || var3 != null || var4 != null)
        {
            parsedVariables = new Dictionary<string, string>();
            if (var1 != null) parsedVariables["var1"] = var1;
            if (var2 != null) parsedVariables["var2"] = var2;
            if (var3 != null) parsedVariables["var3"] = var3;
            if (var4 != null) parsedVariables["var4"] = var4;
        }

        var (agentId, isNewlyAllocated, resolveError) = ResolveAgentId(is_subagent, agent_id);
        // Hand the resolved id to the [McpServerTool] shell: for a sub-agent's
        // first call the id is minted here, and the shell needs it to pick the
        // console that an undelivered response gets stashed on.
        if (agentIdBox != null) agentIdBox.Value = agentId;
        // Wrap every return so the 🔑 notice can never be dropped on a sub-agent's
        // first call regardless of which branch builds the response.
        string Wrap(string r) => PrependAgentIdNoticeIfNew(r, isNewlyAllocated, agentId);
        if (resolveError != null)
            return Wrap(resolveError);

        var sessionManager = ConsoleSessionManager.Instance;
        // Find a ready pipe
        var (readyPipeName, consoleSwitched, closedConsoleMessages, allPipesStatusInfo, liveCwd) = await FindReadyPipeAsync(pipeDiscoveryService, agentId, cancellationToken);

        // The startup notice surfaces "spawned a new console" / "switched
        // to a sibling" context to the AI. Pre-1.9 these paths returned
        // early with `Pipeline NOT executed - verify location and
        // re-execute`, forcing a second tool call. The new contract:
        // spawn / switch silently and run the pipeline in the same call.
        // The user-cd drift case still bails (see drift block below) — that
        // bail surfaces its own notice; the startup notice is folded into
        // it when both fire on the same call.
        string? startupNotice = null;

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start, then fall through to execute the
            // AI's pipeline at the new console in the same tool call.
            // Pick the start_location the same way busy-route does: prefer
            // the agent's session-scoped LastAiCwd so the new console
            // resumes where the AI was working, falling back to UserProfile
            // when this agent has no recorded cwd (first call, fresh agent).
            // Validate Directory.Exists so a since-deleted folder doesn't
            // wedge the spawn.
            var sessionAiCwd = sessionManager.GetSessionLastAiCwd(agentId);
            var autoStartLocation = (!string.IsNullOrEmpty(sessionAiCwd) && Directory.Exists(sessionAiCwd))
                ? sessionAiCwd
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting at {autoStartLocation}... Reason: {allPipesStatusInfo}");
            // Spawning a console for the AI's pipeline is a new attach, so show
            // the default green "AI session connected." notice at startup (same
            // lifecycle marker as a claim).
            var (success, locationResult) = await StartConsoleInternal(powerShellService, agentId, BuildStartupCommands(null, null), autoStartLocation, cancellationToken);
            if (!success)
            {
                return Wrap(locationResult); // Error message
            }

            // Pick up the freshly-spawned pipe so the rest of this call
            // executes against it. GetActivePipeName returns the newly
            // attached pipe because StartConsoleInternal wires SetActivePipeName.
            readyPipeName = sessionManager.GetActivePipeName(agentId);
            if (readyPipeName == null)
            {
                return Wrap("Failed to acquire newly-started console pipe.");
            }

            await SetConsoleTitleAsync(powerShellService, readyPipeName, cancellationToken);

            // Probe the new console for its live cwd — fresh pwsh starts at
            // home, but a future StartConsoleInternal might honor an explicit
            // start_location. Drift check below depends on this value.
            var newStatus = await powerShellService.GetStatusFromPipeAsync(readyPipeName, cancellationToken);
            liveCwd = newStatus?.Cwd;

            startupNotice = $"ℹ️ Started new console {GetConsoleName(readyPipeName)} with PowerShell.MCP module imported. Pipeline running on the new console.";
        }
        else if (consoleSwitched)
        {
            // Console switched (active died → sibling owned pipe, or
            // unowned pipe claimed). Set the title and fall through to
            // execute. Drift check below catches the case where the
            // sibling has its own LastAiCwd from prior AI work.
            await SetConsoleTitleAsync(powerShellService, readyPipeName, cancellationToken);
            await ShowClaimNoticeAsync(powerShellService, readyPipeName, null, cancellationToken);
            startupNotice = $"ℹ️ Switched to existing console {GetConsoleName(readyPipeName)}. Pipeline running there.";
        }

        // Check for local variable assignments without scope prefix.
        // Per-agent dedup: same var name never double-warns the same
        // conversation, so long sessions don't drown in reminders.
        var scopeWarning = CheckLocalVariableAssignments(pipeline, agentId);

        // The AI's own pipeline, captured before the first-attach preamble
        // below prepends a Set-Location of our own — the redundant-cd hint
        // must judge only what the AI wrote.
        var aiPipeline = pipeline;

        // Enforce var1/var2 usage for text editing cmdlets
        var var1Error = PipelineHelper.CheckVar1Enforcement(pipeline, var1, var2);
        if (var1Error != null)
        {
            return Wrap(var1Error);
        }

        // First console attach for this agent in the proxy's lifetime — the
        // resume / cold-start boundary. The proxy is fresh but the AI may carry
        // a stale cwd/state assumption from earlier in the conversation, and the
        // console we just spawned/reclaimed could be sitting anywhere. Normalize
        // to $HOME so this first command runs at a predictable baseline (not an
        // inherited or arbitrary cwd), and replace the plain spawn/switch notice
        // with the "new server session" notice. We do NOT suppress: the move is
        // prepended to the pipeline (one pipe call, no extra AI round-trip), and
        // a reclaim's prior cwd is surfaced as a restore hint. Mid-session
        // re-attaches (TryMarkFirstAttach == false) keep the existing notices.
        if (readyPipeName != null && sessionManager.TryMarkFirstAttach(agentId))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string? priorCwd = null;
            if (!string.IsNullOrEmpty(liveCwd))
            {
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var normLive = Path.TrimEndingDirectorySeparator(Path.GetFullPath(liveCwd));
                var normHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
                if (!string.Equals(normLive, normHome, comparison)) priorCwd = liveCwd;
            }
            pipeline = $"Set-Location -LiteralPath '{home.Replace("'", "''")}'; {pipeline}";
            liveCwd = home;
            startupNotice = BuildNewSessionNotice(GetConsoleName(readyPipeName), home, priorCwd);
        }

        // Cwd drift detection: if the user typed `cd` in the visible console
        // since the AI's last successful execute_command, the live cwd has
        // moved off AI's intended cwd. Bail out without executing — the AI's
        // pipeline was built assuming cwd=AiCwd, silently running at LiveCwd
        // could trigger destructive ops at the wrong place (e.g. Remove-Item
        // *.tmp at C:\Windows\System32). Update LastAiCwd to LiveCwd so the
        // user-cd state is cleared: a re-issue of the same pipeline runs at
        // LiveCwd with no drift, and the AI keeps full agency to either
        // accept the new cwd or prepend Set-Location to revert.
        // DetectCwdDrift returns null when there's no LastAiCwd recorded
        // (e.g. fresh console / auto-start), so those paths naturally fall
        // through to execute.
        var drift = DetectCwdDrift(readyPipeName, liveCwd);
        if (drift.HasValue)
        {
            var driftPid = ConsoleSessionManager.GetPidFromPipeName(readyPipeName);
            if (driftPid.HasValue)
                sessionManager.SetLastAiCwd(agentId, driftPid.Value, drift.Value.LiveCwd);

            var driftConsoleName = GetConsoleName(readyPipeName);
            return Wrap(PipelineHelper.JoinBlocks(
                string.Join("\n", closedConsoleMessages),
                startupNotice,
                $"ℹ️ cwd in console {driftConsoleName} changed from '{drift.Value.AiCwd}' to '{drift.Value.LiveCwd}' outside the AI's commands (e.g. a `cd` typed in the console).\n"
                    + $"Pipeline NOT executed. Re-issue to run at '{drift.Value.LiveCwd}', or prepend `Set-Location -LiteralPath '{drift.Value.AiCwd.Replace("'", "''")}';` to revert."));
        }

        // Running a command in a console contradicts the intent to abandon it,
        // so a pending close confirmation for it is dropped: the next
        // close_console starts over with a fresh human-presence check rather
        // than killing on a single call.
        // Every path above either resolved a pipe or returned; this makes
        // that invariant explicit for the rest of the method.
        if (readyPipeName == null)
            return Wrap("Failed to acquire a console pipe.");

        CloseConsoleGuard.Disarm(readyPipeName);

        // Execute the command
        try
        {
            // Charge the console work already done against the ceiling, so a
            // cold spawn cannot push the response past the client's timeout.
            timeout_seconds = TimeoutCeiling.RemainingFor(timeout_seconds, callClock.Elapsed);
            var result = await powerShellService.ExecuteCommandToPipeAsync(readyPipeName, pipeline, parsedVariables, timeout_seconds, cancellationToken);
            // Parse response: header JSON (first line) + "\n\n" + body
            var separatorIndex = result.IndexOf("\n\n");
            var jsonHeader = separatorIndex >= 0 ? result.Substring(0, separatorIndex) : result;
            var body = separatorIndex >= 0 ? result.Substring(separatorIndex + 2) : "";

            if (jsonHeader.StartsWith('{'))
            {
                try
                {
                    var jsonResponse = JsonSerializer.Deserialize(jsonHeader, GetStatusResponseContext.Default.GetStatusResponse);
                    if (jsonResponse != null)
                    {
                        switch (jsonResponse.Status)
                        {
                            case PipeStatus.Busy:
                                // Mark this pipe as busy for tracking
                                if (jsonResponse.Pid > 0) sessionManager.MarkPipeBusy(agentId, jsonResponse.Pid);

                                if (jsonResponse.Reason == "user_command" || jsonResponse.Reason == "mcp_command")
                                {
                                    // Auto-route: spawn a new console at AI's intended cwd and
                                    // re-run the pipeline there in the same tool call. Pre-fix
                                    // we returned a "Pipeline NOT executed - verify location
                                    // and re-execute" message and the AI had to re-send the
                                    // exact same call, with the new console at HOME (= losing
                                    // the cwd the user/AI had been working in).
                                    //
                                    // Preference order for the spawn location:
                                    //   1. LastAiCwd[busyPid] — where the AI thinks it left
                                    //      the source console. The user may have typed `cd`
                                    //      interactively in the busy console, but the AI's
                                    //      pipeline is built on AI's mental model, not the
                                    //      user's interactive state.
                                    //   2. jsonResponse.Cwd — the busy console's live cwd,
                                    //      used when AI has no recorded intent yet (first
                                    //      tool call after spawning, busy race).
                                    //   3. UserProfile — last-resort fallback if cwd is
                                    //      unreachable (deleted folder, disconnected drive).
                                    var lastAiCwd = jsonResponse.Pid > 0
                                        ? sessionManager.GetLastAiCwd(jsonResponse.Pid)
                                        : null;
                                    var startLoc = (!string.IsNullOrEmpty(lastAiCwd) && Directory.Exists(lastAiCwd))
                                        ? lastAiCwd
                                        : (!string.IsNullOrEmpty(jsonResponse.Cwd) && Directory.Exists(jsonResponse.Cwd))
                                            ? jsonResponse.Cwd
                                            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                                    Console.Error.WriteLine($"[INFO] Runspace busy ({jsonResponse.Reason}), auto-routing to new console at {startLoc}...");
                                    var (startSuccess, startError) = await StartConsoleInternal(powerShellService, agentId, null, startLoc, cancellationToken);
                                    if (!startSuccess)
                                    {
                                        return Wrap(startError);
                                    }

                                    // Set console window title
                                    var activeAfterBusy = sessionManager.GetActivePipeName(agentId);
                                    if (activeAfterBusy != null)
                                    {
                                        await SetConsoleTitleAsync(powerShellService, activeAfterBusy, cancellationToken);
                                    }

                                    var newConsoleName = GetConsoleName(activeAfterBusy);

                                    // Recurse: the new console is now the active standby,
                                    // so FindReadyPipeAsync on the recursive call sees it
                                    // as ready and the pipeline runs cleanly. Recursion
                                    // depth is bounded in practice — StartConsoleInternal
                                    // just spawned a fresh console that hasn't been handed
                                    // out as a user-input target yet, so a second
                                    // back-to-back busy would require a sub-millisecond
                                    // race that's effectively impossible at AI tool-call
                                    // cadence. If it ever happens, the recursion would
                                    // spawn one more console and run there; no infinite
                                    // loop, just one extra console.
                                    // Same clock: the spawn we just paid for
                                    // comes out of the caller's budget too.
                                    var retryResult = await ExecuteCommandCore(
                                        powerShellService, pipeDiscoveryService, pipeline,
                                        timeout_seconds, var1, var2, var3, var4,
                                        agentId, is_subagent: false,
                                        agentIdBox: null,
                                        callClock: callClock,
                                        cancellationToken: cancellationToken);

                                    // Surface the auto-route notice + closed-console
                                    // messages collected by THIS outer call (the recursive
                                    // call's FindReadyPipeAsync runs fresh and won't
                                    // re-report them). Drop the previous bulky
                                    // get_current_location JSON (system info / drives) — the
                                    // recursive call delivers the actual pipeline output and
                                    // the AI no longer needs the OS / drive-list block to
                                    // verify cwd because we already preserved it.
                                    return Wrap(PipelineHelper.JoinBlocks(
                                        string.Join("\n", closedConsoleMessages),
                                        // The source console the pipeline did NOT run on.
                                        PipelineHelper.MarkOtherConsole(FormatBusyStatus(jsonResponse)),
                                        $"ℹ️ Auto-routed to {newConsoleName} at {startLoc} (source console busy with {jsonResponse.Reason}). Pipeline executed automatically — no re-send needed.",
                                        retryResult));
                                }
                                break;

                            case "timeout":
                                // Mark this pipe as busy for tracking
                                if (jsonResponse.Pid > 0) sessionManager.MarkPipeBusy(agentId, jsonResponse.Pid);
                                // Snapshot AI-intended cwd so a later switch / busy-route
                                // can resume there. The timeout cwd is mid-execution but
                                // is still the place AI's pipeline was operating from.
                                if (jsonResponse.Pid > 0 && !string.IsNullOrEmpty(jsonResponse.Cwd))
                                    sessionManager.SetLastAiCwd(agentId, jsonResponse.Pid, jsonResponse.Cwd);

                                // Consume cached output from current pipe (if any)
                                var currentPipeCachedOutput = await powerShellService.ConsumeOutputFromPipeAsync(readyPipeName, cancellationToken);

                                // Collect completed outputs and busy status from other pipes
                                var (timeoutCompletedOutput, timeoutBusyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

                                // Build timeout response: busy status first + closedConsoleInfo + cachedOutput + completedOutput + scopeWarning + timeout message
                                var timeoutStatusLine = !string.IsNullOrEmpty(jsonResponse.StatusLine)
                                    ? jsonResponse.StatusLine
                                    : $"⧗ Pipeline is running | {ConsoleSessionManager.Instance.GetConsoleDisplayName(jsonResponse.Pid)} | Status: Busy | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
                                // A stalled child is a diagnosis, not a guess, so it
                                // leads — it tells the AI both that waiting is futile
                                // and exactly how to re-run. The generic "it might be
                                // stuck" advice follows for every other case. Scope
                                // warning at the end (after instruction for better
                                // readability).
                                return Wrap(PipelineHelper.JoinBlocks(
                                    timeoutBusyStatusInfo,
                                    startupNotice,
                                    currentPipeCachedOutput,
                                    timeoutCompletedOutput,
                                    timeoutStatusLine,
                                    jsonResponse.StalledChild,
                                    "Use wait_for_completion to wait for the result.\n"
                                        + $"If it is instead STUCK — a native CLI (git/npm/ssh) waiting on stdin, or a runaway command — use cancel to interrupt it (Ctrl+C; works on native/running commands but not on PowerShell host prompts), or close_console {jsonResponse.Pid} to abandon the console.",
                                    scopeWarning));

                            case PipeStatus.AwaitingInput:
                                // Command is parked at an interactive prompt. It will
                                // NOT finish on its own and a PowerShell prompt cannot be
                                // cancelled programmatically, so steer the AI to the only
                                // two real moves: let the user answer it, or abandon the
                                // console. (Marked busy for tracking; cwd snapshotted so a
                                // later auto-route can resume where the AI was working.)
                                if (jsonResponse.Pid > 0) sessionManager.MarkPipeBusy(agentId, jsonResponse.Pid);
                                if (jsonResponse.Pid > 0 && !string.IsNullOrEmpty(jsonResponse.Cwd))
                                    sessionManager.SetLastAiCwd(agentId, jsonResponse.Pid, jsonResponse.Cwd);

                                var awaitingConsole = ConsoleSessionManager.Instance.GetConsoleDisplayName(jsonResponse.Pid);
                                var awaitingPromptInfo = string.IsNullOrEmpty(jsonResponse.Message) ? "" : $" — {jsonResponse.Message}";

                                return Wrap(PipelineHelper.JoinBlocks(
                                    string.Join("\n", closedConsoleMessages),
                                    startupNotice,
                                    jsonResponse.StatusLine,
                                    $"⌨ Console {awaitingConsole} is paused at a PowerShell HOST prompt{awaitingPromptInfo} — issued by a cmdlet/function (Read-Host, a missing mandatory parameter, Get-Credential, a confirmation). It will NOT finish on its own. NOTE: `cancel` does NOT work on a PowerShell host prompt (only on native CLIs / runaway commands), so do not call it here. Choose one:\n"
                                        + $"  • Ask the user to type the answer directly in console {awaitingConsole}.\n"
                                        + $"  • Abandon it with close_console {jsonResponse.Pid} (the next command auto-starts a fresh console).",
                                    scopeWarning));

                            case PipeStatus.Completed:
                                // Snapshot AI-intended cwd from the cached completion.
                                if (jsonResponse.Pid > 0 && !string.IsNullOrEmpty(jsonResponse.Cwd))
                                    sessionManager.SetLastAiCwd(agentId, jsonResponse.Pid, jsonResponse.Cwd);

                                // Drain the current pipe's cache inline so the client sees
                                // the result on THIS tool call instead of having to make a
                                // follow-up call. The DLL's shouldCache branch fires when a
                                // new pipe request arrived while this invoke's command was
                                // still running — MarkForCaching gets set, NotifyResultReady
                                // sees the flag and routes the result to the console's local
                                // cache, and the DLL returns a metadata-only "completed"
                                // response assuming the original caller was no longer
                                // listening. In practice the original ExecuteCommand
                                // handler IS still running (this switch case is inside it)
                                // and can still deliver the response through the pipe that
                                // is currently being serviced. Same mechanism the "timeout"
                                // case above already uses. Without this, the client
                                // had to issue a second tool call just to see an already-
                                // completed result — observable as a "Result cached. Will
                                // be returned on next tool call." placeholder in place of
                                // the actual output.
                                var currentPipeCachedCompleted = await powerShellService.ConsumeOutputFromPipeAsync(readyPipeName, cancellationToken);

                                // Collect busy status + other consoles' cached outputs
                                var (cachedCompletedOutput, cachedBusyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

                                // Status line first (same shape as success / timeout cases).
                                var cachedStatusLine = !string.IsNullOrEmpty(jsonResponse.StatusLine)
                                    ? jsonResponse.StatusLine
                                    : $"✓ Pipeline executed successfully | {ConsoleSessionManager.Instance.GetConsoleDisplayName(jsonResponse.Pid)} | Status: Completed | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
                                // Then the drained content. Defensive fallback for a race
                                // where another drainer got there first (e.g. a concurrent
                                // wait_for_completion call) — keep the old placeholder so
                                // the AI still knows the result is being delivered
                                // somewhere, rather than staring at an empty response.
                                return Wrap(PipelineHelper.JoinBlocks(
                                    cachedBusyStatusInfo,
                                    startupNotice,
                                    cachedCompletedOutput,
                                    cachedStatusLine,
                                    !string.IsNullOrEmpty(currentPipeCachedCompleted)
                                        ? currentPipeCachedCompleted
                                        : "Result cached. Will be returned on next tool call."));

                            case "error":
                                return Wrap(jsonResponse.Message ?? $"Error from PowerShell.MCP module: {jsonResponse.Error}");

                            case "success":
                                // Snapshot AI-intended cwd at successful completion. This
                                // is the post-execution cwd — where AI's pipeline left the
                                // shell — so the next execute_command's drift check
                                // compares against the place AI actually finished at.
                                if (jsonResponse.Pid > 0 && !string.IsNullOrEmpty(jsonResponse.Cwd))
                                    sessionManager.SetLastAiCwd(agentId, jsonResponse.Pid, jsonResponse.Cwd);

                                // Normal completion - use body as result
                                var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

                                // Redundant leading cd hint: liveCwd is the
                                // pre-execution cwd (drift check guarantees it
                                // matches the AI's last cwd), jsonResponse.Cwd
                                // the post-execution one.
                                var redundantCdHint = PipelineHelper.CheckRedundantLeadingCd(aiPipeline, liveCwd, jsonResponse.Cwd);
                                // TODO: Uncomment when JsonDuo is published to PS Gallery
                                // var jsonHint = PipelineHelper.CheckJsonFileHint(pipeline, agentId)
                                //     ?? PipelineHelper.CheckJsonFileHint(body, agentId);
                                // (then pass jsonHint as the last block below)

                                // body is this console's status line + its output.
                                // Scope warning and hints go at the end (after
                                // output for better readability). allPipesStatusInfo
                                // is deliberately left out of every response (only
                                // logged to stderr at auto-start): it is the discovery-
                                // time snapshot of the same consoles busyStatusInfo
                                // reports fresh, under raw pipe names.
                                return Wrap(PipelineHelper.JoinBlocks(
                                    string.Join("\n", closedConsoleMessages),
                                    busyStatusInfo,
                                    startupNotice,
                                    completedOutput,
                                    body,
                                    scopeWarning,
                                    redundantCdHint));
                        }
                    }
                }
                catch
                {
                    // Not valid JSON or parsing failed, return as-is
                }
            }

            // Fallback: return result as-is (shouldn't happen with new DLL)
            return Wrap(result);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] ExecuteCommand failed: {ex.Message}");
            // Detect and report closed consoles (the failed pipe + any others)
            var consoleName = sessionManager.GetConsoleDisplayName(readyPipeName);
            sessionManager.ClearDeadPipe(agentId, readyPipeName);
            var otherClosed = pipeDiscoveryService.DetectClosedConsoles(agentId);
            var closedMessages = new List<string> { $"  - ⚠ Console {consoleName} was closed" };
            closedMessages.AddRange(otherClosed);
            return Wrap($"Command execution failed: {ex.Message}\n{string.Join("\n", closedMessages)}\nPlease try again. A new console will be started automatically if needed.");
        }
    }

    [McpServerTool]
    [Description("Wait for busy console(s) to complete and retrieve cached results. Use this after receiving a 'Pipeline is running' response instead of executing Start-Sleep (which would open a new console).")]
    public static async Task<string> WaitForCompletion(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        McpServer mcpServer,
        [Description("Maximum seconds to wait for completion (default: 30). Automatically capped to what the connected MCP client is known to wait for. Returns early if a console completes.")]
        int timeout_seconds = 30,
        [Description("Agent ID for sub-agent console isolation. Obtain this by calling start_console with is_subagent=true. Do not pass arbitrary strings.")]
        string? agent_id = null,
        [Description("Set to true if you are a sub-agent. A unique agent_id will be allocated and returned in the response. Use that agent_id for all subsequent tool calls.")]
        bool is_subagent = false,
        CancellationToken cancellationToken = default)
    {
        TimeoutCeiling.ObserveClient(mcpServer?.ClientInfo?.Name);

        // Same delivery guard as execute_command: this tool's whole job is to
        // drain caches, so a response the client no longer listens for takes
        // the drained output down with it.
        var response = await WaitForCompletionCore(
            powerShellService, pipeDiscoveryService, timeout_seconds, agent_id, is_subagent, cancellationToken);
        return await StashIfCancelledAsync(
            powerShellService,
            string.IsNullOrEmpty(agent_id) ? "default" : agent_id,
            response,
            cancellationToken);
    }

    private static async Task<string> WaitForCompletionCore(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        int timeout_seconds,
        string? agent_id,
        bool is_subagent,
        CancellationToken cancellationToken)
    {
        var sessionManager = ConsoleSessionManager.Instance;

        // wait_for_completion requires an existing agent_id for sub-agents
        if (is_subagent && string.IsNullOrEmpty(agent_id))
            return "❌ Sub-agents must obtain an agent_id by calling start_console, get_current_location, or execute_command with is_subagent=true before calling wait_for_completion.";

        var agentId = string.IsNullOrEmpty(agent_id) ? "default" : agent_id;

        // Validate agent_id
        if (!ConsoleSessionManager.Instance.IsValidAgentId(agentId))
            return $"❌ Invalid agent_id '{agentId}'. Sub-agents must first call start_console with is_subagent=true to obtain a valid agent_id. Do not pass arbitrary strings as agent_id.";

        timeout_seconds = TimeoutCeiling.Clamp(timeout_seconds, 1);

        const int pollIntervalMs = 1000;
        var endTime = DateTime.UtcNow.AddSeconds(timeout_seconds);

        // Detect externally closed consoles - return immediately if any previously busy pipe is gone
        var closedConsoleMessages = new List<string>();
        var previouslyBusyPids = sessionManager.ConsumeKnownBusyPids(agentId);
        var currentPipes = sessionManager.EnumeratePipes(sessionManager.ProxyPid, agentId).ToList();
        var currentPids = currentPipes
            .Select(ConsoleSessionManager.GetPidFromPipeName)
            .OfType<int>()
            .ToHashSet();

        foreach (var pid in previouslyBusyPids)
        {
            if (!currentPids.Contains(pid))
            {
                closedConsoleMessages.Add($"⚠ Console {ConsoleSessionManager.Instance.GetConsoleDisplayName(pid)} was closed");
            }
        }

        // If any previously busy pipe was closed, collect all cached outputs and return
        if (closedConsoleMessages.Count > 0)
        {
            var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, null, cancellationToken);
            return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
        }


        // First pass: identify busy pipes and check for completed/dead
        var busyPipes = new List<string>();

        foreach (var pipeName in currentPipes)
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                // Dead pipe detected - get display name before clearing
                var consoleName = ConsoleSessionManager.Instance.GetConsoleDisplayName(pipeName);
                sessionManager.ClearDeadPipe(agentId, pipeName);
                closedConsoleMessages.Add($"⚠ Console {consoleName} was closed");

                var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, null, cancellationToken);
                return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
            }

            if (status.Status == PipeStatus.Completed)
            {
                // Completed - collect all cached outputs (including this one) and return
                var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, null, cancellationToken);
                return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
            }

            // IsBusy covers AwaitingInput (a command parked at a host prompt)
            // as well as Busy, so wait_for_completion polls it until the human
            // answers (→ Completed, drained) or the timeout elapses — the same
            // contract as a normal timed-out command. Without this an
            // awaiting-input console matched no branch, was never added to
            // busyPipes, and wait_for_completion returned "No commands to wait
            // for" at once.
            if (PipeStatus.IsBusy(status.Status))
            {
                if (status.Pid > 0) sessionManager.MarkPipeBusy(agentId, status.Pid);
                // Only track MCP-initiated commands, not user commands
                if (status.Pipeline != "(user command)")
                {
                    busyPipes.Add(pipeName);
                }
            }
            else if (status.Status == PipeStatus.Standby)
            {
                if (status.Pid > 0) sessionManager.UnmarkPipeBusy(agentId, status.Pid);
            }
        }

        // No MCP-initiated busy pipes - collect any cached outputs and return
        if (busyPipes.Count == 0)
        {
            var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, null, cancellationToken);

            // Nothing running, nothing cached, nothing closed.
            if (completedOutput.Length == 0 && busyStatusInfo.Length == 0 && closedConsoleMessages.Count == 0)
            {
                return NothingToWaitForMessage;
            }

            return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
        }


        // Poll only the busy pipes until timeout or completion/dead
        while (DateTime.UtcNow < endTime)
        {
            // Wait before next poll
            var remainingMs = (int)(endTime - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0) break;
            await Task.Delay(Math.Min(pollIntervalMs, remainingMs), cancellationToken);

            foreach (var pipeName in busyPipes.ToList())
            {
                var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

                if (status == null)
                {
                    // Dead pipe detected - get display name before clearing
                    var consoleName = ConsoleSessionManager.Instance.GetConsoleDisplayName(pipeName);
                    sessionManager.ClearDeadPipe(agentId, pipeName);
                    busyPipes.Remove(pipeName);
                    closedConsoleMessages.Add($"⚠ Console {consoleName} was closed");

                    var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, null, cancellationToken);
                    return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
                }

                if (status.Status == PipeStatus.Completed)
                {
                    // Completed - collect all cached outputs (including this one) and return
                    var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, null, cancellationToken);
                    return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
                }


                if (status.Status == PipeStatus.Standby)
                {
                    if (status.Pid > 0) sessionManager.UnmarkPipeBusy(agentId, status.Pid);
                    // Console returned to standby without caching (unexpected)
                    busyPipes.Remove(pipeName);
                }
            }

            // All busy pipes became standby or dead (unexpected)
            if (busyPipes.Count == 0)
            {
                break;
            }
        }

        // Timeout - collect final status
        var (finalCompletedOutput, finalBusyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, null, cancellationToken);
        return BuildWaitResponse(closedConsoleMessages, finalCompletedOutput, finalBusyStatusInfo);
    }

    // wait_for_completion's answer when nothing is running and nothing is cached.
    // The bare "No commands to wait for completion." read as if a command still
    // in progress had been lost, typically right after the MCP client moved a long
    // call to the background. That call is still in flight in this proxy and
    // receives its own result, so there is genuinely nothing to collect here.
    internal const string NothingToWaitForMessage =
        "No commands to wait for completion. Nothing is running in this session's consoles and no results are waiting to be collected. " +
        "If your MCP client moved an earlier call to the background, that call receives the result itself when the command finishes.";

    private static string BuildWaitResponse(List<string> closedConsoleMessages, string completedOutput, string busyStatusInfo)
    {
        var response = PipelineHelper.JoinBlocks(
            busyStatusInfo,
            string.Join("\n", closedConsoleMessages),
            completedOutput,
            busyStatusInfo.Length > 0 ? "Use wait_for_completion tool to wait and retrieve the result." : null);

        return response.Length == 0 ? NothingToWaitForMessage : response;
    }

    [McpServerTool]
    [Description(@"Ensure a PowerShell console is available, or launch a new one. When reason is empty or omitted, reuses an existing standby console if one is available. When reason is provided, always launches a new console regardless of existing ones.

📌 Calling this first is optional — execute_command auto-starts or reuses a console as needed. Call it explicitly to pre-warm a console at a specific start_location, to show a startup banner, or (with reason) to force an additional console.")]
    public static async Task<string> StartConsole(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        [Description("Do NOT specify unless you need a separate console. Forces a new console launch. Omit to reuse an existing standby console (preferred).")]
        string? reason = null,
        [Description("Message displayed at console startup (e.g. greeting, joke, fun fact). Be creative and make the user smile!")]
        string? banner = null,
        [Description("Optional starting directory path. If relative, resolved from home directory. Defaults to home directory if not specified.")]
        string? start_location = null,
        [Description("Agent ID for sub-agent console isolation. Obtain this by calling start_console with is_subagent=true. Do not pass arbitrary strings.")]
        string? agent_id = null,
        [Description("Set to true if you are a sub-agent. A unique agent_id will be allocated and returned in the response. Use that agent_id for all subsequent tool calls.")]
        bool is_subagent = false,
        CancellationToken cancellationToken = default)
    {
        var (agentId, isNewlyAllocated, resolveError) = ResolveAgentId(is_subagent, agent_id);
        // Wrap every return so the 🔑 notice can never be dropped on a sub-agent's
        // first call regardless of which branch builds the response.
        string Wrap(string r) => PrependAgentIdNoticeIfNew(r, isNewlyAllocated, agentId);
        if (resolveError != null)
            return Wrap(resolveError);

        var forceNew = !string.IsNullOrEmpty(reason);

        // Reuse first to keep the desktop from filling up with console windows:
        // when no reason is given we reclaim ANY available console — an already
        // owned standby OR an unowned one (a prior session's released console or
        // a user-started one) — and only spawn a fresh one when nothing is
        // available. The cwd a reclaimed console happens to sit at no longer
        // needs an opt-in (the old start_location gate): the first-attach
        // treatment below normalizes it (move to $HOME for execution) and the
        // "new server session" notice tells the AI its prior context may differ.
        var includeUnowned = true;

        // When no reason is given, try to reuse an existing standby console
        if (!forceNew)
        {
            var discoveryResult = await pipeDiscoveryService.FindReadyPipeAsync(agentId, cancellationToken, includeUnowned);
            if (discoveryResult.ReadyPipeName != null)
            {
                // Set console window title if this was a newly claimed (unowned) console
                if (discoveryResult.ConsoleSwitched)
                {
                    await SetConsoleTitleAsync(powerShellService, discoveryResult.ReadyPipeName, cancellationToken);
                    // Newly claimed: show the caller's banner, or the default
                    // "AI session connected." notice when none was given.
                    await ShowClaimNoticeAsync(powerShellService, discoveryResult.ReadyPipeName, banner, cancellationToken);
                }
                else if (!string.IsNullOrEmpty(banner))
                {
                    // Reused an already-owned standby console with an explicit
                    // banner — still surface the AI's banner (a greeting / joke),
                    // but no generic "connected" notice since nothing was claimed.
                    await ShowClaimNoticeAsync(powerShellService, discoveryResult.ReadyPipeName, banner, cancellationToken);
                }

                var reuseLocationResult = await powerShellService.GetCurrentLocationFromPipeAsync(discoveryResult.ReadyPipeName, cancellationToken);

                // First attach for this agent (resume / cold-start). Mark it so a
                // following execute_command treats this console as owned-active
                // (no second $HOME normalization). Like get_current_location,
                // start_console is non-executing, so we preserve the console's cwd
                // and only surface the "new server session" notice when we reclaimed
                // an existing (unowned) console that may carry a prior session's state.
                string? reuseNewSessionNotice = null;
                if (ConsoleSessionManager.Instance.TryMarkFirstAttach(agentId) && discoveryResult.ConsoleSwitched)
                {
                    reuseNewSessionNotice = BuildNewSessionNotice(GetConsoleName(discoveryResult.ReadyPipeName), discoveryResult.LiveCwd ?? "the console's current location", null);
                }

                // Always collect cached outputs - any console may have completed work
                var (reuseCompletedOutput, reuseBusyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, discoveryResult.ReadyPipeName, cancellationToken);
                return Wrap(PipelineHelper.JoinBlocks(
                    reuseNewSessionNotice,
                    // Closed consoles detected during discovery
                    string.Join("\n", discoveryResult.ClosedConsoleMessages),
                    reuseBusyStatusInfo,
                    reuseCompletedOutput,
                    "ℹ️ Did not launch a new console. An existing standby console is available and will be reused. To force a new console, provide the reason parameter.",
                    reuseLocationResult));
            }
            // No standby console found, fall through to create a new one
        }

        var (resolvedPath, warningMessage) = ResolveStartLocation(start_location);
        var startupCommands = BuildStartupCommands(banner, reason);
        var (success, startResult) = await StartConsoleInternal(powerShellService, agentId, startupCommands, resolvedPath, cancellationToken);
        if (!success)
        {
            return Wrap(startResult); // Error message
        }

        // Mark first attach so a following execute_command sees an owned-active
        // console and does not re-normalize to $HOME. No "new server session"
        // notice here: a freshly spawned console is clean (nothing carried over)
        // and starts at the resolved start_location / $HOME the AI asked for.
        ConsoleSessionManager.Instance.TryMarkFirstAttach(agentId);

        // Set console window title
        var newPipeName = ConsoleSessionManager.Instance.GetActivePipeName(agentId);
        if (newPipeName != null)
        {
            await SetConsoleTitleAsync(powerShellService, newPipeName, cancellationToken);
        }

        // Collect busy status from Proxy side
        var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, newPipeName, cancellationToken);

        // Build response: busy status first + completed output + start message + location
        return Wrap(PipelineHelper.JoinBlocks(
            busyStatusInfo,
            completedOutput,
            warningMessage,
            "PowerShell console started successfully with PowerShell.MCP module imported.",
            startResult));
    }

    /// <summary>
    /// Builds the "new server session" notice shown on the proxy's FIRST
    /// console attach for an agent (the resume / cold-start boundary). The proxy
    /// restarted (or just started), so its runtime state is fresh, but the AI may
    /// still carry intent from earlier in the conversation — variables, modules,
    /// and cwd it set before are NOT guaranteed on this console. <paramref
    /// name="currentCwd"/> is where the console now sits (for execute_command we
    /// move it to $HOME; get_current_location reports it as-is). When the console
    /// was reclaimed from a prior session/user and we moved away from its cwd,
    /// <paramref name="priorCwdToRestore"/> carries that cwd as a one-line restore
    /// hint (null when nothing was displaced, e.g. a fresh spawn or a preserved cwd).
    /// </summary>
    internal static string BuildNewSessionNotice(string consoleName, string currentCwd, string? priorCwdToRestore)
    {
        var sb = new StringBuilder();
        sb.Append($"🔄 New server session — attached to console {consoleName}, now at {currentCwd}. ");
        sb.Append("Variables, modules, functions, and cwd from earlier in this conversation are not carried over to this console.");
        if (!string.IsNullOrEmpty(priorCwdToRestore))
        {
            sb.Append($" The console was at '{priorCwdToRestore}' — run Set-Location -LiteralPath '{priorCwdToRestore.Replace("'", "''")}' to resume there.");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Builds PowerShell commands to display the startup notice on a newly
    /// spawned console. A spawned console is a new AI attach, so it always
    /// shows a green line — the caller's banner when given, otherwise the
    /// default "AI session connected." (mirroring the claim path's
    /// ShowClaimNoticeAsync, so a custom banner replaces rather than doubles
    /// the generic notice). The optional reason is appended in dark yellow.
    /// </summary>
    internal static string BuildStartupCommands(string? banner, string? reason)
    {
        var parts = new List<string>();

        // Green lifecycle line — banner if supplied, else the default notice.
        var greenLine = string.IsNullOrEmpty(banner) ? "AI session connected." : banner;
        parts.Add($"Write-Host '{greenLine.Replace("'", "''")}' -ForegroundColor Green");

        if (!string.IsNullOrEmpty(reason))
        {
            parts.Add("Write-Host ''");  // blank line between notice and reason
            parts.Add($"Write-Host 'Reason: {reason.Replace("'", "''")}' -ForegroundColor DarkYellow");
        }
        parts.Add("Write-Host ''");  // blank line before prompt
        return string.Join("; ", parts);
    }

    /// <summary>
    /// Internal method to start PowerShell console.
    /// Returns (success, result) where result is locationResult on success, or error message on failure.
    /// </summary>
    private static async Task<(bool success, string result)> StartConsoleInternal(
        IPowerShellService powerShellService,
        string agentId,
        string? startupCommands,
        string startLocation,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionManager = ConsoleSessionManager.Instance;

            Console.Error.WriteLine("[INFO] Starting PowerShell console...");
            // Start new console
            var (success, pipeName) = await PowerShellProcessManager.StartPowerShellWithModuleAndPipeNameAsync(agentId, startupCommands, startLocation);

            if (!success)
            {
                return (false, "Failed to start PowerShell console or establish Named Pipe connection.\n\nPossible causes:\n- No supported terminal emulator found (gnome-terminal, konsole, xfce4-terminal, xterm, etc.)\n- Terminal emulator failed to start\n- PowerShell.MCP module failed to initialize\n\nPlease ensure a terminal emulator is installed and try again.");
            }

            // Register the new console
            sessionManager.SetActivePipeName(agentId, pipeName);

            Console.Error.WriteLine($"[INFO] PowerShell console started successfully (pipe={pipeName}), setting title and getting current location...");

            // Set console title before getting location (so title appears in status line)
            await SetConsoleTitleAsync(powerShellService, pipeName, cancellationToken);

            // Get current location from new console
            var locationResult = await powerShellService.GetCurrentLocationFromPipeAsync(pipeName, cancellationToken);

            Console.Error.WriteLine("[INFO] PowerShell console startup completed");

            return (true, locationResult);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] StartConsole failed: {ex.Message}");
            return (false, $"Failed to start PowerShell console: {ex.Message}\n\nPlease check if a terminal emulator is available and try again.");
        }
    }
    /// <summary>
    /// Resolves start location path and validates it.
    /// Returns (resolvedPath, warningMessage).
    /// If directory doesn't exist, returns home directory and a warning message.
    /// </summary>
    private static (string resolvedPath, string? warningMessage) ResolveStartLocation(string? startLocation)
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrEmpty(startLocation))
        {
            return (homeDir, null);
        }

        string resolvedPath;
        if (Path.IsPathRooted(startLocation))
        {
            // Absolute path
            resolvedPath = startLocation;
        }
        else
        {
            // Relative path - resolve from home directory
            resolvedPath = Path.Combine(homeDir, startLocation);
        }

        if (!Directory.Exists(resolvedPath))
        {
            var warning = $"⚠️ Warning: Specified start_location does not exist: {resolvedPath}\nConsole started in default location (home directory) instead.\n";
            return (homeDir, warning);
        }

        return (resolvedPath, null);
    }


    /// <summary>
    /// Checks for local variable assignments without scope prefix and
    /// returns a warning message. The warning is deduped per-agent and
    /// per-variable-name (see <see cref="PipelineHelper.CheckLocalVariableAssignments"/>);
    /// threading the current agentId through is what makes the dedup
    /// work across calls in the same conversation.
    /// </summary>
    private static string? CheckLocalVariableAssignments(string pipeline, string agentId)
        => PipelineHelper.CheckLocalVariableAssignments(pipeline, agentId);
}
