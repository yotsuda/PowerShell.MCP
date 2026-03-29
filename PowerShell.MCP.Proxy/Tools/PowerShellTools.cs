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
    private static async Task<(string? readyPipeName, bool consoleSwitched, IReadOnlyList<string> closedConsoleMessages, string? allPipesStatusInfo)> FindReadyPipeAsync(
        IPipeDiscoveryService pipeDiscoveryService,
        string agentId,
        CancellationToken cancellationToken)
    {
        var result = await pipeDiscoveryService.FindReadyPipeAsync(agentId, cancellationToken);
        return (result.ReadyPipeName, result.ConsoleSwitched, result.ClosedConsoleMessages, result.AllPipesStatusInfo);
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
        if (error != null)
            return error;

        // Find a ready pipe
        var (readyPipeName, _, closedConsoleMessages, allPipesStatusInfo) = await FindReadyPipeAsync(pipeDiscoveryService, agentId, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start (StartConsole includes busy info collection)
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting... Reason: {allPipesStatusInfo}");
            return await StartConsole(powerShellService, pipeDiscoveryService, agent_id: agentId, is_subagent: is_subagent, cancellationToken: cancellationToken);
        }

        try
        {
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
            if (isNewlyAllocated)
            {
                response.AppendLine($"🔑 Your agent_id is: {agentId} — pass this in all subsequent tool calls.");
                response.AppendLine();
            }
            response.Append(result);
            return response.ToString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] GetCurrentLocation failed: {ex.Message}");
            return $"Failed to get current location: {ex.Message}\n\nPlease try again. A new console will be started automatically if needed.";
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

⚠️ CRITICAL - Verbose/Debug Output:
Verbose and Debug streams are NOT visible to you. If you need verbose/debug information, ask the user to copy it from the console and share it with you.

📝 Text File Operations:
ALWAYS use the specialized cmdlets for text file editing: Show-TextFiles, Add-LinesToFile, Update-LinesInFile, Update-MatchInFile, Remove-LinesFromFile.
NEVER use Set-Content, [IO.File]::WriteAllText, or other alternatives—even when source code contains $ or backtick characters. Instead, pass content via var1-var4 parameters.
Create new file: Add-LinesToFile path -Content $var1 (with var1 parameter containing the content)
Edit existing file: Add-LinesToFile, Update-LinesInFile, Update-MatchInFile, Remove-LinesFromFile (use var1-var4 for content with $, backtick, or quotes)
For detailed examples: invoke_expression('Get-Help <cmdlet-name> -Examples')
Edit cmdlets show changed lines with 2 lines of context. Use Show-TextFiles after editing if you need the full file view.

📌 Prefer these cmdlets over other file read/edit/search tools provided by the host application. They handle special characters ($, backtick, double-quote) safely via var1-var4 parameters, and keep all operations in a single persistent session without context switching.

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
        if (resolveError != null)
            return resolveError;

        var sessionManager = ConsoleSessionManager.Instance;
        // Find a ready pipe
        var (readyPipeName, consoleSwitched, closedConsoleMessages, allPipesStatusInfo) = await FindReadyPipeAsync(pipeDiscoveryService, agentId, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting... Reason: {allPipesStatusInfo}");
            var (success, locationResult) = await StartConsoleInternal(powerShellService, agentId, null, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cancellationToken);
            if (!success)
            {
                return locationResult; // Error message
            }

            // Set console window title
            var activeAfterStart = sessionManager.GetActivePipeName(agentId);
            if (activeAfterStart != null)
            {
                await SetConsoleTitleAsync(powerShellService, activeAfterStart, cancellationToken);
            }

            // Collect completed outputs and busy status (after console start, using new pipe as exclude)
            var newPipeName = sessionManager.GetActivePipeName(agentId);
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, newPipeName, cancellationToken);

            var consoleName = GetConsoleName(newPipeName);

            // Build response: closedConsoles + busy status first + message + location + completedOutputs
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
            response.AppendLine($"Started new console {consoleName} with PowerShell.MCP module imported. Pipeline NOT executed - verify location and re-execute.");
            if (isNewlyAllocated)
            {
                response.AppendLine($"🔑 Your agent_id is: {agentId} — pass this in all subsequent tool calls.");
            }
            response.AppendLine();
            response.Append(locationResult);
            if (completedOutputs.Length > 0)
            {
                response.AppendLine();
                response.AppendLine();
                response.Append(completedOutputs);
            }
            return response.ToString();
        }

        // Console switched - get location (DLL will automatically include its own cached outputs)
        if (consoleSwitched)
        {
            // Set console window title before reading location (so status line shows the new name)
            await SetConsoleTitleAsync(powerShellService, readyPipeName, cancellationToken);
            var locationResult = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

            var consoleName = GetConsoleName(readyPipeName);

            // Build response: closedConsoles + busy status + message + locationResult + completedOutputs
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
            if (!string.IsNullOrEmpty(allPipesStatusInfo))
            {
                response.AppendLine(allPipesStatusInfo);
            }
            response.AppendLine($"Switched to console {consoleName}. Pipeline NOT executed - verify location and re-execute.");
            response.AppendLine();
            response.AppendLine(locationResult);
            if (completedOutputs.Length > 0)
            {
                response.AppendLine();
                response.Append(completedOutputs);
            }
            return response.ToString();
        }

        // Check for local variable assignments without scope prefix
        var scopeWarning = CheckLocalVariableAssignments(pipeline);

        // Enforce var1/var2 usage for text editing cmdlets
        var var1Error = PipelineHelper.CheckVar1Enforcement(pipeline, var1, var2);
        if (var1Error != null)
        {
            return var1Error;
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
                                    // Auto-start new console
                                    Console.Error.WriteLine($"[INFO] Runspace busy ({jsonResponse.Reason}), auto-starting new console...");
                                    var (success, locationResult) = await StartConsoleInternal(powerShellService, agentId, null, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cancellationToken);
                                    if (!success)
                                    {
                                        return locationResult; // Error message
                                    }

                                    // Set console window title
                                    var activeAfterBusy = sessionManager.GetActivePipeName(agentId);
                                    if (activeAfterBusy != null)
                                    {
                                        await SetConsoleTitleAsync(powerShellService, activeAfterBusy, cancellationToken);
                                    }

                                    var newPipeName = sessionManager.GetActivePipeName(agentId);
                                    var (completedOutputs, busyInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, newPipeName, cancellationToken);

                                    var newConsoleName = GetConsoleName(newPipeName);

                                    var busyResponse = new StringBuilder();
                                    // Busy status at the top (current pipe first, then other pipes)
                                    busyResponse.AppendLine(FormatBusyStatus(jsonResponse));
                                    if (busyInfo.Length > 0)
                                    {
                                        busyResponse.Append(busyInfo);
                                    }
                                    busyResponse.AppendLine();
                                    busyResponse.AppendLine($"Started new console {newConsoleName} with PowerShell.MCP module imported. Pipeline NOT executed - verify location and re-execute.");
                                    busyResponse.AppendLine();
                                    busyResponse.Append(locationResult);
                                    if (completedOutputs.Length > 0)
                                    {
                                        busyResponse.AppendLine();
                                        busyResponse.AppendLine();
                                        busyResponse.Append(completedOutputs);
                                    }
                                    return busyResponse.ToString();
                                }
                                break;

                            case "timeout":
                                // Mark this pipe as busy for tracking
                                if (jsonResponse.Pid > 0) sessionManager.MarkPipeBusy(agentId, jsonResponse.Pid);

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
                                return timeoutResponse.ToString();

                            case PipeStatus.Completed:
                                // Result was cached - return status
                                // Collect busy status from other pipes
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
                                if (cachedCompletedOutput.Length > 0)
                                {
                                    cachedResponse.Append(cachedCompletedOutput);
                                }
                                // Use statusLine from dll if available
                                var cachedStatusLine = !string.IsNullOrEmpty(jsonResponse.StatusLine)
                                    ? jsonResponse.StatusLine
                                    : $"✓ Pipeline executed successfully | {ConsoleSessionManager.Instance.GetConsoleDisplayName(jsonResponse.Pid)} | Status: Completed | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
                                cachedResponse.AppendLine(cachedStatusLine);
                                cachedResponse.AppendLine();
                                cachedResponse.Append("Result cached. Will be returned on next tool call.");
                                return cachedResponse.ToString();

                            case "error":
                                return jsonResponse.Message ?? $"Error from PowerShell.MCP module: {jsonResponse.Error}";

                            case "success":
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
                                // One-time hint about MarkdownPointer module
                                var markdownHint = PipelineHelper.CheckMarkdownFileHint(pipeline, agentId)
                                    ?? PipelineHelper.CheckMarkdownFileHint(output, agentId);
                                if (!string.IsNullOrEmpty(markdownHint))
                                {
                                    successResponse.AppendLine();
                                    successResponse.AppendLine(markdownHint);
                                }
                                // TODO: Uncomment when JsonDuo is published to PS Gallery
                                // var jsonHint = PipelineHelper.CheckJsonFileHint(pipeline, agentId)
                                //     ?? PipelineHelper.CheckJsonFileHint(output, agentId);
                                // if (!string.IsNullOrEmpty(jsonHint))
                                // {
                                //     successResponse.AppendLine();
                                //     successResponse.AppendLine(jsonHint);
                                // }
                                return successResponse.ToString();
                        }
                    }
                }
                catch
                {
                    // Not valid JSON or parsing failed, return as-is
                }
            }

            // Fallback: return result as-is (shouldn't happen with new DLL)
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] InvokeExpression failed: {ex.Message}");
            // Detect and report closed console
            var consoleName = sessionManager.GetConsoleDisplayName(readyPipeName);
            sessionManager.ClearDeadPipe(agentId, readyPipeName);
            return $"Command execution failed: {ex.Message}\n  - ⚠ Console {consoleName} was closed\nPlease try again. A new console will be started automatically if needed.";
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
        if (resolveError != null)
            return resolveError;

        var forceNew = !string.IsNullOrEmpty(reason);

        // When no reason is given, try to reuse an existing standby console
        if (!forceNew)
        {
            var discoveryResult = await pipeDiscoveryService.FindReadyPipeAsync(agentId, cancellationToken);
            if (discoveryResult.ReadyPipeName != null)
            {
                // Set console window title if this was a newly claimed (unowned) console
                if (discoveryResult.ConsoleSwitched)
                {
                    await SetConsoleTitleAsync(powerShellService, discoveryResult.ReadyPipeName, cancellationToken);
                }

                // Display banner on the existing console silently (message only, no command echo)
                if (!string.IsNullOrEmpty(banner))
                {
                    var escaped = banner.Replace("'", "''");
                    await powerShellService.ExecuteSilentAsync(
                        discoveryResult.ReadyPipeName,
                        $"[Console]::WriteLine(); [Console]::WriteLine(); Write-Host '{escaped}' -ForegroundColor Green; [Console]::WriteLine(); try {{ $p = & {{ prompt }}; [Console]::Write($p.TrimEnd(' ').TrimEnd('>') + '> ' + \"`e[0K\") }} catch {{ [Console]::Write(\"PS $((Get-Location).Path)> `e[0K\") }}",
                        cancellationToken);
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
                if (isNewlyAllocated)
                {
                    reuseResponse.AppendLine($"🔑 Your agent_id is: {agentId} — pass this in all subsequent tool calls.");
                }
                reuseResponse.AppendLine();
                reuseResponse.Append(reuseLocationResult);
                return reuseResponse.ToString();
            }
            // No standby console found, fall through to create a new one
        }

        var (resolvedPath, warningMessage) = ResolveStartLocation(start_location);
        var startupCommands = BuildStartupCommands(banner, reason);
        var (success, startResult) = await StartConsoleInternal(powerShellService, agentId, startupCommands, resolvedPath, cancellationToken);
        if (!success)
        {
            return startResult; // Error message
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
        if (isNewlyAllocated)
        {
            response.AppendLine($"🔑 Your agent_id is: {agentId} — pass this in all subsequent tool calls.");
        }
        response.AppendLine();
        response.Append(startResult);
        return response.ToString();
    }

    /// <summary>
    /// Builds PowerShell commands to display banner and/or reason at console startup.
    /// Banner is shown in green, reason in dark yellow.
    /// Returns null if both are empty, or a string of PowerShell commands.
    /// </summary>
    private static string? BuildStartupCommands(string? banner, string? reason)
    {
        if (string.IsNullOrEmpty(banner) && string.IsNullOrEmpty(reason))
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(banner))
        {
            var escaped = banner.Replace("'", "''");
            parts.Add($"Write-Host '{escaped}' -ForegroundColor Green");
        }
        if (!string.IsNullOrEmpty(reason))
        {
            if (parts.Count > 0)
                parts.Add("Write-Host ''");  // blank line between banner and reason
            var escaped = reason.Replace("'", "''");
            parts.Add($"Write-Host 'Reason: {escaped}' -ForegroundColor DarkYellow");
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
    /// Checks for local variable assignments without scope prefix and returns a warning message.
    /// </summary>
    private static string? CheckLocalVariableAssignments(string pipeline)
        => PipelineHelper.CheckLocalVariableAssignments(pipeline);
}
