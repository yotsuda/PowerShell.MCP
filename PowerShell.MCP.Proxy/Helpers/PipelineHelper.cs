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
}