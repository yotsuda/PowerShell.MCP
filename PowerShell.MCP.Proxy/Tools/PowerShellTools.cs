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
    /// Finds a ready pipe using cached state, with minimal queries.
    /// Returns (readyPipeName, statusInfo for busy/completed pipes)
    /// </summary>
    private static async Task<(string? readyPipeName, StringBuilder statusInfo, bool consoleSwitched)> FindReadyPipeAsync(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken)
    {
        var sessionManager = ConsoleSessionManager.Instance;
        var statusInfo = new StringBuilder();
        var readyCandidates = new List<string>();
        var originalActivePipe = sessionManager.ActivePipeName;
        // Step 1: Check known busy pipes FIRST (they might be standby now)
        // Collect ready candidates before checking ActivePipeName
        var busyPipes = sessionManager.GetBusyPipes();
        foreach (var (pipeName, _) in busyPipes)
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                // Dead pipe
                sessionManager.ClearDeadPipe(pipeName);
            }
            else if (status.Status == "standby")
            {
                // Now ready - add to candidates
                sessionManager.RemoveFromBusy(pipeName);
                readyCandidates.Add(pipeName);
            }
            else if (status.Status == "completed")
            {
                // Has unreported output - add to candidates with output collected
                AppendCompletedStatus(statusInfo, status);
                sessionManager.RemoveFromBusy(pipeName);
                readyCandidates.Add(pipeName);
            }
            else // still busy
            {
                var busyInfo = FormatBusyStatus(status);
                sessionManager.MarkPipeBusy(pipeName, busyInfo);
                statusInfo.AppendLine(busyInfo);
            }
        }

        // Step 2: Check ActivePipeName (if not already checked in Step 1)
        var activePipe = sessionManager.ActivePipeName;
        if (activePipe != null && !busyPipes.ContainsKey(activePipe))
        {
            var status = await powerShellService.GetStatusFromPipeAsync(activePipe, cancellationToken);

            if (status == null)
            {
                // Dead pipe - try candidates
                sessionManager.ClearDeadPipe(activePipe);
            }
            else if (status.Status == "standby")
            {
                // Ready to use
                return (activePipe, statusInfo, false);
            }
            else if (status.Status == "completed")
            {
                // Has unreported output - use this pipe
                AppendCompletedStatus(statusInfo, status);
                return (activePipe, statusInfo, false);
            }
            else // busy
            {
                // Mark as busy - use candidates if available
                var busyInfo = FormatBusyStatus(status);
                sessionManager.MarkPipeBusy(activePipe, busyInfo);
                statusInfo.AppendLine(busyInfo);
            }
        }

        // Step 3: Use ready candidate if available
        if (readyCandidates.Count > 0)
        {
            var candidatePipe = readyCandidates[0];
            sessionManager.SetActivePipeName(candidatePipe);
            return (candidatePipe, statusInfo, originalActivePipe != null && candidatePipe != originalActivePipe);
        }

        // Step 4: Discover new pipes (last resort) - lazy evaluation stops at first standby
        var knownPipes = new HashSet<string>(busyPipes.Keys);
        if (activePipe != null) knownPipes.Add(activePipe);

        var unknownPipes = sessionManager.EnumeratePipes()
            .Where(p => !knownPipes.Contains(p));

        foreach (var pipeName in unknownPipes)
        {
            var status = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);

            if (status == null)
            {
                // Dead pipe - skip
                continue;
            }

            if (status.Status == "standby")
            {
                sessionManager.SetActivePipeName(pipeName);
                return (pipeName, statusInfo, originalActivePipe != null && pipeName != originalActivePipe);
            }

            if (status.Status == "completed")
            {
                AppendCompletedStatus(statusInfo, status);
                sessionManager.SetActivePipeName(pipeName);
                return (pipeName, statusInfo, originalActivePipe != null && pipeName != originalActivePipe);
            }

            // busy
            var busyInfo = FormatBusyStatus(status);
            sessionManager.MarkPipeBusy(pipeName, busyInfo);
            statusInfo.AppendLine(busyInfo);
        }

        // No ready pipe found
        return (null, statusInfo, false);
    }

    private static string FormatBusyStatus(GetStatusResponse status)
    {
        var truncatedPipeline = TruncatePipeline(status.Pipeline ?? "");
        return $"⧗ | pwsh PID: {status.Pid} | Status: Busy | Pipeline: {truncatedPipeline} | Duration: {status.Duration:F2}s";
    }

    private static void AppendCompletedStatus(StringBuilder statusInfo, GetStatusResponse status)
    {
        // Output already contains the summary line, just append it directly
        statusInfo.AppendLine(status.Output);
        statusInfo.AppendLine();
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

    private static string BuildNotExecutedResponse(StringBuilder statusInfo, string message, string jsonContent)
    {
        var response = new StringBuilder();
        if (statusInfo.Length > 0)
        {
            response.AppendLine(statusInfo.ToString());
        }
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
        var sessionManager = ConsoleSessionManager.Instance;

        // Find a ready pipe using cached state
        var (readyPipeName, statusInfo, _) = await FindReadyPipeAsync(powerShellService, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start
            Console.Error.WriteLine("[INFO] No ready PowerShell console found, auto-starting...");
            var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);

            if (statusInfo.Length > 0)
            {
                return $"{statusInfo}\n{startResult}";
            }
            return startResult;
        }

        try
        {
            var result = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);

            if (statusInfo.Length > 0)
            {
                return $"{statusInfo}\n{result}";
            }
            return result;
        }
        catch
        {
            // Failed - try to start new console
            Console.Error.WriteLine("[INFO] Failed to get location, auto-starting new console...");
            return await StartPowershellConsole(powerShellService, cancellationToken: cancellationToken);
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

        // Find a ready pipe using cached state
        var (readyPipeName, statusInfo, consoleSwitched) = await FindReadyPipeAsync(powerShellService, cancellationToken);

        if (readyPipeName == null)
        {
            // No ready pipe - auto-start
            Console.Error.WriteLine("[INFO] No ready PowerShell console found, auto-starting...");
            var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);

            // Extract JSON from startResult (skip first paragraph)
            var separatorIndex = startResult.IndexOf("\n\n");
            var json = separatorIndex > 0 ? startResult[(separatorIndex + 2)..] : startResult;
            return BuildNotExecutedResponse(statusInfo,
                "PowerShell console started with PowerShell.MCP module imported. Pipeline NOT executed - verify location and re-execute.",
                json);
        }

        // Console switched - don't execute pipeline, return location info instead
        if (consoleSwitched)
        {
            var locationResult = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);
            return BuildNotExecutedResponse(statusInfo,
                "Console switched. Pipeline NOT executed - verify location and re-execute.",
                locationResult);
        }

        // Execute the command
        try
        {
            var result = await powerShellService.InvokeExpressionToPipeAsync(readyPipeName, pipeline, execute_immediately, cancellationToken);

            // Check for error messages
            if (result.StartsWith(ERROR_CONSOLE_NOT_RUNNING, StringComparison.Ordinal))
            {
                Console.Error.WriteLine("[INFO] PowerShell console not running, auto-starting...");
                var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);
                return $"PowerShell console was not running. It has been automatically started, but the requested pipeline was NOT executed. Please verify the current location and re-execute the command if appropriate.\n\n{startResult}";
            }

            if (result == ConsoleSessionManager.ErrorModuleNotImported)
            {
                Console.Error.WriteLine("[INFO] PowerShell.MCP module not imported, starting new console...");
                var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);
                return $"[LLM] Pipeline NOT executed - verify location and re-execute.\n\n[Inform user] Existing PowerShell window did not have PowerShell.MCP module imported, so a new console was opened. Original pwsh remains unchanged.\n\n{startResult}";
            }

            if (statusInfo.Length > 0)
            {
                return $"{statusInfo}\n{result}";
            }
            return result;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] InvokeExpression failed: {ex.Message}");

            // Try to start new console
            var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);
            return $"Command execution failed. A new console has been started.\n\n{startResult}\n\n[LLM] Pipeline NOT executed - verify location and re-execute.";
        }
    }

    [McpServerTool]
    [Description("Launch a new PowerShell console window with PowerShell.MCP module imported. This tool should only be executed when explicitly requested by the user or when other tool executions fail.")]
    public static async Task<string> StartPowershellConsole(
        IPowerShellService powerShellService,
        [Description("Message displayed at console startup (e.g. greeting, joke, fun fact). Be creative and make the user smile!")]
        string? banner = null,
        CancellationToken cancellationToken = default)
    {
        return await StartPowershellConsoleInternal(powerShellService, forceNew: false, banner, cancellationToken);
    }

    /// <summary>
    /// Internal method to start PowerShell console
    /// </summary>
    private static async Task<string> StartPowershellConsoleInternal(
        IPowerShellService powerShellService,
        bool forceNew,
        string? banner,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionManager = ConsoleSessionManager.Instance;

            Console.Error.WriteLine($"[INFO] Starting PowerShell console (forceNew={forceNew})...");

            // If not forcing new console, check for existing available console
            if (!forceNew)
            {
                var (readyPipeName, statusInfo, _) = await FindReadyPipeAsync(powerShellService, cancellationToken);

                if (readyPipeName != null)
                {
                    try
                    {
                        var locationResult = await powerShellService.GetCurrentLocationFromPipeAsync(readyPipeName, cancellationToken);
                        var msg = "Since pwsh with the PowerShell.MCP module imported is already running, the existing session will continue to be used.\n\n";

                        if (statusInfo.Length > 0)
                        {
                            return $"{statusInfo}\n{msg}{locationResult}";
                        }
                        return msg + locationResult;
                    }
                    catch
                    {
                        // Fall through to start new console
                    }
                }
            }

            // Start new console
            var (success, pid) = await PowerShellProcessManager.StartPowerShellWithModuleAndPidAsync(banner);

            if (!success)
            {
                return "Failed to start PowerShell console or establish Named Pipe connection.\n\nPossible causes:\n- No supported terminal emulator found (gnome-terminal, konsole, xfce4-terminal, xterm, etc.)\n- Terminal emulator failed to start\n- PowerShell.MCP module failed to initialize\n\nPlease ensure a terminal emulator is installed and try again.";
            }

            // Register the new console with PID-based pipe name
            var pipeName = ConsoleSessionManager.GetPipeNameForPid(pid);
            sessionManager.SetActivePipeName(pipeName);

            Console.Error.WriteLine($"[INFO] PowerShell console started successfully (pipe={pipeName}), getting current location...");

            // Get current location from new console
            var newLocationResult = await powerShellService.GetCurrentLocationFromPipeAsync(pipeName, cancellationToken);

            Console.Error.WriteLine("[INFO] PowerShell console startup completed");

            return $"PowerShell console started successfully with PowerShell.MCP module imported.\n\n{newLocationResult}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] StartPowershellConsole failed: {ex.Message}");
            return $"Failed to start PowerShell console: {ex.Message}\n\nPlease check if a terminal emulator is available and try again.";
        }
    }
}
