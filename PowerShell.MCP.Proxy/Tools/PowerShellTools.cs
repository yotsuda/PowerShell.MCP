using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Models;
using System.Runtime.InteropServices;

namespace PowerShell.MCP.Proxy.Tools;

[McpServerToolType]
public class PowerShellTools
{
    // Error message constant definitions
    private const string ERROR_CONSOLE_NOT_RUNNING = "The PowerShell 7 console is not running.";
    /// <summary>
    /// Finds a ready pipe. Returns (pipeName, consoleSwitched).
    /// </summary>
    private static async Task<(string? readyPipeName, bool consoleSwitched)> FindReadyPipeAsync(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken)
    {
        var sessionManager = ConsoleSessionManager.Instance;

        // Step 1: Check ActivePipeName
        var activePipe = sessionManager.ActivePipeName;
        if (activePipe != null)
        {
            var status = await powerShellService.GetStatusFromPipeAsync(activePipe, cancellationToken);

            if (status == null)
            {
                sessionManager.ClearDeadPipe(activePipe);
            }
            else if (status.Status == "standby" || status.Status == "completed")
            {
                // No switch - DLL will handle its own cache automatically
                return (activePipe, false);
            }
            else // busy
            {
                var busyInfo = FormatBusyStatus(status);
                sessionManager.MarkPipeBusy(activePipe, busyInfo);
            }
        }

        // Step 1.5: Check busy pipes - they might have become ready
        var busyPipes = sessionManager.GetBusyPipes().Keys.ToList();
        foreach (var pipeName in busyPipes)
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                sessionManager.ClearDeadPipe(pipeName);
                continue;
            }

            if (status.Status == "standby" || status.Status == "completed")
            {
                // Found a ready pipe - set as active and return
                sessionManager.SetActivePipeName(pipeName);
                return (pipeName, activePipe != null);
            }
            // busy pipes stay as they are - CollectAllCachedOutputsAsync will handle completed ones later
        }

        // Step 2: Discover new standby pipe
        var knownPipes = new HashSet<string>(sessionManager.GetBusyPipes().Keys);
        if (activePipe != null) knownPipes.Add(activePipe);

        foreach (var pipeName in sessionManager.EnumeratePipes().Where(p => !knownPipes.Contains(p)))
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null) continue;

            if (status.Status == "standby" || status.Status == "completed")
            {
                sessionManager.SetActivePipeName(pipeName);
                return (pipeName, activePipe != null);
            }

            if (status.Status == "busy")
            {
                var busyInfo = FormatBusyStatus(status);
                sessionManager.MarkPipeBusy(pipeName, busyInfo);
            }
        }

        // No ready pipe found
        return (null, false);
    }

    private static string FormatBusyStatus(GetStatusResponse status)
    {
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

        // Collect pipes to check: busyPipes (excluding excludePipeName)
        var pipesToCheck = new HashSet<string>(sessionManager.GetBusyPipes().Keys);
        if (excludePipeName != null)
        {
            pipesToCheck.Remove(excludePipeName);
        }
        foreach (var pipeName in pipesToCheck)
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                sessionManager.ClearDeadPipe(pipeName);
            }
            else if (status.Status == "completed")
            {
                // Consume the cached output
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
                sessionManager.RemoveFromBusy(pipeName);
            }
            else if (status.Status == "standby")
            {
                sessionManager.RemoveFromBusy(pipeName);
            }
            else // busy
            {
                var busyInfo = FormatBusyStatus(status);
                sessionManager.MarkPipeBusy(pipeName, busyInfo);
                busyStatusInfo.AppendLine(busyInfo);
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

    private static string BuildNotExecutedResponse(string message, string jsonContent)
    {
        var response = new StringBuilder();
        response.AppendLine(message);
        response.AppendLine();
        response.Append(jsonContent);
        return response.ToString();
    }

    [McpServerTool]
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns current_location and other_drive_locations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.")]
    public static async Task<string> GetCurrentLocation(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken = default)
    {
        // Find a ready pipe
        var (readyPipeName, _) = await FindReadyPipeAsync(powerShellService, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start (StartPowershellConsole includes busy info collection)
            Console.Error.WriteLine("[INFO] No ready PowerShell console found, auto-starting...");
            return await StartPowershellConsole(powerShellService, cancellationToken: cancellationToken);
        }

        try
        {
            // Get location (DLL will include its own cached outputs automatically)
            var result = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);

            // Collect completed outputs and busy status info from other pipes
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

            // Build response: completed outputs + busyStatusInfo + result
            var response = new StringBuilder();
            if (completedOutputs.Length > 0)
            {
                response.AppendLine(completedOutputs);
                response.AppendLine();
            }
            if (busyStatusInfo.Length > 0)
            {
                response.Append(busyStatusInfo);
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
    [Description(@"Execute PowerShell cmdlets and CLI tools (e.g., git) in persistent console. Session persists: modules, variables, functions, authentication stay active—no re-authentication. Install any modules and learn them via Get-Help. Commands visible in history for user learning.

⚠️ CRITICAL - Variable Scope:
Local variables are NOT preserved between invoke_expression calls. Use $script: or $global: scope to share variables across calls.

⚠️ CRITICAL - Verbose/Debug Output:
Verbose and Debug streams are NOT visible to you. If you need verbose/debug information, ask the user to copy it from the console and share it with you.

⚠ CRITICAL - File Operations:
For user-provided paths (like C:\), use PowerShell.MCP tools ONLY. Server-side tools (such as str_replace) cannot access them.
When calling invoke_expression for file operations, ALWAYS use these cmdlets. NEVER use Set-Content, Get-Content, or Out-File:

• Show-TextFile [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Encoding <string>]
  Displays file contents with line numbers. Filter by line range and/or matching text (literal or regex).
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

⚡ Markdown/Mermaid/KaTeX Viewer (Windows, requires MarkdownViewer module):
• Show-MarkdownViewer <content|path> [-Title <string>] - Display Markdown with Mermaid diagrams (auto-refresh on save).

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
        [Description("The PowerShell command or pipeline to execute. When execute_immediately=true (immediate execution), both single-line and multi-line commands are supported, including if statements, loops, functions, and try-catch blocks. When execute_immediately=false (insertion mode), only single-line commands are supported - use semicolons to combine multiple statements into a single line.")]
        string pipeline,
        [Description("If true, executes the command immediately and returns the result. If false, inserts the command into the console for manual execution. (Windows only - Linux/macOS always execute immediately)")]
        bool execute_immediately = true,
        CancellationToken cancellationToken = default)
    {
        // Linux/macOS does not support execute_immediately=false (insert mode)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !execute_immediately)
        {
            return "execute_immediately=false (insert mode) is not supported on Linux/macOS. Please use execute_immediately=true or omit the parameter.";
        }

        // Find a ready pipe
        var (readyPipeName, consoleSwitched) = await FindReadyPipeAsync(powerShellService, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start
            Console.Error.WriteLine("[INFO] No ready PowerShell console found, auto-starting...");
            var startResult = await StartPowershellConsoleInternal(powerShellService, null, cancellationToken);

            // Collect completed outputs and busy status (after console start, using new pipe as exclude)
            var sessionManager = ConsoleSessionManager.Instance;
            var newPipeName = sessionManager.ActivePipeName;
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, newPipeName, cancellationToken);

            // Build response: completed outputs + busy status + modified start message
            var response = new StringBuilder();
            if (completedOutputs.Length > 0)
            {
                response.AppendLine(completedOutputs);
                response.AppendLine();
            }
            if (busyStatusInfo.Length > 0)
            {
                response.Append(busyStatusInfo);
                response.AppendLine();
            }
            response.Append(startResult.Replace(
                "PowerShell console started successfully with PowerShell.MCP module imported.",
                "PowerShell console started with PowerShell.MCP module imported. Pipeline NOT executed - verify location and re-execute."));
            return response.ToString();
        }

        // Console switched - get location (DLL will automatically include its own cached outputs)
        if (consoleSwitched)
        {
            var locationResult = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);
            var (completedOutputs, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

            // Build response: completedOutputs + locationResult (includes pipe's own cache) + busy status + message
            var response = new StringBuilder();
            if (completedOutputs.Length > 0)
            {
                response.AppendLine(completedOutputs);
                response.AppendLine();
            }
            response.AppendLine(locationResult);
            response.AppendLine();
            if (busyStatusInfo.Length > 0)
            {
                response.Append(busyStatusInfo);
                response.AppendLine();
            }
            response.Append("Console switched. Pipeline NOT executed - verify location and re-execute.");
            return response.ToString();
        }

        // Execute the command
        try
        {
            var result = await powerShellService.InvokeExpressionToPipeAsync(readyPipeName, pipeline, execute_immediately, cancellationToken);

            // Check if command is still running (timeout case)
            const string stillRunningMessage = "Command is still running.";
            if (result.StartsWith(stillRunningMessage, StringComparison.Ordinal))
            {
                // Mark this pipe as busy for later cache collection
                var sessionManager = ConsoleSessionManager.Instance;
                sessionManager.MarkPipeBusy(readyPipeName, $"⧗ | Pipeline: {TruncatePipeline(pipeline)} | Waiting for completion");
                return result;
            }

            // Normal case - collect other pipes' cached outputs and busy status
            var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, readyPipeName, cancellationToken);

            // Build response: completedOutput + busyStatusInfo + result
            var response = new StringBuilder();
            if (completedOutput.Length > 0)
            {
                response.Append(completedOutput);
            }
            if (busyStatusInfo.Length > 0)
            {
                response.Append(busyStatusInfo);
            }
            if (response.Length > 0)
            {
                response.AppendLine();
                response.Append(result);
                return response.ToString();
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] InvokeExpression failed: {ex.Message}");
            return $"Command execution failed: {ex.Message}\n\nPlease try again. A new console will be started automatically if needed.";
        }
    }

    [McpServerTool]
    [Description("Wait for busy console(s) to complete and retrieve cached results. Use this after receiving 'Command is still running' response instead of executing Start-Sleep (which would open a new console).")]
    public static async Task<string> WaitForCompletion(
        IPowerShellService powerShellService,
        [Description("Maximum seconds to wait for completion (1-170, default: 30). Returns early if a console completes.")]
        int timeout_seconds = 30,
        CancellationToken cancellationToken = default)
    {
        var sessionManager = ConsoleSessionManager.Instance;

        // Clamp timeout to reasonable range
        timeout_seconds = Math.Clamp(timeout_seconds, 1, 170);

        const int pollIntervalMs = 1000; // Check every second
        var endTime = DateTime.UtcNow.AddSeconds(timeout_seconds);

        // First pass: enumerate all pipes and find busy/completed ones
        var busyPipes = new List<string>();
        foreach (var pipeName in sessionManager.EnumeratePipes())
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null) continue;

            if (status.Status == "completed")
            {
                // Consume output directly from this completed pipe
                var output = await powerShellService.ConsumeOutputFromPipeAsync(pipeName, cancellationToken);
                if (!string.IsNullOrEmpty(output))
                {
                    // Check remaining pipes for busy status
                    var remainingBusyInfo = new StringBuilder();
                    foreach (var otherPipe in sessionManager.EnumeratePipes().Where(p => p != pipeName))
                    {
                        var otherStatus = await powerShellService.GetStatusFromPipeAsync(otherPipe, cancellationToken);
                        if (otherStatus?.Status == "busy")
                        {
                            remainingBusyInfo.AppendLine(FormatBusyStatus(otherStatus));
                            sessionManager.MarkPipeBusy(otherPipe, FormatBusyStatus(otherStatus));
                        }
                    }
                    
                    if (remainingBusyInfo.Length > 0)
                    {
                        return $"{output}\n\n{remainingBusyInfo.ToString().TrimEnd()}";
                    }
                    return output;
                }
            }

            if (status.Status == "busy")
            {
                busyPipes.Add(pipeName);
                var busyInfo = FormatBusyStatus(status);
                sessionManager.MarkPipeBusy(pipeName, busyInfo);
            }
        }

        // No busy consoles - nothing to wait for
        if (busyPipes.Count == 0)
        {
            return "No busy consoles to wait for.";
        }


        // Poll only the busy pipes until timeout or completion
        while (DateTime.UtcNow < endTime)
        {
            // Wait before next poll
            var remainingMs = (int)(endTime - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0) break;
            await Task.Delay(Math.Min(pollIntervalMs, remainingMs), cancellationToken);

            foreach (var pipeName in busyPipes)
            {
                var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

                if (status == null) continue;

                if (status.Status == "completed")
                {
                    // Consume output directly from this completed pipe
                    var output = await powerShellService.ConsumeOutputFromPipeAsync(pipeName, cancellationToken);
                    sessionManager.RemoveFromBusy(pipeName);
                    if (!string.IsNullOrEmpty(output))
                    {
                        // Check remaining busy pipes for status
                        var remainingBusyInfo = new StringBuilder();
                        foreach (var otherPipe in busyPipes.Where(p => p != pipeName))
                        {
                            var otherStatus = await powerShellService.GetStatusFromPipeAsync(otherPipe, cancellationToken);
                            if (otherStatus?.Status == "busy")
                            {
                                remainingBusyInfo.AppendLine(FormatBusyStatus(otherStatus));
                            }
                        }
                        
                        if (remainingBusyInfo.Length > 0)
                        {
                            return $"{output}\n\n{remainingBusyInfo.ToString().TrimEnd()}";
                        }
                        return output;
                    }
                }

                if (status.Status == "standby")
                {
                    // Console returned to standby without caching (unexpected)
                    sessionManager.RemoveFromBusy(pipeName);
                }
            }
        }

        // Timeout - collect final status
        var (finalCompletedOutput, finalBusyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, null, cancellationToken);
        return BuildWaitResponse(finalCompletedOutput, finalBusyStatusInfo);
    }

    private static string BuildWaitResponse(string completedOutput, string busyStatusInfo)
    {
        var response = new StringBuilder();
        if (completedOutput.Length > 0)
        {
            response.Append(completedOutput);
        }
        if (busyStatusInfo.Length > 0)
        {
            response.Append(busyStatusInfo);
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
        CancellationToken cancellationToken = default)
    {
        var startResult = await StartPowershellConsoleInternal(powerShellService, banner, cancellationToken);

        // Collect busy status from Proxy side
        var sessionManager = ConsoleSessionManager.Instance;
        var newPipeName = sessionManager.ActivePipeName;
        var (completedOutput, busyStatusInfo) = await CollectAllCachedOutputsAsync(powerShellService, newPipeName, cancellationToken);

        // Build response: completed output + busy status + start message
        var response = new StringBuilder();
        if (completedOutput.Length > 0)
        {
            response.Append(completedOutput);
        }
        if (busyStatusInfo.Length > 0)
        {
            response.Append(busyStatusInfo);
            response.AppendLine();
        }
        response.Append(startResult);
        return response.ToString();
    }

    /// <summary>
    /// Internal method to start PowerShell console
    /// </summary>
    private static async Task<string> StartPowershellConsoleInternal(
        IPowerShellService powerShellService,
        string? banner,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionManager = ConsoleSessionManager.Instance;

            // Check existing pipes and mark busy ones before starting new console
            foreach (var existingPipe in sessionManager.EnumeratePipes())
            {
                var status = await powerShellService.GetStatusFromPipeAsync(existingPipe, cancellationToken);
                if (status?.Status == "busy")
                {
                    var busyInfo = FormatBusyStatus(status);
                    sessionManager.MarkPipeBusy(existingPipe, busyInfo);
                }
            }

            Console.Error.WriteLine("[INFO] Starting PowerShell console...");
            // Start new console
            var (success, pipeName) = await PowerShellProcessManager.StartPowerShellWithModuleAndPipeNameAsync(banner);

            if (!success)
            {
                return "Failed to start PowerShell console or establish Named Pipe connection.\n\nPossible causes:\n- No supported terminal emulator found (gnome-terminal, konsole, xfce4-terminal, xterm, etc.)\n- Terminal emulator failed to start\n- PowerShell.MCP module failed to initialize\n\nPlease ensure a terminal emulator is installed and try again.";
            }

            // Register the new console
            sessionManager.SetActivePipeName(pipeName);

            Console.Error.WriteLine($"[INFO] PowerShell console started successfully (pipe={pipeName}), getting current location...");

            // Get current location from new console
            var newLocationResult = await powerShellService.GetCurrentLocationFromPipeAsync(pipeName, cancellationToken);

            Console.Error.WriteLine("[INFO] PowerShell console startup completed");

            // Build response (busy info collection is done by caller)
            var response = new StringBuilder();
            response.AppendLine("PowerShell console started successfully with PowerShell.MCP module imported.");
            response.AppendLine();
            response.Append(newLocationResult);
            return response.ToString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] StartPowershellConsole failed: {ex.Message}");
            return $"Failed to start PowerShell console: {ex.Message}\n\nPlease check if a terminal emulator is available and try again.";
        }
    }
}
