using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using PowerShell.MCP.Proxy.Services;
using System.Runtime.InteropServices;

namespace PowerShell.MCP.Proxy.Tools;

[McpServerToolType]
public class PowerShellTools
{
    // Error message constant definitions
    private const string ERROR_CONSOLE_NOT_RUNNING = "The PowerShell 7 console is not running.";
    private const string ERROR_MODULE_NOT_IMPORTED = "PowerShell.MCP module is not imported in existing pwsh.";
    
    [McpServerTool]
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns current_location and other_drive_locations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.")]
    public static async Task<string> GetCurrentLocation(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken = default)
    {
        var result = await powerShellService.GetCurrentLocationAsync(cancellationToken);
        
        // Check if error message (when PowerShell is not running)
        // Use StartsWith to check only the beginning of error message
        if (result.StartsWith(ERROR_CONSOLE_NOT_RUNNING, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("[INFO] PowerShell console not running, auto-starting...");
            
            // Automatically execute start_powershell_console
            return await StartPowershellConsole(powerShellService, cancellationToken: cancellationToken);
        }
        
        // Do not auto-start if module is not imported (respect user choice)
        // Return normal result or other error message as-is
        return result;
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
        // because PSReadLine is not available
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !execute_immediately)
        {
            return "execute_immediately=false (insert mode) is not supported on Linux/macOS. Please use execute_immediately=true or omit the parameter.";
        }

        var sessionManager = ConsoleSessionManager.Instance;
        
        // First check if active console is busy using get_status (lightweight, fixed format response)
        var (isBusy, busyStatusResponse) = await powerShellService.GetStatusAsync(cancellationToken);
        
        if (isBusy)
        {
            Console.Error.WriteLine("[INFO] Current console is busy, trying standby consoles...");
            var busyPipeName = sessionManager.ActivePipeName;
            
            // Mark the console as busy
            if (busyPipeName != null)
            {
                sessionManager.SetConsoleBusy(busyPipeName, busyStatusResponse);
            }
            
            // Try to find a standby console (not the active one)
            var allPipes = sessionManager.GetAllPipeNames();
            foreach (var pipeName in allPipes)
            {
                if (pipeName == busyPipeName) continue; // Skip the busy one
                
                try
                {
                    // Try to get status from this console
                    var (standbyIsBusy, _) = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);
                    
                    if (!standbyIsBusy)
                    {
                        // Found a ready standby console - switch to it
                        sessionManager.RegisterConsole(pipeName, setAsActive: true);
                        Console.Error.WriteLine($"[INFO] Switched to standby console: {pipeName}");
                        
                        // Get location info from the standby console
                        var locationResult = await powerShellService.GetCurrentLocationAsync(cancellationToken);
                        
                        return $"{busyStatusResponse}\n\nSwitched to a standby console. Verify current location and re-execute the pipeline if appropriate.\n\n{locationResult}";
                    }
                }
                catch
                {
                    // Console not available, continue checking others
                    sessionManager.UnregisterConsole(pipeName);
                }
            }
            
            // No standby console available, start a new one
            Console.Error.WriteLine("[INFO] No standby console available, starting new console...");
            var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);
            
            return $"{busyStatusResponse}\n\nStarted a new console. Verify current location and re-execute the pipeline if appropriate.\n\n{startResult}";
        }

        // Console is ready, execute the command
        var result = await powerShellService.InvokeExpressionAsync(pipeline, execute_immediately, cancellationToken);
        
        // Check if error message (when PowerShell is not running)
        if (result.StartsWith(ERROR_CONSOLE_NOT_RUNNING, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("[INFO] PowerShell console not running, auto-starting...");
            
            // Auto-start console (location info will also be retrieved)
            var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);
            
            // Do not execute pipeline. Prompt AI for confirmation (important info first)
            return $"PowerShell console was not running. It has been automatically started, but the requested pipeline was NOT executed. Please verify the current location and re-execute the command if appropriate.\n\n{startResult}";
        }
        
        // Check if pwsh is running but module is not imported
        if (result == ERROR_MODULE_NOT_IMPORTED)
        {
            Console.Error.WriteLine("[INFO] PowerShell.MCP module not imported, starting new console...");
            
            // Auto-start new console with module imported
            var startResult = await StartPowershellConsoleInternal(powerShellService, forceNew: true, banner: null, cancellationToken);
            
            // Do not execute pipeline. Prompt AI for confirmation
            return $"[LLM] Pipeline NOT executed - verify location and re-execute.\n\n[Inform user] Existing PowerShell window did not have PowerShell.MCP module imported, so a new console was opened. Original pwsh remains unchanged.\n\n{startResult}";
        }
        
        // Check for busy consoles and append their status to the result
        var busyStatusBuilder = new StringBuilder();
        var busyConsoles = sessionManager.GetBusyConsoles();
        
        foreach (var (pipeName, _) in busyConsoles)
        {
            try
            {
                var (stillBusy, statusResponse) = await powerShellService.GetStatusFromPipeAsync(pipeName, cancellationToken);
                
                if (stillBusy)
                {
                    // Still busy, append status
                    busyStatusBuilder.AppendLine(statusResponse);
                }
                else
                {
                    // No longer busy, clear from cache
                    sessionManager.ClearConsoleBusy(pipeName);
                }
            }
            catch
            {
                // Console no longer available
                sessionManager.ClearConsoleBusy(pipeName);
                sessionManager.UnregisterConsole(pipeName);
            }
        }
        
        // Append busy status if any
        if (busyStatusBuilder.Length > 0)
        {
            return $"{busyStatusBuilder}\n{result}";
        }
        
        // Return normal result
        return result;
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
    /// <param name="forceNew">If true, force start a new console even if one is available (used when current console is busy)</param>
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
                // Cleanup dead consoles first
                sessionManager.CleanupDeadConsoles();
                
                if (PowerShellProcessManager.IsPowerShellProcessRunning())
                {
                    // Check status using get_status (lightweight, no risk of text content confusion)
                    var (isBusy, statusResponse) = await powerShellService.GetStatusAsync(cancellationToken);
                    
                    if (string.IsNullOrEmpty(statusResponse))
                    {
                        // No response - module not imported
                        return @"A pwsh instance is already running and the existing session will continue to be used. However, the PowerShell.MCP module may not be imported in the existing session.
Please guide the user to run 'Import-Module PowerShell.MCP' to import the module.
Note LLM cannot import the module automatically.";
                    }
                    else if (isBusy)
                    {
                        return @"Since pwsh with the PowerShell.MCP module imported is already running, the existing session will continue to be used.
However, the existing pwsh is currently executing a pipeline from the previous invoke_expression call, so it cannot accept a new command.
Please guide the user to either wait for the command to complete or close the existing PowerShell console.
Note that commands executed via invoke_expression cannot be cancelled with Ctrl+C and must complete naturally.";
                    }
                    else
                    {
                        var msg = @"Since pwsh with the PowerShell.MCP module imported is already running, the existing session will continue to be used.

";
                        return msg + await powerShellService.GetCurrentLocationAsync(cancellationToken);
                    }
                }
            }

            // Start new console
            var (success, pid) = await PowerShellProcessManager.StartPowerShellWithModuleAndPidAsync(banner);
            
            if (!success)
            {
                return "Failed to start PowerShell console or establish Named Pipe connection.\n\nPossible causes:\n- No supported terminal emulator found (gnome-terminal, konsole, xfce4-terminal, xterm, etc.)\n- Terminal emulator failed to start\n- PowerShell.MCP module failed to initialize\n\nPlease ensure a terminal emulator is installed and try again.";
            }
            
            // Console is registered via RegistrationPipeServer with setAsActive=true
            // This works for all platforms (Windows/Linux/macOS)
            Console.Error.WriteLine($"[INFO] PowerShell console started successfully (pipe={sessionManager.ActivePipeName}), getting current location...");
            
            // Get current location from new console
            var locationResult = await powerShellService.GetCurrentLocationAsync(cancellationToken);
            
            Console.Error.WriteLine("[INFO] PowerShell console startup completed");
            
            return $"PowerShell console started successfully with PowerShell.MCP module imported.\n\n{locationResult}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] StartPowershellConsole failed: {ex.Message}");
            return $"Failed to start PowerShell console: {ex.Message}\n\nPlease check if a terminal emulator is available and try again.";
        }
    }
}
