using ModelContextProtocol.Server;
using System.ComponentModel;
using PowerShell.MCP.Proxy.Services;

namespace PowerShell.MCP.Proxy.Tools;

[McpServerToolType]
public static class PowerShellTools
{
    // エラーメッセージの定数定義
    private const string ERROR_CONSOLE_NOT_RUNNING = "The PowerShell 7 console is not running.";
    
    [McpServerTool]
    [Description("Retrieves the current location and all available drives (providers) from the PowerShell session. Returns current_location and other_drive_locations array. Call this when you need to understand the current PowerShell context, as users may change location during the session. When executing multiple invoke_expression commands in succession, calling once at the beginning is sufficient.")]
    public static async Task<string> GetCurrentLocation(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken = default)
    {
        var result = await powerShellService.GetCurrentLocationAsync(cancellationToken);
        
        // エラーメッセージかどうかをチェック（PowerShell が起動していない場合）
        // StartsWith を使用して、エラーメッセージの先頭部分のみをチェック
        if (result.StartsWith(ERROR_CONSOLE_NOT_RUNNING, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("[INFO] PowerShell console not running, auto-starting...");
            
            // 自動的に start_powershell_console を実行
            return await StartPowershellConsole(powerShellService, cancellationToken);
        }
        
        // モジュールがインポートされていない場合は自動起動しない（ユーザーの選択を尊重）
        // 通常の結果またはその他のエラーメッセージをそのまま返す
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
  Replaces specified line range with new content or creates new file. Use -Content @() to delete lines. Accepts pipeline input for Content.

• Update-MatchInFile [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Replacement <string>] [-Encoding <string>] [-Backup]
  Replaces matching text (literal or regex) within optional line range.
  ⚠️ STRONGLY RECOMMENDED: Run with -WhatIf first to preview changes. Regex mistakes can corrupt files.

• Remove-LinesFromFile [-Path] <string[]> [-LineRange <int[]>] [-Contains <string>] [-Pattern <regex>] [-Encoding <string>] [-Backup]
  Removes lines matching text (literal or regex) within optional range.

Note: All cmdlets support -LiteralPath for exact paths and accept arrays directly (no loops needed). For LineRange, use -1 or 0 for end of file (e.g., 100,-1).

Examples:
  ✅ CORRECT: invoke_expression('Add-LinesToFile -Path file.cs -Content $code')
  ✅ CORRECT: invoke_expression('Show-TextFile file.txt -LineRange 10,20')
  ✅ CORRECT: invoke_expression('Show-TextFile file.txt -LineRange 100,-1')  # To end of file
  ✅ CORRECT: invoke_expression('Show-TextFile file.txt -LineRange -10')     # Last 10 lines
  ❌ WRONG: invoke_expression('Set-Content -Path file.cs -Value $code')
  ❌ WRONG: invoke_expression('Get-Content file.txt | Select-Object -Skip 9 -First 11')

For detailed examples: invoke_expression('Get-Help <cmdlet-name> -Examples')")]
    public static async Task<string> InvokeExpression(
        IPowerShellService powerShellService,
        [Description("The PowerShell command or pipeline to execute. When execute_immediately=true (immediate execution), both single-line and multi-line commands are supported, including if statements, loops, functions, and try-catch blocks. When execute_immediately=false (insertion mode), only single-line commands are supported - use semicolons to combine multiple statements into a single line.")]
        string pipeline,
        [Description("If true, executes the command immediately and returns the result. If false, inserts the command into the console for manual execution.")]
        bool execute_immediately = true,
        CancellationToken cancellationToken = default)
    {
        var result = await powerShellService.InvokeExpressionAsync(pipeline, execute_immediately, cancellationToken);
        
        // エラーメッセージかどうかをチェック（PowerShell が起動していない場合）
        // StartsWith を使用して、エラーメッセージの先頭部分のみをチェック
        if (result.StartsWith(ERROR_CONSOLE_NOT_RUNNING, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("[INFO] PowerShell console not running, auto-starting...");
            
            // 自動的にコンソールを起動（location情報も取得される）
            var startResult = await StartPowershellConsole(powerShellService, cancellationToken);
            
            // pipeline は実行しない。AIに確認を促す（重要な情報を先頭に）
            return $"PowerShell console was not running. It has been automatically started, but the requested pipeline was NOT executed. Please verify the current location and re-execute the command if appropriate.\n\n{startResult}";
        }
        
        // モジュールがインポートされていない場合は自動起動しない（ユーザーの選択を尊重）
        // 通常の結果またはその他のエラーメッセージをそのまま返す
        return result;
    }

    [McpServerTool]
    [Description("Launch a new PowerShell console window with PowerShell.MCP module imported. This tool should only be executed when explicitly requested by the user or when other tool executions fail.")]
    public static async Task<string> StartPowershellConsole(
        IPowerShellService powerShellService,
        CancellationToken cancellationToken = default)
    {
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
