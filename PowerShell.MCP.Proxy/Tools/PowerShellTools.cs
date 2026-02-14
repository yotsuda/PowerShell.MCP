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
    private static async Task<(string? readyPipeName, bool consoleSwitched, string? allPipesStatusInfo)> FindReadyPipeAsync(
        IPipeDiscoveryService pipeDiscoveryService,
        string agentId,
        CancellationToken cancellationToken)
    {
        var result = await pipeDiscoveryService.FindReadyPipeAsync(agentId, cancellationToken);
        return (result.ReadyPipeName, result.ConsoleSwitched, result.AllPipesStatusInfo);
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

    private static string GetPidString(string? pipeName)
        => PipelineHelper.GetPidString(pipeName);

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

    [McpServerTool]
    [Description("Generate a unique agent ID for console isolation. Call this once before using any other PowerShell tools if you are a sub-agent.")]
    public static string GenerateAgentId()
    {
        return Guid.NewGuid().ToString("N")[..8];
    }

    [McpServerTool]
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns current_location and other_drive_locations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.")]
    public static async Task<string> GetCurrentLocation(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        [Description("Agent ID from generate_agent_id. Required for sub-agents to isolate their console sessions.")]
        string? agent_id = null,
        CancellationToken cancellationToken = default)
    {
        var agentId = string.IsNullOrEmpty(agent_id) ? "default" : agent_id;

        // Find a ready pipe
        var (readyPipeName, _, allPipesStatusInfo) = await FindReadyPipeAsync(pipeDiscoveryService, agentId, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start (StartPowershellConsole includes busy info collection)
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting... Reason: {allPipesStatusInfo}");
            return await StartPowershellConsole(powerShellService, pipeDiscoveryService, agent_id: agent_id, cancellationToken: cancellationToken);
        }

        try
        {
            // Get location (DLL will include its own cached outputs automatically)
            var result = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);

            // Collect completed outputs and busy status info from other pipes
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

            // Build response: busyStatusInfo + completedOutputs + result
            var response = new StringBuilder();
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

📌 The console window gives the user real-time visibility into your work. Prefer this tool for file operations, searches, and command execution.

💡 API Exploration: Use Invoke-RestMethod to explore Web APIs and Add-Type for Win32 API testing. Verify API behavior before writing production code—get immediate feedback without compilation.

⚠️ CRITICAL - Variable Scope:
Local variables are NOT preserved between invoke_expression calls. Use $script: or $global: scope to share variables across calls.

⚠️ CRITICAL - Verbose/Debug Output:
Verbose and Debug streams are NOT visible to you. If you need verbose/debug information, ask the user to copy it from the console and share it with you.

📝 Text File Operations:
ALWAYS use the specialized cmdlets for text file editing: Show-TextFiles, Add-LinesToFile, Update-LinesInFile, Update-MatchInFile, Remove-LinesFromFile.
NEVER use Set-Content, [IO.File]::WriteAllText, or other alternatives—even when source code contains $ or backtick characters. Instead, pass content via var1-var4 parameters (e.g., Update-MatchInFile path -Contains $var1 -Replacement $var2).
For detailed examples: invoke_expression('Get-Help <cmdlet-name> -Examples')
Edit cmdlets show changed lines with 2 lines of context. Use Show-TextFiles after editing if you need the full file view.

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
        [Description("Agent ID from generate_agent_id. Required for sub-agents to isolate their console sessions.")]
        string? agent_id = null,
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

        var agentId = string.IsNullOrEmpty(agent_id) ? "default" : agent_id;

        var sessionManager = ConsoleSessionManager.Instance;
        // Find a ready pipe
        var (readyPipeName, consoleSwitched, allPipesStatusInfo) = await FindReadyPipeAsync(pipeDiscoveryService, agentId, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting... Reason: {allPipesStatusInfo}");
            var (success, locationResult) = await StartPowershellConsoleInternal(powerShellService, agentId, null, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cancellationToken);
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

            // Extract PID from pipe name (format: PowerShell.MCP.Communication.{PID})
            var pid = GetPidString(newPipeName);

            // Build response: busy status first + message + location + completedOutputs
            var response = new StringBuilder();
            if (busyStatusInfo.Length > 0)
            {
                response.Append(busyStatusInfo);
                response.AppendLine();
            }
            response.AppendLine($"Started new console PID#{pid} with PowerShell.MCP module imported. Pipeline NOT executed - verify location and re-execute.");
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
            var locationResult = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);

            // Set console window title for claimed console
            await SetConsoleTitleAsync(powerShellService, readyPipeName, cancellationToken);
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(pipeDiscoveryService, agentId, readyPipeName, cancellationToken);

            // Extract PID from pipe name (format: PowerShell.MCP.Communication.{PID})
            var pid = GetPidString(readyPipeName);

            // Build response: busy status first + closedConsoleInfo + message + locationResult + completedOutputs
            var response = new StringBuilder();
            if (busyStatusInfo.Length > 0)
            {
                response.Append(busyStatusInfo);
                response.AppendLine();
            }
            if (!string.IsNullOrEmpty(allPipesStatusInfo))
            {
                response.AppendLine(allPipesStatusInfo);
            }
            response.AppendLine($"Switched to console PID#{pid}. Pipeline NOT executed - verify location and re-execute.");
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
        // Check if multi-line command (not added to console history)
        var historyWarning = PipelineHelper.CheckMultiLineHistory(pipeline);

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
                                    var (success, locationResult) = await StartPowershellConsoleInternal(powerShellService, agentId, null, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cancellationToken);
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

                                    var newPid = GetPidString(newPipeName);

                                    var busyResponse = new StringBuilder();
                                    // Busy status at the top (current pipe first, then other pipes)
                                    busyResponse.AppendLine(FormatBusyStatus(jsonResponse));
                                    if (busyInfo.Length > 0)
                                    {
                                        busyResponse.Append(busyInfo);
                                    }
                                    busyResponse.AppendLine();
                                    busyResponse.AppendLine($"Started new console PID#{newPid} with PowerShell.MCP module imported. Pipeline NOT executed - verify location and re-execute.");
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
                                    : $"⧗ Pipeline is still running | pwsh PID: {jsonResponse.Pid} | Status: Busy | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
                                timeoutResponse.AppendLine(timeoutStatusLine);
                                // Then warnings (history warning before scope warning for user visibility)
                                if (!string.IsNullOrEmpty(historyWarning))
                                {
                                    timeoutResponse.AppendLine();
                                    timeoutResponse.AppendLine(historyWarning);
                                }
                                if (!string.IsNullOrEmpty(scopeWarning))
                                {
                                    timeoutResponse.AppendLine();
                                    timeoutResponse.AppendLine(scopeWarning);
                                }
                                timeoutResponse.AppendLine();
                                timeoutResponse.Append("Use wait_for_completion tool to wait and retrieve the result.");
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
                                    : $"✓ Pipeline executed successfully | pwsh PID: {jsonResponse.Pid} | Status: Completed | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
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
                                // Then warnings (history warning before scope warning for user visibility)
                                if (!string.IsNullOrEmpty(historyWarning))
                                {
                                    successResponse.AppendLine();
                                    successResponse.AppendLine(historyWarning);
                                }
                                if (!string.IsNullOrEmpty(scopeWarning))
                                {
                                    successResponse.AppendLine();
                                    successResponse.AppendLine(scopeWarning);
                                }
                                // Then output
                                if (output.Length > 0)
                                {
                                    successResponse.AppendLine();
                                    successResponse.Append(output);
                                }
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
            return $"Command execution failed: {ex.Message}\n\nPlease try again. A new console will be started automatically if needed.";
        }
    }

    [McpServerTool]
    [Description("Wait for busy console(s) to complete and retrieve cached results. Use this after receiving 'Pipeline is still running' response instead of executing Start-Sleep (which would open a new console).")]
    public static async Task<string> WaitForCompletion(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        [Description("Maximum seconds to wait for completion (1-170, default: 30). Returns early if a console completes.")]
        int timeout_seconds = 30,
        [Description("Agent ID from generate_agent_id. Required for sub-agents to isolate their console sessions.")]
        string? agent_id = null,
        CancellationToken cancellationToken = default)
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var agentId = string.IsNullOrEmpty(agent_id) ? "default" : agent_id;

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
            .Select(p => p!.Value)
            .ToHashSet();

        foreach (var pid in previouslyBusyPids)
        {
            if (!currentPids.Contains(pid))
            {
                closedConsoleMessages.Add($"⚠ Console PID {pid} was closed");
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
                // Dead pipe detected - collect all cached outputs and return
                sessionManager.ClearDeadPipe(agentId, pipeName);
                var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                closedConsoleMessages.Add($"⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");

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
                    // Dead pipe detected - collect all cached outputs and return
                    sessionManager.ClearDeadPipe(agentId, pipeName);
                    busyPipes.Remove(pipeName);
                    var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                    closedConsoleMessages.Add($"⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");

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
    [Description("Ensure a PowerShell console is available, or launch a new one. When reason is empty or omitted, reuses an existing standby console if one is available. When reason is provided, always launches a new console regardless of existing ones.")]
    public static async Task<string> StartPowershellConsole(
        IPowerShellService powerShellService,
        IPipeDiscoveryService pipeDiscoveryService,
        [Description("Optional. Why a new console is needed. Leave empty to reuse an existing standby console when available.")]
        string? reason = null,
        [Description("Message displayed at console startup (e.g. greeting, joke, fun fact). Be creative and make the user smile!")]
        string? banner = null,
        [Description("Optional starting directory path. If relative, resolved from home directory. Defaults to home directory if not specified.")]
        string? start_location = null,
        [Description("Agent ID from generate_agent_id. Required for sub-agents to isolate their console sessions.")]
        string? agent_id = null,
        CancellationToken cancellationToken = default)
    {
        var agentId = string.IsNullOrEmpty(agent_id) ? "default" : agent_id;
        var forceNew = !string.IsNullOrEmpty(reason);

        // When no reason is given, try to reuse an existing standby console
        if (!forceNew)
        {
            var discoveryResult = await pipeDiscoveryService.FindReadyPipeAsync(agentId, cancellationToken);
            if (discoveryResult.ReadyPipeName != null)
            {
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
                if (discoveryResult.ConsoleSwitched)
                {
                    reuseResponse.AppendLine("No reason was provided, so an existing standby console was used instead of launching a new one. If you need a new console, specify why in the reason parameter.");
                }
                reuseResponse.AppendLine();
                reuseResponse.Append(reuseLocationResult);
                return reuseResponse.ToString();
            }
            // No standby console found, fall through to create a new one
        }

        var (resolvedPath, warningMessage) = ResolveStartLocation(start_location);
        var startupCommands = BuildStartupCommands(banner, reason);
        var (success, startResult) = await StartPowershellConsoleInternal(powerShellService, agentId, startupCommands, resolvedPath, cancellationToken);
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
    private static async Task<(bool success, string result)> StartPowershellConsoleInternal(
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
            Console.Error.WriteLine($"[ERROR] StartPowershellConsole failed: {ex.Message}");
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
