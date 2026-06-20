using ModelContextProtocol.Server;
using System.ComponentModel;
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
        // Case-insensitive comparison on Windows; Path.GetFullPath normalizes
        // separators and trailing slashes so "C:\Project" and "C:\Project\"
        // don't trigger a spurious cd.
        var normalizedAi = Path.GetFullPath(aiCwd);
        var normalizedLive = Path.GetFullPath(liveCwd);
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
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns current_location and other_drive_locations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.")]
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
        var (readyPipeName, consoleSwitched, closedConsoleMessages, allPipesStatusInfo, _) = await FindReadyPipeAsync(pipeDiscoveryService, agentId, cancellationToken);

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

            // Get location (DLL will include its own cached outputs automatically)
            var result = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);

            // Collect completed outputs and busy status info from other pipes
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

            // Build response: closedConsoles + busyStatusInfo + completedOutputs + agentId info + result
            var response = new StringBuilder();
            if (closedConsoleMessages.Count > 0)
            {
                response.AppendLine(string.Join("\n", closedConsoleMessages));
                response.AppendLine();
            }
            if (busyStatusInfo.Length > 0)
            {
                response.Append(busyStatusInfo);
                response.AppendLine();
            }
            if (completedOutputs.Length > 0)
            {
                response.Append(completedOutputs);
            }
            response.Append(result);
            return Wrap(response.ToString());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] GetCurrentLocation failed: {ex.Message}");
            return Wrap($"Failed to get current location: {ex.Message}\n\nPlease try again. A new console will be started automatically if needed.");
        }
    }

    [McpServerTool]
    [Description(@"Execute PowerShell cmdlets and CLI tools (e.g., git) in persistent console. Session persists: modules, variables, functions, authentication stay active—no re-authentication. Install any modules and learn them via Get-Help.

📌 This is your primary tool for all command execution tasks: directory navigation, git operations, build/test commands, file system operations, process management, environment variable access, and any shell/terminal task. Sessions persist across calls—variables, modules, functions, and authentication stay active. Install any PowerShell Gallery module to extend capabilities (e.g., Az for Azure, AWS.Tools for AWS, Microsoft.Graph for M365).

💡 API Exploration: Use Invoke-RestMethod to explore Web APIs and Add-Type for Win32 API testing. Verify API behavior before writing production code—get immediate feedback without compilation.

⚠️ CRITICAL - Variable Scope:
Local variables are NOT preserved between invoke_expression calls. Use $script: or $global: scope to share variables across calls.

⚠️ CRITICAL - String Interpolation:
Double-quoted strings expand variables and subexpressions: ""$var"" becomes the value of $var, ""$(expr)"" evaluates expr. Use single quotes for literal strings: '$var' keeps the text $var as-is.

📝 Text File Operations:
ALWAYS use the specialized cmdlets for text file editing: Show-TextFiles, Add-LinesToFile, Update-LinesInFile, Update-MatchInFile, Remove-LinesFromFile.
NEVER use Set-Content, [IO.File]::WriteAllText, or other alternatives—even when source code contains $ or backtick characters. Instead, pass content via var1-var4 parameters.
Create new file: Add-LinesToFile path -Content $var1 (with var1 parameter containing the content)
Edit existing file: Add-LinesToFile, Update-LinesInFile, Update-MatchInFile, Remove-LinesFromFile (use var1-var4 for content with $, backtick, or quotes)
For detailed examples: invoke_expression('Get-Help <cmdlet-name> -Examples')
Edit cmdlets show changed lines with 2 lines of context. Use Show-TextFiles after editing if you need the full file view.

📌 Prefer these cmdlets over other file read/edit/search tools provided by the host application. They handle special characters ($, backtick, double-quote) safely via var1-var4 parameters, and keep all operations in a single persistent session without context switching.

🤖 AI Cmdlets (requires PromptAI module: Install-Module PromptAI):
Invoke-Claude ""prompt"" — Call Anthropic Claude API. Invoke-GPT ""prompt"" — Call OpenAI API. Invoke-Gemini ""prompt"" — Call Google Gemini API. All support -Model and -SystemPrompt.

🔤 Variables Parameter:
Use var1/var2/var3/var4 parameters to inject literal string values into the pipeline, bypassing the PowerShell parser. Reference them as $var1/$var2/$var3/$var4 in the pipeline.
When editing source code files, ALWAYS use variables for -OldText, -Replacement, -Content parameters to avoid unintended expansion of $, backtick, or double-quote characters.")]
    public static async Task<string> InvokeExpression(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        [Description("The PowerShell command or pipeline to execute. Multi-line commands (if, loops, try-catch, etc.) are supported.")]
        string pipeline,
        [Description("Timeout in seconds (0-170, default: 170). On timeout, execution continues in background and result is cached for retrieval on next tool call. Use 0 for commands requiring user interaction (e.g., pause, Read-Host).")]
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
        // Clamp timeout to valid range
        timeout_seconds = Math.Clamp(timeout_seconds, 0, 170);

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
            startupNotice = $"ℹ️ Switched to console {GetConsoleName(readyPipeName)}. Pipeline running on the new console.";
        }

        // Check for local variable assignments without scope prefix.
        // Per-agent dedup: same var name never double-warns the same
        // conversation, so long sessions don't drown in reminders.
        var scopeWarning = CheckLocalVariableAssignments(pipeline, agentId);

        // Enforce var1/var2 usage for text editing cmdlets
        var var1Error = PipelineHelper.CheckVar1Enforcement(pipeline, var1, var2);
        if (var1Error != null)
        {
            return Wrap(var1Error);
        }

        // Cwd drift detection: if the user typed `cd` in the visible console
        // since the AI's last successful invoke_expression, the live cwd has
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
            var bailResponse = new StringBuilder();
            if (closedConsoleMessages.Count > 0)
            {
                bailResponse.AppendLine(string.Join("\n", closedConsoleMessages));
                bailResponse.AppendLine();
            }
            if (!string.IsNullOrEmpty(startupNotice))
            {
                bailResponse.AppendLine(startupNotice);
                bailResponse.AppendLine();
            }
            bailResponse.AppendLine($"ℹ️ cwd in console {driftConsoleName} changed from '{drift.Value.AiCwd}' to '{drift.Value.LiveCwd}' outside the AI's commands (e.g. a `cd` typed in the console).");
            bailResponse.AppendLine($"Pipeline NOT executed. Re-issue to run at '{drift.Value.LiveCwd}', or prepend `Set-Location -LiteralPath '{drift.Value.AiCwd.Replace("'", "''")}';` to revert.");
            return Wrap(bailResponse.ToString());
        }

        // Execute the command
        try
        {
            var result = await powerShellService.InvokeExpressionToPipeAsync(readyPipeName, pipeline, parsedVariables, timeout_seconds, cancellationToken);
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
                                    var retryResult = await InvokeExpression(
                                        powerShellService, pipeDiscoveryService, pipeline,
                                        timeout_seconds, var1, var2, var3, var4,
                                        agentId, is_subagent: false,
                                        cancellationToken: cancellationToken);

                                    // Surface the auto-route notice + closed-console
                                    // messages collected by THIS outer call (the recursive
                                    // call's FindReadyPipeAsync runs fresh and won't
                                    // re-report them). Drop the previous bulky
                                    // get_current_location JSON (system info / drives) — the
                                    // recursive call delivers the actual pipeline output and
                                    // the AI no longer needs the OS / drive-list block to
                                    // verify cwd because we already preserved it.
                                    var busyResponse = new StringBuilder();
                                    if (closedConsoleMessages.Count > 0)
                                    {
                                        busyResponse.AppendLine(string.Join("\n", closedConsoleMessages));
                                        busyResponse.AppendLine();
                                    }
                                    busyResponse.AppendLine(FormatBusyStatus(jsonResponse));
                                    busyResponse.AppendLine($"ℹ️ Auto-routed to {newConsoleName} at {startLoc} (source console busy with {jsonResponse.Reason}). Pipeline executed automatically — no re-send needed.");
                                    busyResponse.AppendLine();
                                    busyResponse.Append(retryResult);
                                    return Wrap(busyResponse.ToString());
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
                                var timeoutResponse = new StringBuilder();
                                if (timeoutBusyStatusInfo.Length > 0)
                                {
                                    timeoutResponse.Append(timeoutBusyStatusInfo);
                                }
                                if (!string.IsNullOrEmpty(allPipesStatusInfo))
                                {
                                    timeoutResponse.AppendLine(allPipesStatusInfo);
                                    timeoutResponse.AppendLine();
                                }
                                if (!string.IsNullOrEmpty(startupNotice))
                                {
                                    timeoutResponse.AppendLine(startupNotice);
                                    timeoutResponse.AppendLine();
                                }
                                if (!string.IsNullOrEmpty(currentPipeCachedOutput))
                                {
                                    timeoutResponse.AppendLine(currentPipeCachedOutput);
                                    timeoutResponse.AppendLine();
                                }
                                if (timeoutCompletedOutput.Length > 0)
                                {
                                    timeoutResponse.Append(timeoutCompletedOutput);
                                }
                                // Status line first
                                var timeoutStatusLine = !string.IsNullOrEmpty(jsonResponse.StatusLine)
                                    ? jsonResponse.StatusLine
                                    : $"⧗ Pipeline is still running | {ConsoleSessionManager.Instance.GetConsoleDisplayName(jsonResponse.Pid)} | Status: Busy | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
                                timeoutResponse.AppendLine(timeoutStatusLine);
                                timeoutResponse.AppendLine();
                                timeoutResponse.Append("Use wait_for_completion tool to wait and retrieve the result.");
                                // Scope warning at the end (after instruction for better readability)
                                if (!string.IsNullOrEmpty(scopeWarning))
                                {
                                    timeoutResponse.AppendLine();
                                    timeoutResponse.AppendLine(scopeWarning);
                                }
                                return Wrap(timeoutResponse.ToString());

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
                                // listening. In practice the original InvokeExpression
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

                                var cachedResponse = new StringBuilder();
                                if (cachedBusyStatusInfo.Length > 0)
                                {
                                    cachedResponse.Append(cachedBusyStatusInfo);
                                }
                                if (!string.IsNullOrEmpty(allPipesStatusInfo))
                                {
                                    cachedResponse.AppendLine(allPipesStatusInfo);
                                    cachedResponse.AppendLine();
                                }
                                if (!string.IsNullOrEmpty(startupNotice))
                                {
                                    cachedResponse.AppendLine(startupNotice);
                                    cachedResponse.AppendLine();
                                }
                                if (cachedCompletedOutput.Length > 0)
                                {
                                    cachedResponse.Append(cachedCompletedOutput);
                                }
                                // Status line first (same shape as success / timeout cases).
                                var cachedStatusLine = !string.IsNullOrEmpty(jsonResponse.StatusLine)
                                    ? jsonResponse.StatusLine
                                    : $"✓ Pipeline executed successfully | {ConsoleSessionManager.Instance.GetConsoleDisplayName(jsonResponse.Pid)} | Status: Completed | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
                                cachedResponse.AppendLine(cachedStatusLine);
                                cachedResponse.AppendLine();
                                // Then the drained content. Defensive fallback for a race
                                // where another drainer got there first (e.g. a concurrent
                                // wait_for_completion call) — keep the old placeholder so
                                // the AI still knows the result is being delivered
                                // somewhere, rather than staring at an empty response.
                                if (!string.IsNullOrEmpty(currentPipeCachedCompleted))
                                {
                                    cachedResponse.Append(currentPipeCachedCompleted);
                                }
                                else
                                {
                                    cachedResponse.Append("Result cached. Will be returned on next tool call.");
                                }
                                return Wrap(cachedResponse.ToString());

                            case "error":
                                return Wrap(jsonResponse.Message ?? $"Error from PowerShell.MCP module: {jsonResponse.Error}");

                            case "success":
                                // Snapshot AI-intended cwd at successful completion. This
                                // is the post-execution cwd — where AI's pipeline left the
                                // shell — so the next invoke_expression's drift check
                                // compares against the place AI actually finished at.
                                if (jsonResponse.Pid > 0 && !string.IsNullOrEmpty(jsonResponse.Cwd))
                                    sessionManager.SetLastAiCwd(agentId, jsonResponse.Pid, jsonResponse.Cwd);

                                // Normal completion - use body as result
                                var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

                                // Split body into status line and output
                                var statusLine = body;
                                var output = "";
                                var bodyNewlineIndex = body.IndexOf('\n');
                                if (bodyNewlineIndex >= 0)
                                {
                                    statusLine = body[..bodyNewlineIndex];
                                    output = body[(bodyNewlineIndex + 1)..];
                                }

                                var successResponse = new StringBuilder();
                                if (closedConsoleMessages.Count > 0)
                                {
                                    successResponse.AppendLine(string.Join("\n", closedConsoleMessages));
                                    successResponse.AppendLine();
                                }
                                if (busyStatusInfo.Length > 0)
                                {
                                    successResponse.Append(busyStatusInfo);
                                }
                                if (!string.IsNullOrEmpty(allPipesStatusInfo))
                                {
                                    successResponse.AppendLine(allPipesStatusInfo);
                                }
                                if (!string.IsNullOrEmpty(startupNotice))
                                {
                                    successResponse.AppendLine(startupNotice);
                                    successResponse.AppendLine();
                                }
                                if (completedOutput.Length > 0)
                                {
                                    successResponse.Append(completedOutput);
                                }
                                // Status line first
                                successResponse.AppendLine(statusLine);
                                // Then output (already starts with \n from body split)
                                if (output.Length > 0)
                                {
                                    successResponse.Append(output);
                                }
                                // Scope warning at the end (after output for better readability)
                                if (!string.IsNullOrEmpty(scopeWarning))
                                {
                                    successResponse.AppendLine();
                                    successResponse.AppendLine(scopeWarning);
                                }
                                // TODO: Uncomment when JsonDuo is published to PS Gallery
                                // var jsonHint = PipelineHelper.CheckJsonFileHint(pipeline, agentId)
                                //     ?? PipelineHelper.CheckJsonFileHint(output, agentId);
                                // if (!string.IsNullOrEmpty(jsonHint))
                                // {
                                //     successResponse.AppendLine();
                                //     successResponse.AppendLine(jsonHint);
                                // }
                                return Wrap(successResponse.ToString());
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
            Console.Error.WriteLine($"[ERROR] InvokeExpression failed: {ex.Message}");
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
    [Description("Wait for busy console(s) to complete and retrieve cached results. Use this after receiving 'Pipeline is still running' response instead of executing Start-Sleep (which would open a new console).")]
    public static async Task<string> WaitForCompletion(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        [Description("Maximum seconds to wait for completion (1-170, default: 30). Returns early if a console completes.")]
        int timeout_seconds = 30,
        [Description("Agent ID for sub-agent console isolation. Obtain this by calling start_console with is_subagent=true. Do not pass arbitrary strings.")]
        string? agent_id = null,
        [Description("Set to true if you are a sub-agent. A unique agent_id will be allocated and returned in the response. Use that agent_id for all subsequent tool calls.")]
        bool is_subagent = false,
        CancellationToken cancellationToken = default)
    {
        var sessionManager = ConsoleSessionManager.Instance;

        // wait_for_completion requires an existing agent_id for sub-agents
        if (is_subagent && string.IsNullOrEmpty(agent_id))
            return "❌ Sub-agents must obtain an agent_id by calling start_console, get_current_location, or invoke_expression with is_subagent=true before calling wait_for_completion.";

        var agentId = string.IsNullOrEmpty(agent_id) ? "default" : agent_id;

        // Validate agent_id
        if (!ConsoleSessionManager.Instance.IsValidAgentId(agentId))
            return $"❌ Invalid agent_id '{agentId}'. Sub-agents must first call start_console with is_subagent=true to obtain a valid agent_id. Do not pass arbitrary strings as agent_id.";

        timeout_seconds = Math.Clamp(timeout_seconds, 1, 170);

        const int pollIntervalMs = 1000;
        var endTime = DateTime.UtcNow.AddSeconds(timeout_seconds);

        // Detect externally closed consoles - return immediately if any previously busy pipe is gone
        var closedConsoleMessages = new List<string>();
        var previouslyBusyPids = sessionManager.ConsumeKnownBusyPids(agentId);
        var currentPipes = sessionManager.EnumeratePipes(sessionManager.ProxyPid, agentId).ToList();
        var currentPids = currentPipes
            .Select(ConsoleSessionManager.GetPidFromPipeName)
            .Where(p => p.HasValue)
            .Select(p => p.Value)
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

            if (status.Status == PipeStatus.Busy)
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

            // If no completed output and no busy info, return "No commands to wait for completion."
            if (completedOutput.Length == 0 && busyStatusInfo.Length == 0 && closedConsoleMessages.Count == 0)
            {
                return "No commands to wait for completion.";
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

    private static string BuildWaitResponse(List<string> closedConsoleMessages, string completedOutput, string busyStatusInfo)
    {
        var response = new StringBuilder();
        if (busyStatusInfo.Length > 0)
        {
            response.Append(busyStatusInfo);
        }
        if (closedConsoleMessages.Count > 0)
        {
            response.AppendLine(string.Join("\n", closedConsoleMessages));
            response.AppendLine();
        }
        if (completedOutput.Length > 0)
        {
            response.Append(completedOutput);
        }
        if (busyStatusInfo.Length > 0)
        {
            response.AppendLine();
            response.Append("Use wait_for_completion tool to wait and retrieve the result.");
        }

        if (response.Length == 0)
        {
            return "No busy consoles or cached results.";
        }

        return response.ToString();
    }

    [McpServerTool]
    [Description(@"Ensure a PowerShell console is available, or launch a new one. When reason is empty or omitted, reuses an existing standby console if one is available. When reason is provided, always launches a new console regardless of existing ones.

📌 This is your primary tool for all command execution tasks: directory navigation, git operations, build/test commands, file system operations, process management, environment variable access, and any shell/terminal task. Sessions persist across calls (authentication, variables, modules stay active). Install any PowerShell Gallery module without admin privileges to extend capabilities (e.g., Az for Azure, AWS.Tools for AWS, Microsoft.Graph for M365).")]
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

        // Skip unowned-pipe discovery when the caller didn't pin a target
        // cwd. Without an explicit start_location, the AI hasn't expressed
        // a "where I want to be" intent; claiming an unowned console
        // (whose cwd is whatever the user happened to be in when they
        // ran Import-Module) would inherit an arbitrary cwd and confuse
        // subsequent invoke_expression calls. A fresh console at the
        // proxy's default home is the predictable baseline. Already-owned
        // standby consoles are still reused — the skip only blocks the
        // unowned-claim step.
        var includeUnowned = !string.IsNullOrEmpty(start_location);

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

                var reuseResponse = new StringBuilder();
                // Report closed consoles detected during discovery
                if (discoveryResult.ClosedConsoleMessages.Count > 0)
                {
                    foreach (var msg in discoveryResult.ClosedConsoleMessages)
                        reuseResponse.AppendLine(msg);
                    reuseResponse.AppendLine();
                }
                // Always collect cached outputs - any console may have completed work
                var (reuseCompletedOutput, reuseBusyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, discoveryResult.ReadyPipeName, cancellationToken);
                if (reuseBusyStatusInfo.Length > 0)
                {
                    reuseResponse.Append(reuseBusyStatusInfo);
                    reuseResponse.AppendLine();
                }
                if (reuseCompletedOutput.Length > 0)
                {
                    reuseResponse.Append(reuseCompletedOutput);
                }
                reuseResponse.AppendLine("ℹ️ Did not launch a new console. An existing standby console is available and will be reused. To force a new console, provide the reason parameter.");
                reuseResponse.AppendLine();
                reuseResponse.Append(reuseLocationResult);
                return Wrap(reuseResponse.ToString());
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

        // Set console window title
        var newPipeName = ConsoleSessionManager.Instance.GetActivePipeName(agentId);
        if (newPipeName != null)
        {
            await SetConsoleTitleAsync(powerShellService, newPipeName, cancellationToken);
        }

        // Collect busy status from Proxy side
        var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, newPipeName, cancellationToken);

        // Build response: busy status first + completed output + start message + location
        var response = new StringBuilder();
        if (busyStatusInfo.Length > 0)
        {
            response.Append(busyStatusInfo);
            response.AppendLine();
        }
        if (completedOutput.Length > 0)
        {
            response.Append(completedOutput);
        }
        if (!string.IsNullOrEmpty(warningMessage))
        {
            response.Append(warningMessage);
            response.AppendLine();
        }
        response.AppendLine("PowerShell console started successfully with PowerShell.MCP module imported.");
        response.AppendLine();
        response.Append(startResult);
        return Wrap(response.ToString());
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
