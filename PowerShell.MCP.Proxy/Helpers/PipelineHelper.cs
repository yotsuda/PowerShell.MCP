using System.Text.RegularExpressions;

namespace PowerShell.MCP.Proxy.Helpers;

/// <summary>
/// Helper methods for pipeline string manipulation
/// </summary>
public static partial class PipelineHelper
{
    /// <summary>
    /// Truncate pipeline string to specified length
    /// </summary>
    public static string Truncate(string? pipeline, int maxLength = 30)
    {
        if (string.IsNullOrEmpty(pipeline)) return "";

        // Normalize whitespace
        var normalized = string.Join(" ", pipeline.Split(default(char[]), StringSplitOptions.RemoveEmptyEntries));

        if (normalized.Length <= maxLength)
            return normalized;

        return normalized[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Get PID string from pipe name
    /// </summary>
    public static string GetPidString(string? pipeName)
    {
        if (pipeName == null) return "unknown";
        var pid = Services.ConsoleSessionManager.GetPidFromPipeName(pipeName);
        return pid?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Check for local variable assignments without scope prefix and return warning message
    /// </summary>
    public static string? CheckLocalVariableAssignments(string pipeline)
    {
        // Pattern: $varname = (but not $script:, $global:, $env:, $using:, $null, $true, $false)
        // Also exclude common automatic variables like $_, $?, $^, $$, $args, $input, $foreach, $switch
        var matches = LocalVariableRegex().Matches(pipeline);

        if (matches.Count == 0) return null;

        var vars = matches.Select(m => "$" + m.Groups[1].Value).Distinct().ToList();
        if (vars.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("⚠️ SCOPE WARNING: Local variable assignment(s) detected:");
        foreach (var v in vars)
        {
            sb.AppendLine($"  {v} → Consider using {v.Replace("$", "$script:")} to preserve across calls");
        }
        return sb.ToString().TrimEnd();
    }

    [GeneratedRegex(@"\$(?!script:|global:|env:|using:|null\b|true\b|false\b|_\b|\?\b|\^\b|\$\b|args\b|input\b|foreach\b|switch\b|Matches\b|PSItem\b)([a-zA-Z_]\w*)\s*=")]
    private static partial Regex LocalVariableRegex();

    /// <summary>
    /// Check if pipeline is 3+ lines (not added to history) and return warning message
    /// </summary>
    public static string? CheckMultiLineHistory(string pipeline)
    {
        var lineCount = pipeline.Split('\n').Length;
        if (lineCount <= 2)
            return null;

        return "⚠️ HISTORY NOTE: Multi-line command (3+ lines) was NOT added to console history. To keep it in history, save as a .ps1 file and execute it, or rewrite as a single-line command using semicolons.";
    }

    /// <summary>
    /// Format busy status line
    /// </summary>
    public static string FormatBusyStatus(string? statusLine, int pid, string? pipeline, double duration)
    {
        // Use statusLine from dll if available, otherwise fallback to old format
        if (!string.IsNullOrEmpty(statusLine))
            return statusLine;

        var truncatedPipeline = Truncate(pipeline);
        return $"⧗ | pwsh PID: {pid} | Status: Busy | Pipeline: {truncatedPipeline} | Duration: {duration:F2}s";
    }

    /// <summary>
    /// Checks if bundled text editing cmdlets are used without var1/var2 parameters.
    /// Returns an error message if validation fails, null if OK.
    /// </summary>
    public static string? CheckVar1Enforcement(string pipeline, string? var1, string? var2)
    {
        // Skip validation when cmdlet is used as argument to Get-Help or Get-Command
        if (HelpOrGetCommandRegex().IsMatch(pipeline))
            return null;

        // Add-LinesToFile: always requires var1 (for -Content)
        if (AddLinesToFileRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Add-LinesToFile requires the var1 parameter for -Content to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the content via var1 and reference it as $var1 in the pipeline.";
        }

        // Update-LinesInFile: always requires var1 (for -Content)
        if (UpdateLinesInFileRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Update-LinesInFile requires the var1 parameter for -Content to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the content via var1 and reference it as $var1 in the pipeline.";
        }

        // Update-MatchInFile: requires var1 (-OldText) and var2 (-Replacement)
        if (UpdateMatchInFileRegex().IsMatch(pipeline))
        {
            if (var1 == null)
                return "ERROR: Update-MatchInFile requires the var1 parameter for -OldText to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the old text via var1 and reference it as $var1 in the pipeline.";
            if (var2 == null)
                return "ERROR: Update-MatchInFile requires the var2 parameter for -Replacement to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the replacement text via var2 and reference it as $var2 in the pipeline.";
        }

        // Remove-LinesFromFile: requires var1 only when -Pattern or -Contains is used
        if (RemoveLinesFromFileRegex().IsMatch(pipeline))
        {
            if (PatternOrContainsParamRegex().IsMatch(pipeline) && var1 == null)
            {
                return "ERROR: Remove-LinesFromFile with -Pattern or -Contains requires the var1 parameter to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the pattern/text via var1 and reference it as $var1 in the pipeline.";
            }
        }

        // Set-Content: requires var1 (for -Value)
        if (SetContentRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Set-Content requires the var1 parameter for -Value to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the value via var1 and reference it as $var1 in the pipeline.";
        }

        // Add-Content: requires var1 (for -Value)
        if (AddContentRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Add-Content requires the var1 parameter for -Value to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the value via var1 and reference it as $var1 in the pipeline.";
        }

        return null; // No issues
    }

    [GeneratedRegex(@"\b(Get-Help|Get-Command)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HelpOrGetCommandRegex();

    [GeneratedRegex(@"(?<![/\\])\bAdd-LinesToFile\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddLinesToFileRegex();

    [GeneratedRegex(@"(?<![/\\])\bUpdate-LinesInFile\b", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateLinesInFileRegex();

    [GeneratedRegex(@"(?<![/\\])\bUpdate-MatchInFile\b", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateMatchInFileRegex();

    [GeneratedRegex(@"(?<![/\\])\bRemove-LinesFromFile\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoveLinesFromFileRegex();

    [GeneratedRegex(@"-Pattern\b|-Contains\b", RegexOptions.IgnoreCase)]
    private static partial Regex PatternOrContainsParamRegex();

    [GeneratedRegex(@"(?<![/\\])\bSet-Content\b", RegexOptions.IgnoreCase)]
    private static partial Regex SetContentRegex();

    [GeneratedRegex(@"(?<![/\\])\bAdd-Content\b", RegexOptions.IgnoreCase)]
    private static partial Regex AddContentRegex();
}
