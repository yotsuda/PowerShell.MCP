using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Models;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Proxy.Tools;

[McpServerToolType]
public class PowerShellTools
{
    /// <summary>
    /// Finds a ready pipe. Returns (pipeName, consoleSwitched, allPipesStatusInfo).
    /// allPipesStatusInfo contains status of all pipes when no ready pipe is found.
    /// Also detects externally closed consoles by comparing known busy PIDs with current pipes.
    /// </summary>
    private static async Task<(string? readyPipeName, bool consoleSwitched, string? allPipesStatusInfo)> FindReadyPipeAsync(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken)
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var allPipesStatus = new List<string>();

        // Detect externally closed consoles
        var previouslyBusyPids = sessionManager.ConsumeKnownBusyPids();
        var currentPipes = sessionManager.EnumeratePipes(sessionManager.ProxyPid).ToList();
        var currentPids = currentPipes
            .Select(ConsoleSessionManager.GetPidFromPipeName)
            .Where(p => p.HasValue)
            .Select(p => p!.Value)
            .ToHashSet();

        foreach (var pid in previouslyBusyPids)
        {
            if (!currentPids.Contains(pid))
            {
                allPipesStatus.Add($"  - ⚠ Console PID {pid} was closed");
            }
        }

        // Step 1: Check ActivePipeName first
        var activePipe = sessionManager.ActivePipeName;
        if (activePipe != null)
        {
            var status = await powerShellService.GetStatusFromPipeAsync(activePipe, cancellationToken);

            if (status == null)
            {
                sessionManager.ClearDeadPipe(activePipe);
                var pid = ConsoleSessionManager.GetPidFromPipeName(activePipe);
                allPipesStatus.Add($"  - ⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");
            }
            else if (status.Status == "standby" || status.Status == "completed")
            {
                if (status.Pid > 0) sessionManager.UnmarkPipeBusy(status.Pid);
                return (activePipe, false, BuildClosedConsoleInfo(allPipesStatus));
            }
            else // busy
            {
                if (status.Pid > 0) sessionManager.MarkPipeBusy(status.Pid);
                allPipesStatus.Add($"  - {activePipe}: {status.Status} (pipeline: {status.Pipeline ?? "unknown"}, duration: {status.Duration:F1}s)");
            }
        }

        // Step 2: Check all other pipes via EnumeratePipes
        foreach (var pipeName in currentPipes)
        {
            if (pipeName == activePipe) continue; // Already checked

            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                sessionManager.ClearDeadPipe(pipeName);
                var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                allPipesStatus.Add($"  - ⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");
                continue;
            }

            if (status.Status == "standby" || status.Status == "completed")
            {
                if (status.Pid > 0) sessionManager.UnmarkPipeBusy(status.Pid);
                sessionManager.SetActivePipeName(pipeName);
                return (pipeName, true, BuildClosedConsoleInfo(allPipesStatus));
            }

            if (status.Pid > 0) sessionManager.MarkPipeBusy(status.Pid);
            allPipesStatus.Add($"  - {pipeName}: {status.Status} (pipeline: {status.Pipeline ?? "unknown"}, duration: {status.Duration:F1}s)");
        }

        // Step 3: Check unowned pipes (user-started consoles not yet claimed by any proxy)
        foreach (var pipeName in sessionManager.EnumerateUnownedPipes())
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                sessionManager.ClearDeadPipe(pipeName);
                continue;
            }

            if (status.Status == "standby" || status.Status == "completed")
            {
                // Claim this console - response may not be received because pipe closes during rename
                var pwshPid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                if (!pwshPid.HasValue) continue;

                // Fire and forget - the pipe will close before response
                _ = powerShellService.ClaimConsoleAsync(pipeName, sessionManager.ProxyPid, cancellationToken);

                // Calculate the expected new pipe name
                var newPipeName = ConsoleSessionManager.GetPipeNameForPids(sessionManager.ProxyPid, pwshPid.Value);

                // Wait for the new pipe to become available (retry with short timeout)
                for (int i = 0; i < 20; i++)
                {
                    await Task.Delay(100, cancellationToken);

                    var newStatus = await powerShellService.GetStatusFromPipeAsync(newPipeName, cancellationToken);
                    if (newStatus != null)
                    {
                        sessionManager.SetActivePipeName(newPipeName);
                        return (newPipeName, true, BuildClosedConsoleInfo(allPipesStatus));
                    }
                }
            }

            if (status.Pid > 0) sessionManager.MarkPipeBusy(status.Pid);
            allPipesStatus.Add($"  - {pipeName} (unowned): {status.Status}");
        }

        // No ready pipe found
        var statusInfo = allPipesStatus.Count > 0
            ? "All pipes status:\n" + string.Join("\n", allPipesStatus)
            : "No pipes found.";
        return (null, false, statusInfo);
    }

    /// <summary>
    /// Builds closed console info string if there are any closed console messages.
    /// </summary>
    private static string? BuildClosedConsoleInfo(List<string> allPipesStatus)
    {
        var closedMessages = allPipesStatus.Where(s => s.Contains("was closed")).ToList();
        if (closedMessages.Count == 0) return null;
        return string.Join("\n", closedMessages);
    }

    private static string FormatBusyStatus(GetStatusResponse status)
    {
        // Use statusLine from dll if available, otherwise fallback to old format
        if (!string.IsNullOrEmpty(status.StatusLine))
            return status.StatusLine;

        var truncatedPipeline = TruncatePipeline(status.Pipeline ?? "");
        return $"⧗ | pwsh PID: {status.Pid} | Status: Busy | Pipeline: {truncatedPipeline} | Duration: {status.Duration:F2}s";
    }

    /// <summary>
    /// Collects cached outputs and busy status from all pipes except excludePipeName.
    /// Returns (completedOutput, busyStatusInfo).
    /// </summary>
    private static async Task<(string completedOutput, string busyStatusInfo)> CollectAllCachedOutputsAsync(
        IPowerShellService powerShellService,
        string? excludePipeName,
        CancellationToken cancellationToken)
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var completedOutput = new StringBuilder();
        var busyStatusInfo = new StringBuilder();

        foreach (var pipeName in sessionManager.EnumeratePipes(sessionManager.ProxyPid))
        {
            if (pipeName == excludePipeName) continue;

            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                sessionManager.ClearDeadPipe(pipeName);
            }
            else if (status.Status == "completed")
            {
                if (status.Pid > 0) sessionManager.UnmarkPipeBusy(status.Pid);
                var output = await powerShellService.ConsumeOutputFromPipeAsync(pipeName, cancellationToken);
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
                if (status.Pid > 0) sessionManager.MarkPipeBusy(status.Pid);
                busyStatusInfo.AppendLine(FormatBusyStatus(status));
            }
            else if (status.Status == "standby")
            {
                if (status.Pid > 0) sessionManager.UnmarkPipeBusy(status.Pid);
            }
        }

        return (completedOutput.ToString(), busyStatusInfo.ToString());
    }

    private static string TruncatePipeline(string pipeline, int maxLength = 30)
    {
        if (string.IsNullOrEmpty(pipeline)) return "";

        // Normalize whitespace
        var normalized = string.Join(" ", pipeline.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..(maxLength - 3)] + "...";
    }

    private static string GetPidString(string? pipeName)
    {
        if (pipeName == null) return "unknown";
        var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
        return pid?.ToString() ?? "unknown";
    }

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
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns current_location and other_drive_locations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.")]
    public static async Task<string> GetCurrentLocation(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken = default)
    {
        // Find a ready pipe
        var (readyPipeName, _, allPipesStatusInfo) = await FindReadyPipeAsync(powerShellService, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start (StartPowershellConsole includes busy info collection)
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting... Reason: {allPipesStatusInfo}");
            return await StartPowershellConsole(powerShellService, cancellationToken: cancellationToken);
        }

        try
        {
            // Get location (DLL will include its own cached outputs automatically)
            var result = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);

            // Collect completed outputs and busy status info from other pipes
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

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
    [Description(@"Execute PowerShell cmdlets and CLI tools (e.g., git) in persistent console. Session persists: modules, variables, functions, authentication stay active—no re-authentication. Install any modules and learn them via Get-Help. Commands visible in history for user learning.

⚠️ CRITICAL - Variable Scope:
Local variables are NOT preserved between invoke_expression calls. Use $script: or $global: scope to share variables across calls.

⚠️ CRITICAL - Verbose/Debug Output:
Verbose and Debug streams are NOT visible to you. If you need verbose/debug information, ask the user to copy it from the console and share it with you.

⚠ CRITICAL - File Operations:
For user-provided paths (like C:\), use PowerShell.MCP tools ONLY. Server-side tools (such as str_replace) cannot access them.
When calling invoke_expression for file operations, ALWAYS use these cmdlets. NEVER use Set-Content, Get-Content, or Out-File:

• Show-TextFile [-Path] <string[]> [-Recurse] [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Encoding <string>]
  Displays file contents with line numbers. Filter by line range and/or matching text (literal or regex).
  Use -Recurse with -Pattern/-Contains to search subdirectories.
  Use negative LineRange to show tail: -LineRange -10 shows last 10 lines, -LineRange -10,-1 is equivalent.

• Add-LinesToFile [-Path] <string[]> [-LineNumber <int>] [-Content] <Object[]> [-Encoding <string>] [-Backup]
  Inserts lines at specified position or appends to end or creates new file. Accepts pipeline input for Content.

• Update-LinesInFile [-Path] <string[]> [[-LineRange] <int[]>] [-Content <Object[]>] [-Encoding <string>] [-Backup]
  Replaces ENTIRE LINES in specified range with new content. Use for replacing whole lines.
  Use -Content @() to delete lines. Accepts pipeline input for Content.

• Update-MatchInFile [-Path] <string[]> [-LineRange <int[]>] [-OldText <string>] [-Pattern <regex>] [-Replacement <string>] [-Encoding <string>] [-Backup]
  Replaces ONLY THE MATCHED PORTION within lines, not entire lines. Rest of line is preserved.
  ⚠️ Use -WhatIf first to preview changes.

• Remove-LinesFromFile [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Encoding <string>] [-Backup]
  Removes lines matching text (literal or regex) within optional range. Use negative LineRange to remove tail (e.g., -LineRange -10).
  ⚠️ With -Contains/-Pattern, use -WhatIf first to preview.

⚠️ Update-LinesInFile vs Update-MatchInFile:
  - Replace WHOLE LINE → Update-LinesInFile -LineRange N,N -Content 'new line'
  - Replace PART OF LINE → Update-MatchInFile -OldText 'old' -Replacement 'new'

Note: All cmdlets support -LiteralPath for exact paths and accept arrays directly (no loops needed). For LineRange, use -1 or 0 for end of file (e.g., 100,-1).

Examples:
  ✅ CORRECT: invoke_expression('Add-LinesToFile -Path file.cs -Content $code')
  ✅ CORRECT: invoke_expression('Show-TextFile file.txt -LineRange 10,20')
  ✅ CORRECT: invoke_expression('Show-TextFile file.txt -LineRange 100,-1')  # To end of file
  ✅ CORRECT: invoke_expression('Show-TextFile file.txt -LineRange -10')     # Last 10 lines
  ✅ CORRECT: invoke_expression('Update-LinesInFile file.md -LineRange 5,5 -Content ""new line""')  # Replace entire line 5
  ✅ CORRECT: invoke_expression('Update-MatchInFile file.md -OldText ""TODO"" -Replacement ""DONE""')  # Replace only ""TODO"" → ""DONE""
  ❌ WRONG: invoke_expression('Set-Content -Path file.cs -Value $code')
  ❌ WRONG: invoke_expression('Get-Content file.txt | Select-Object -Skip 9 -First 11')

For detailed examples: invoke_expression('Get-Help <cmdlet-name> -Examples')")]
    public static async Task<string> InvokeExpression(
        IPowerShellService powerShellService,
        [Description("The PowerShell command or pipeline to execute. Both single-line and multi-line commands are supported, including if statements, loops, functions, and try-catch blocks.")]
        string pipeline,
        [Description("Timeout in seconds (0-170, default: 170). On timeout, execution continues in background and result is cached for retrieval on next MCP tool call. You can work on other tasks in parallel. Use 0 for commands requiring user interaction (e.g., pause, Read-Host).")]
        int timeout_seconds = 170,
        CancellationToken cancellationToken = default)
    {
        // Clamp timeout to valid range
        timeout_seconds = Math.Clamp(timeout_seconds, 0, 170);


        var sessionManager = ConsoleSessionManager.Instance;
        // Find a ready pipe
        var (readyPipeName, consoleSwitched, allPipesStatusInfo) = await FindReadyPipeAsync(powerShellService, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start
            Console.Error.WriteLine($"[INFO] No ready PowerShell console found, auto-starting... Reason: {allPipesStatusInfo}");
            var (success, locationResult) = await StartPowershellConsoleInternal(powerShellService, null, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cancellationToken);
            if (!success)
            {
                return locationResult; // Error message
            }

            // Set console window title
            if (sessionManager.ActivePipeName != null)
            {
                await SetConsoleTitleAsync(powerShellService, sessionManager.ActivePipeName, cancellationToken);
            }

            // Collect completed outputs and busy status (after console start, using new pipe as exclude)
            var newPipeName = sessionManager.ActivePipeName;
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, newPipeName, cancellationToken);

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
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

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

        // Execute the command
        try
        {
            var result = await powerShellService.InvokeExpressionToPipeAsync(readyPipeName, pipeline, timeout_seconds, cancellationToken);
            // Parse response: header JSON (first line) + "\n\n" + body
            var separatorIndex = result.IndexOf("\n\n");
            var jsonHeader = separatorIndex >= 0 ? result.Substring(0, separatorIndex) : result;
            var body = separatorIndex >= 0 ? result.Substring(separatorIndex + 2) : "";

            if (jsonHeader.StartsWith("{"))
            {
                try
                {
                    var jsonResponse = JsonSerializer.Deserialize(jsonHeader, GetStatusResponseContext.Default.GetStatusResponse);
                    if (jsonResponse != null)
                    {
                        switch (jsonResponse.Status)
                        {
                            case "busy":
                                // Mark this pipe as busy for tracking
                                if (jsonResponse.Pid > 0) sessionManager.MarkPipeBusy(jsonResponse.Pid);

                                if (jsonResponse.Reason == "user_command" || jsonResponse.Reason == "mcp_command")
                                {
                                    // Auto-start new console
                                    Console.Error.WriteLine($"[INFO] Runspace busy ({jsonResponse.Reason}), auto-starting new console...");
                                    var (success, locationResult) = await StartPowershellConsoleInternal(powerShellService, null, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), cancellationToken);
                                    if (!success)
                                    {
                                        return locationResult; // Error message
                                    }

                                    // Set console window title
                                    if (sessionManager.ActivePipeName != null)
                                    {
                                        await SetConsoleTitleAsync(powerShellService, sessionManager.ActivePipeName, cancellationToken);
                                    }

                                    var newPipeName = sessionManager.ActivePipeName;
                                    var (completedOutputs, busyInfo) = await CollectAllCachedOutputsAsync(powerShellService, newPipeName, cancellationToken);

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
                                if (jsonResponse.Pid > 0) sessionManager.MarkPipeBusy(jsonResponse.Pid);

                                // Consume cached output from current pipe (if any)
                                var currentPipeCachedOutput = await powerShellService.ConsumeOutputFromPipeAsync(readyPipeName, cancellationToken);

                                // Collect completed outputs and busy status from other pipes
                                var (timeoutCompletedOutput, timeoutBusyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

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
                                if (!string.IsNullOrEmpty(scopeWarning))
                                {
                                    timeoutResponse.AppendLine(scopeWarning);
                                    timeoutResponse.AppendLine();
                                }
                                // Use statusLine from dll if available
                                var timeoutStatusLine = !string.IsNullOrEmpty(jsonResponse.StatusLine)
                                    ? jsonResponse.StatusLine
                                    : $"⧗ Pipeline is still running | pwsh PID: {jsonResponse.Pid} | Status: Busy | Pipeline: {jsonResponse.Pipeline} | Duration: {jsonResponse.Duration:F2}s";
                                timeoutResponse.AppendLine(timeoutStatusLine);
                                timeoutResponse.AppendLine();
                                timeoutResponse.Append("Use wait_for_completion tool to wait and retrieve the result.");
                                return timeoutResponse.ToString();

                            case "completed":
                                // Result was cached - return status
                                // Collect busy status from other pipes
                                var (cachedCompletedOutput, cachedBusyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

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

                            case "success":
                                // Normal completion - use body as result
                                var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

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
                                if (!string.IsNullOrEmpty(scopeWarning))
                                {
                                    successResponse.AppendLine(scopeWarning);
                                    successResponse.AppendLine();
                                }
                                if (successResponse.Length > 0 && string.IsNullOrEmpty(scopeWarning))
                                {
                                    successResponse.AppendLine();
                                }
                                successResponse.Append(body);
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
        [Description("Maximum seconds to wait for completion (1-170, default: 30). Returns early if a console completes.")]
        int timeout_seconds = 30,
        CancellationToken cancellationToken = default)
    {
        var sessionManager = ConsoleSessionManager.Instance;

        timeout_seconds = Math.Clamp(timeout_seconds, 1, 170);

        const int pollIntervalMs = 1000;
        var endTime = DateTime.UtcNow.AddSeconds(timeout_seconds);

        // Detect externally closed consoles - return immediately if any previously busy pipe is gone
        var closedConsoleMessages = new List<string>();
        var previouslyBusyPids = sessionManager.ConsumeKnownBusyPids();
        var currentPipes = sessionManager.EnumeratePipes(sessionManager.ProxyPid).ToList();
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
            var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);
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
                sessionManager.ClearDeadPipe(pipeName);
                var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                closedConsoleMessages.Add($"⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");

                var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);
                return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
            }

            if (status.Status == "completed")
            {
                // Completed - collect all cached outputs (including this one) and return
                var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);
                return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
            }

            if (status.Status == "busy")
            {
                if (status.Pid > 0) sessionManager.MarkPipeBusy(status.Pid);
                // Only track MCP-initiated commands, not user commands
                if (status.Pipeline != "(user command)")
                {
                    busyPipes.Add(pipeName);
                }
            }
            else if (status.Status == "standby")
            {
                if (status.Pid > 0) sessionManager.UnmarkPipeBusy(status.Pid);
            }
        }

        // No MCP-initiated busy pipes - collect any cached outputs and return
        if (busyPipes.Count == 0)
        {
            var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);

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
                    sessionManager.ClearDeadPipe(pipeName);
                    busyPipes.Remove(pipeName);
                    var pid = ConsoleSessionManager.GetPidFromPipeName(pipeName);
                    closedConsoleMessages.Add($"⚠ Console PID {pid?.ToString() ?? "unknown"} was closed");

                    var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);
                    return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
                }

                if (status.Status == "completed")
                {
                    // Completed - collect all cached outputs (including this one) and return
                    var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);
                    return BuildWaitResponse(closedConsoleMessages, completedOutput, busyStatusInfo);
                }


                if (status.Status == "standby")
                {
                    if (status.Pid > 0) sessionManager.UnmarkPipeBusy(status.Pid);
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
        var (finalCompletedOutput, finalBusyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);
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
    [Description("Launch a new PowerShell console window with PowerShell.MCP module imported. This tool should only be executed when explicitly requested by the user or when other tool executions fail.")]
    public static async Task<string> StartPowershellConsole(
        IPowerShellService powerShellService,
        [Description("Message displayed at console startup (e.g. greeting, joke, fun fact). Be creative and make the user smile!")]
        string? banner = null,
        [Description("Optional starting directory path. If relative, resolved from home directory. Defaults to home directory if not specified.")]
        string? start_location = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedPath, warningMessage) = ResolveStartLocation(start_location);
        var (success, locationResult) = await StartPowershellConsoleInternal(powerShellService, banner, resolvedPath, cancellationToken);
        if (!success)
        {
            return locationResult; // Error message
        }

        // Set console window title
        var sessionManager = ConsoleSessionManager.Instance;
        if (sessionManager.ActivePipeName != null)
        {
            await SetConsoleTitleAsync(powerShellService, sessionManager.ActivePipeName, cancellationToken);
        }

        // Collect busy status from Proxy side
        // sessionManager already declared above
        var newPipeName = sessionManager.ActivePipeName;
        var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, newPipeName, cancellationToken);

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
        response.Append(locationResult);
        return response.ToString();
    }

    /// <summary>
    /// Internal method to start PowerShell console.
    /// Returns (success, result) where result is locationResult on success, or error message on failure.
    /// </summary>
    private static async Task<(bool success, string result)> StartPowershellConsoleInternal(
        IPowerShellService powerShellService,
        string? banner,
        string startLocation,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionManager = ConsoleSessionManager.Instance;

            Console.Error.WriteLine("[INFO] Starting PowerShell console...");
            // Start new console
            var (success, pipeName) = await PowerShellProcessManager.StartPowerShellWithModuleAndPipeNameAsync(banner, startLocation);

            if (!success)
            {
                return (false, "Failed to start PowerShell console or establish Named Pipe connection.\n\nPossible causes:\n- No supported terminal emulator found (gnome-terminal, konsole, xfce4-terminal, xterm, etc.)\n- Terminal emulator failed to start\n- PowerShell.MCP module failed to initialize\n\nPlease ensure a terminal emulator is installed and try again.");
            }

            // Register the new console
            sessionManager.SetActivePipeName(pipeName);

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
    {
        // Pattern: $varname = (but not $script:, $global:, $env:, $using:, $null, $true, $false)
        // Also exclude common automatic variables like $_, $?, $^, $$, $args, $input, $foreach, $switch
        var pattern = @"\$(?!script:|global:|env:|using:|null\b|true\b|false\b|_\b|\?\b|\^\b|\$\b|args\b|input\b|foreach\b|switch\b|Matches\b|PSItem\b)([a-zA-Z_]\w*)\s*=";
        var matches = Regex.Matches(pipeline, pattern);

        if (matches.Count == 0) return null;

        var vars = matches.Select(m => "$" + m.Groups[1].Value).Distinct().ToList();
        if (vars.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("⚠️ SCOPE WARNING: Local variable assignment(s) detected:");
        foreach (var v in vars)
        {
            sb.AppendLine($"  {v} → Consider using {v.Replace("$", "$script:")} to preserve across calls");
        }
        return sb.ToString().TrimEnd();
    }
}
