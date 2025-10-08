using ModelContextProtocol.Server;
using System.ComponentModel;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Proxy.Models;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Tools;

[McpServerToolType]
public static class PowerShellTools
{
    [McpServerTool]
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns current_location and other_drive_locations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.")]
    public static async Task<string> GetCurrentLocation(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[DEBUG] GetCurrentLocation static method called!");
        
        // PowerShellService からの例外はそのまま通す（フォールバック判定削除）
        return await powerShellService.GetCurrentLocationAsync(cancellationToken);
    }

    [McpServerTool]
    [Description(@"Execute PowerShell commands in the PowerShell console. Supports both immediate execution and command insertion modes.

⚠️ IMPORTANT - Verbose/Debug Output Streams:
Verbose and Debug streams are NOT captured and NOT visible to the LLM. These streams display only in the user's console. If you need verbose/debug information from a command, ask the user to copy it from the console and share it with you.

⚠️ IMPORTANT - Variable Scope:
Local variables are NOT preserved between invoke_expression calls. Use $script: or $global: scope to share variables across calls.

⚠️ CRITICAL - Text File Operations:
NEVER use Get-Content or Set-Content for text file operations. This module includes LLM-optimized cmdlets that preserve file metadata (encoding, newlines) and provide better error handling:

• Show-TextFile [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Encoding <string>]
  Displays file contents with line numbers. Filter by line range and/or matching text (literal or regex).

• Test-TextFileContains [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Encoding <string>]
  Tests if file contains matching text (literal or regex) within specified line range. Returns Boolean.

• Add-LinesToFile [-Path] <string[]> [-LineNumber <int>] [-AtEnd] [-Content] <Object[]> [-Encoding <string>] [-Backup] [-WhatIf]
  Inserts lines at specified position or appends to end or creates new file. Accepts arrays for multiple lines.

• Update-LinesInFile [-Path] <string[]> [[-LineRange] <int[]>] [-Content <Object[]>] [-Encoding <string>] [-Backup] [-WhatIf]
  Replaces specified line range with new content or creates new file. Omit content to delete lines.

• Update-MatchInFile [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <string>] [-Replacement <string>] [-Encoding <string>] [-Backup] [-WhatIf]
  Replaces matching text (literal or regex) within optional line range.

• Remove-LinesFromFile [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Encoding <string>] [-Backup] [-WhatIf]
  Removes lines matching text (literal or regex) within optional range.

IMPORTANT:
- All cmdlets support both -Path (wildcards allowed) and -LiteralPath (exact path, no wildcard expansion)
- Content parameter in Add-LinesToFile accepts Object[] - you can pass string arrays for multiple lines
- Always check parameter types before using - arrays can be passed directly without loops

USE BUILT-IN PARAMETERS:
  ✅ CORRECT: Show-TextFile file.txt -LineRange 10,20
  ❌ WRONG: Show-TextFile file.txt | Select-Object -Skip 9 -First 11

For detailed examples: Get-Help <cmdlet-name> -Examples")]
    public static async Task<string> InvokeExpression(
        IPowerShellService powerShellService,
        [Description("The PowerShell command or pipeline to execute. When execute_immediately=true (immediate execution), both single-line and multi-line commands are supported, including if statements, loops, functions, and try-catch blocks. When execute_immediately=false (insertion mode), only single-line commands are supported - use semicolons to combine multiple statements into a single line.")]
        string pipeline,
        [Description("If true, executes the command immediately and returns the result. If false, inserts the command into the console for manual execution.")]
        bool execute_immediately = true,
        CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine($"[DEBUG] InvokeExpression static method called! Pipeline: {pipeline}, ExecuteImmediately: {execute_immediately}");
        
        // PowerShellService からの例外はそのまま通す
        return await powerShellService.InvokeExpressionAsync(pipeline, execute_immediately, cancellationToken);
    }

    [McpServerTool]
    [Description("Launch a new PowerShell console window with PowerShell.MCP module imported. This tool should only be executed when explicitly requested by the user or when other tool executions fail.")]
    public static async Task<string> StartPowershellConsole(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[DEBUG] StartPowershellConsole static method called!");

        try
        {
            Console.Error.WriteLine("[INFO] Starting new PowerShell console with PowerShell.MCP module...");

            // 1. プロキシ側で直接 pwsh.exe を起動（Named Pipe 経由ではない）

            // すでに pwsh.exe が起動済みであれば、操作に失敗する
            if (PowerShellProcessManager.IsPowerShellProcessRunning())
            {
                var loc = await GetCurrentLocation(powerShellService, cancellationToken);
                if (loc.Contains("PowerShell.MCP module is not imported"))
                {
                    // The existing pwsh.exe is available but PowerShell.MCP module is not imported
                    return @"A pwsh.exe instance is already running and the existing session will continue to be used. However, the PowerShell.MCP module may not be imported in the existing session.
Please guide the user to run 'Import-Module PowerShell.MCP' to import the module.
Note LLM cannot import the module automatically.";
                }
                else if (loc.Contains("previous pipeline is running"))
                {
                    // The existing pwsh.exe is still executing the previous command
                    return @"Since pwsh.exe with the PowerShell.MCP module imported is already running, the existing session will continue to be used.
However, the existing pwsh.exe is currently executing a pipeline from the previous invoke_expression call, so it cannot accept a new command.
Please guide the user to either wait for the command to complete or close the existing PowerShell console.
Note that commands executed via invoke_expression cannot be cancelled with Ctrl+C and must complete naturally.";
                }
                else
                {
                    // The existing pwsh.exe is available with PowerShell.MCP module imported
                    var msg = @"Since pwsh.exe with the PowerShell.MCP module imported is already running, the existing session will continue to be used.

";
                    return msg + await powerShellService.GetCurrentLocationAsync(cancellationToken);
                }
            }

            bool success = await PowerShellProcessManager.StartPowerShellWithModuleAsync();
            
            if (!success)
            {
                throw new InvalidOperationException("Failed to start PowerShell console or establish Named Pipe connection");
            }
            
            Console.Error.WriteLine("[INFO] PowerShell console started successfully, getting current location...");
            
            // 2. 自動で get_current_location を実行
            var locationResult = await powerShellService.GetCurrentLocationAsync(cancellationToken);
            
            Console.Error.WriteLine("[INFO] PowerShell console startup completed");
            
            // 3. 結果を MCP クライアントに返す
            return $"PowerShell console started successfully with PowerShell.MCP module imported.\n\n{locationResult}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] StartPowershellConsole failed: {ex.Message}");
            throw new InvalidOperationException($"Failed to start PowerShell console: {ex.Message}");
        }
    }
}
