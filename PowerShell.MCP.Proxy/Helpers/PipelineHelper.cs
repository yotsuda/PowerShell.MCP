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
    /// Check for local variable assignments without scope prefix and return warning message.
    /// First call returns a detailed warning; subsequent calls return a compact 1-liner.
    /// </summary>
    private static volatile bool _scopeWarningDetailShown = false;
    public static string? CheckLocalVariableAssignments(string pipeline)
    {
        // Pattern: $varname = (but not $script:, $global:, $env:, $using:, $null, $true, $false)
        // Also exclude common automatic variables like $_, $?, $^, $$, $args, $input, $foreach, $switch
        var matches = LocalVariableRegex().Matches(pipeline);

        if (matches.Count == 0) return null;

        // Exclude variables that are for-loop initializers (e.g., for ($i = 0; ...))
        var forLoopVars = ForLoopInitializerRegex().Matches(pipeline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var varNames = matches
            .Select(m => m.Groups[1].Value)
            .Where(v => !forLoopVars.Contains(v))
            .Distinct()
            .ToList();

        if (varNames.Count == 0) return null;

        if (!_scopeWarningDetailShown)
        {
            _scopeWarningDetailShown = true;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("⚠️ SCOPE WARNING: Local variable assignment(s) detected:");
            foreach (var name in varNames)
            {
                sb.AppendLine($"  ${name} → Consider using $script:{name} to preserve across calls");
            }
            return sb.ToString().TrimEnd();
        }
        else
        {
            // Compact reminder for subsequent calls
            var varList = string.Join(", ", varNames.Select(n => "$" + n));
            return $"⚠️ SCOPE: Use $script: prefix for: {varList}";
        }
    }

    /// <summary>
    /// Resets the scope warning state. For testing only.
    /// </summary>
    internal static void ResetScopeWarningState() => _scopeWarningDetailShown = false;

    /// <summary>
    /// Check if input/output contains .md files and return a one-time hint about MarkdownPointer module (per agent).
    /// </summary>
    private static readonly HashSet<string> _markdownHintShownAgents = new(StringComparer.OrdinalIgnoreCase);
    public static string? CheckMarkdownFileHint(string text, string agentId)
    {
        lock (_markdownHintShownAgents)
        {
            if (_markdownHintShownAgents.Contains(agentId)) return null;
            if (!MarkdownFileRegex().IsMatch(text)) return null;

            _markdownHintShownAgents.Add(agentId);
        }

        if (!OperatingSystem.IsWindows())
            return null;

        if (IsMarkdownPointerInstalled())
            return "💡 .md file(s) detected — Use the MarkdownPointer module to render and preview Markdown (e.g., mdp .\\README.md). It also includes an MCP server for AI integration.";
        else
            return "💡 .md file(s) detected — Install the MarkdownPointer PowerShell module (Install-Module MarkdownPointer) to render and preview Markdown with Mermaid/KaTeX support. After installation, use Get-Command -Module MarkdownPointer to explore available commands.";
    }

    /// <summary>
    /// Resets the markdown hint state. For testing only.
    /// </summary>
    internal static void ResetMarkdownHintState()
    {
        lock (_markdownHintShownAgents) { _markdownHintShownAgents.Clear(); }
    }

    /// <summary>
    /// Check if input/output contains .json files and return a one-time hint about JsonDuo module (per agent).
    /// </summary>
    private static readonly HashSet<string> _jsonHintShownAgents = new(StringComparer.OrdinalIgnoreCase);
    public static string? CheckJsonFileHint(string text, string agentId)
    {
        lock (_jsonHintShownAgents)
        {
            if (_jsonHintShownAgents.Contains(agentId)) return null;
            if (!JsonFileRegex().IsMatch(text)) return null;

            _jsonHintShownAgents.Add(agentId);
        }

        if (!OperatingSystem.IsWindows())
            return null;

        if (IsModuleInstalled("JsonDuo"))
            return "💡 .json file(s) detected — Use JsonDuo to view and edit JSON (e.g., jd .\\config.json). It also includes diff and MCP server features.";
        else
            return null; // JsonDuo is not publicly available yet
    }

    /// <summary>
    /// Resets the JSON hint state. For testing only.
    /// </summary>
    internal static void ResetJsonHintState()
    {
        lock (_jsonHintShownAgents) { _jsonHintShownAgents.Clear(); }
    }

    /// <summary>
    /// Check if a PowerShell module is installed by scanning PSModulePath directories.
    /// </summary>
    private static bool IsModuleInstalled(string moduleName)
    {
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var searchDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // PSModulePath from environment
        var modulePath = Environment.GetEnvironmentVariable("PSModulePath");
        if (!string.IsNullOrEmpty(modulePath))
        {
            foreach (var dir in modulePath.Split(separator, StringSplitOptions.RemoveEmptyEntries))
                searchDirs.Add(dir);
        }

        // Well-known PowerShell module paths (not always in PSModulePath for non-PS processes)
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        searchDirs.Add(Path.Combine(userHome, "Documents", "PowerShell", "Modules"));
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            // Check both versioned paths (e.g., PowerShell/7/Modules) and generic
            foreach (var psDir in Directory.EnumerateDirectories(Path.Combine(programFiles, "PowerShell")))
            {
                var modulesDir = Path.Combine(psDir, "Modules");
                if (Directory.Exists(modulesDir))
                    searchDirs.Add(modulesDir);
            }
        }

        foreach (var dir in searchDirs)
        {
            if (Directory.Exists(Path.Combine(dir, moduleName)))
                return true;
        }
        return false;
    }

    private static bool IsMarkdownPointerInstalled() => IsModuleInstalled("MarkdownPointer");

    [GeneratedRegex(@"\$(?!script:|global:|env:|using:|null\b|true\b|false\b|_\b|\?\b|\^\b|\$\b|args\b|input\b|foreach\b|switch\b|Matches\b|PSItem\b)([a-zA-Z_]\w*)\s*=")]
    private static partial Regex LocalVariableRegex();

    [GeneratedRegex(@"for\s*\(\s*\$([a-zA-Z_]\w*)\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex ForLoopInitializerRegex();

    /// <summary>
    /// Format busy status line
    /// </summary>
    public static string FormatBusyStatus(string? statusLine, int pid, string? pipeline, double duration)
    {
        // Use statusLine from dll if available, otherwise fallback
        if (!string.IsNullOrEmpty(statusLine))
            return statusLine;

        var consoleName = Services.ConsoleSessionManager.Instance.GetConsoleDisplayName(pid);
        var truncatedPipeline = Truncate(pipeline);
        return $"⧗ | {consoleName} | Status: Busy | Pipeline: {truncatedPipeline} | Duration: {duration:F2}s";
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
            return "ERROR: Use Add-LinesToFile instead of Set-Content. Example: Add-LinesToFile \"path\" -Content $var1 (pass content via the var1 parameter of invoke_expression). Add-LinesToFile creates new files and handles $, backtick, and double-quote characters safely.";
        }

        // Add-Content: requires var1 (for -Value)
        if (AddContentRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Use Add-LinesToFile instead of Add-Content. Example: Add-LinesToFile \"path\" -Content $var1 (pass content via the var1 parameter of invoke_expression). Add-LinesToFile creates new files and handles $, backtick, and double-quote characters safely.";
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

    [GeneratedRegex(@"\S+\.md\b", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownFileRegex();

    [GeneratedRegex(@"\S+\.json\b", RegexOptions.IgnoreCase)]
    private static partial Regex JsonFileRegex();
}
