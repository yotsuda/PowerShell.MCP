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
    /// Joins response blocks with exactly one blank line between them.
    /// Null / whitespace-only blocks are dropped and each block's leading and
    /// trailing line breaks are trimmed, so a block that already ends in a
    /// newline (or doesn't) can't produce a missing or doubled separator.
    /// Every tool response is assembled through this so separate consoles'
    /// results and trailing notices never run together line-to-line.
    /// </summary>
    public static string JoinBlocks(params string?[] blocks)
        => string.Join("\n\n", blocks
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Select(b => b!.Trim('\r', '\n')));

    /// <summary>
    /// Prefix that marks a status line as belonging to a console other than
    /// the one this tool call ran on, so the AI can tell its own result apart
    /// from other consoles' reports without inferring it from block order.
    /// </summary>
    public const string OtherConsoleMarker = "[other console] ";

    /// <summary>
    /// Prefixes every status line (✓ / ✗ / ⧗ Pipeline ...) in a block with
    /// <see cref="OtherConsoleMarker"/>. A single console's drained output can
    /// hold several joined results, hence every line rather than just the first.
    /// </summary>
    public static string MarkOtherConsole(string block)
        => StatusLineStartRegex().Replace(block, OtherConsoleMarker);

    [GeneratedRegex(@"^(?=[✓✗⧗] Pipeline )", RegexOptions.Multiline)]
    private static partial Regex StatusLineStartRegex();

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
    /// Check for local variable assignments without scope prefix and
    /// return a warning message.
    ///
    /// Noise-control contract: each distinct variable name is reported
    /// to a given agent AT MOST ONCE. Re-assigning the same name on
    /// later pipelines is silent (the AI already got the lesson for
    /// that name). If a caller introduces a NEW local variable name,
    /// it produces a compact warning covering only the new names.
    /// The very first warning shown to an agent is the detailed
    /// "Consider using $script:..." form so the AI learns the concept;
    /// subsequent new-name warnings are the short one-liner.
    ///
    /// Pre-refactor behaviour was to emit a compact warning on EVERY
    /// call that contained any local assignment — which in practice
    /// produced 20+ duplicate warnings per session for the same
    /// already-warned-about name, dominating response volume without
    /// adding information.
    /// </summary>
    private static readonly object _scopeWarningLock = new();
    // Per-agent record of every local variable name we've already
    // warned that agent about. Separate dictionary (not just a set of
    // "agents that saw the detail") so a NEW name still triggers a
    // compact reminder even after the detail was shown.
    private static readonly Dictionary<string, HashSet<string>> _warnedVarsPerAgent
        = new(StringComparer.OrdinalIgnoreCase);
    // Agents that have seen the detailed warning at least once. Any
    // subsequent warning to the same agent uses the compact form.
    private static readonly HashSet<string> _detailShownAgents
        = new(StringComparer.OrdinalIgnoreCase);

    public static string? CheckLocalVariableAssignments(string pipeline, string agentId = "default")
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

        lock (_scopeWarningLock)
        {
            if (!_warnedVarsPerAgent.TryGetValue(agentId, out var warnedVars))
            {
                warnedVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _warnedVarsPerAgent[agentId] = warnedVars;
            }

            // Only warn about variable names this agent has not seen
            // before. If every name in this pipeline was already warned
            // about, stay silent — the AI has no lesson left to learn
            // for those names and re-printing the warning is pure noise.
            var newVars = varNames.Where(v => !warnedVars.Contains(v)).ToList();
            if (newVars.Count == 0) return null;

            foreach (var v in newVars) warnedVars.Add(v);

            if (_detailShownAgents.Add(agentId))
            {
                // First warning ever for this agent — spell out the fix so
                // the AI learns the $script: convention.
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("⚠️ SCOPE WARNING: Local variable assignment(s) detected:");
                foreach (var name in newVars)
                {
                    sb.AppendLine($"  ${name} → Consider using $script:{name} to preserve across calls");
                }
                return sb.ToString().TrimEnd();
            }
            else
            {
                // Agent already knows the rule; just list the newly
                // introduced names as a compact reminder.
                var varList = string.Join(", ", newVars.Select(n => "$" + n));
                return $"⚠️ SCOPE: Use $script: prefix for: {varList}";
            }
        }
    }

    /// <summary>
    /// Resets the scope warning state. For testing only.
    /// </summary>
    internal static void ResetScopeWarningState()
    {
        lock (_scopeWarningLock)
        {
            _warnedVarsPerAgent.Clear();
            _detailShownAgents.Clear();
        }
    }

    /// <summary>
    /// Detects a redundant leading <c>cd</c> / <c>Set-Location</c> and returns
    /// a hint telling the AI the console's location already persists across
    /// calls. Fires only when the pipeline starts with a location change whose
    /// target equals the pre-execution cwd AND the pipeline left the console
    /// at that same cwd — i.e. the prefix did nothing. Returns null otherwise
    /// (including when either cwd is unknown or not a filesystem path).
    /// </summary>
    public static string? CheckRedundantLeadingCd(string pipeline, string? preCwd, string? postCwd)
    {
        if (string.IsNullOrEmpty(preCwd) || string.IsNullOrEmpty(postCwd)) return null;
        var match = LeadingSetLocationRegex().Match(pipeline);
        if (!match.Success) return null;

        var arg = match.Groups["arg"].Value;
        if (arg.StartsWith('\''))
            arg = arg[1..^1].Replace("''", "'");
        else if (arg.StartsWith('"'))
            arg = arg[1..^1];
        if (arg.Length == 0) return null;

        try
        {
            if (!Path.IsPathFullyQualified(preCwd) || !Path.IsPathFullyQualified(postCwd)) return null;
            if (arg == "~" || arg.StartsWith("~/") || arg.StartsWith("~\\"))
                arg = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + arg[1..];
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var normPre = Path.TrimEndingDirectorySeparator(Path.GetFullPath(preCwd));
            var normPost = Path.TrimEndingDirectorySeparator(Path.GetFullPath(postCwd));
            var normTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arg, normPre));
            if (!string.Equals(normPre, normTarget, comparison) || !string.Equals(normPre, normPost, comparison))
                return null;
            return $"💡 Unnecessary leading `{match.Groups["verb"].Value}`: the location persists across execute_command calls. Omit it next time.";
        }
        catch
        {
            return null;
        }
    }

    // Leading cd / chdir / sl / Set-Location with one literal path argument
    // (bare, single-quoted, or double-quoted without $ / backtick expansion),
    // followed by a statement separator or end of pipeline.
    [GeneratedRegex(@"^\s*(?<verb>cd|chdir|sl|Set-Location)\s+(?:-(?:LiteralPath|Path|LP|PSPath)(?::|\s)\s*)?(?<arg>'(?:[^']|'')*'|""[^""`$]*""|[^\s;|&'""`$(){}]+)\s*(?:;|&&|\r?\n|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingSetLocationRegex();

    /// <summary>
    /// Detects a pipeline that runs its real work through a child
    /// <c>pwsh -Command</c> / <c>-File</c> / <c>-EncodedCommand</c> (or
    /// <c>pwsh script.ps1</c>) and returns a hint that this console is already
    /// pwsh and can run it directly. An AI used to a bash-style tool wraps
    /// commands this way by habit; here it costs a process start, doubles the
    /// quoting, and strands variables, modules and cwd changes in the child.
    ///
    /// <para>Fires only for a LEADING invocation, so <c>Start-Process pwsh</c>,
    /// <c>Start-Job</c>, or a pwsh buried mid-pipeline (usually deliberate) are
    /// left alone, and never for <c>powershell(.exe)</c> — Windows PowerShell
    /// 5.1 is a different engine, and running it from here is the only way to
    /// test against it. A nested pwsh has legitimate uses too (a profile-free
    /// test, another PowerShell version, a script that must exit the shell), so
    /// the hint is shown AT MOST ONCE per agent: the lesson is one sentence,
    /// and repeating it on every call would punish the deliberate cases.</para>
    /// </summary>
    private static readonly HashSet<string> _nestedPwshHintShownAgents = new(StringComparer.OrdinalIgnoreCase);

    public static string? CheckNestedPwsh(string pipeline, string agentId = "default")
    {
        if (!IsNestedPwshInvocation(pipeline)) return null;
        lock (_nestedPwshHintShownAgents)
        {
            if (!_nestedPwshHintShownAgents.Add(agentId)) return null;
        }
        return "💡 This console is already pwsh — run the command or script directly (e.g. `& .\\script.ps1`) instead of through `pwsh -Command` / `-File`. " +
               "A nested pwsh is a second process: it starts slowly, doubles the quoting, and its variables, modules and cwd changes never reach this session. " +
               "Keep it only when you need a fresh process on purpose (a script tested without the profile, another PowerShell version, or something that must exit the shell). Shown once per session.";
    }

    /// <summary>
    /// True when the pipeline starts with a pwsh invocation that runs a command
    /// or script in the child. Exposed for tests; <see cref="CheckNestedPwsh"/>
    /// adds the per-agent dedup on top.
    /// </summary>
    internal static bool IsNestedPwshInvocation(string pipeline)
    {
        var match = LeadingPwshRegex().Match(pipeline);
        if (!match.Success) return false;
        var args = match.Groups["args"].Value;
        // Flags are looked for outside quoted spans, so a `-c` inside a string
        // argument doesn't count; a .ps1 anywhere in the arguments does, since
        // pwsh 7 treats a bare script path as -File.
        if (NestedPwshRunFlagRegex().IsMatch(QuotedSpanRegex().Replace(args, " "))) return true;
        return Ps1ArgumentRegex().IsMatch(args);
    }

    /// <summary>Resets the nested-pwsh hint state. For testing only.</summary>
    internal static void ResetNestedPwshHintState()
    {
        lock (_nestedPwshHintShownAgents) { _nestedPwshHintShownAgents.Clear(); }
    }

    // A leading pwsh / pwsh.exe / pwsh-preview — bare, with a path, quoted, or
    // via the call operator — followed by its arguments up to an unquoted
    // statement separator (; | newline). Only pwsh: `powershell` is Windows
    // PowerShell 5.1, a different engine, and is deliberately not matched.
    [GeneratedRegex(@"^\s*(?:&\s*)?(?:'[^']*[\\/]pwsh(?:-preview)?(?:\.exe)?'|""[^""]*[\\/]pwsh(?:-preview)?(?:\.exe)?""|(?:[^\s'"";|&]*[\\/])?pwsh(?:-preview)?(?:\.exe)?)(?![\w.-])(?<args>(?:""(?:[^""]|"""")*""|'(?:[^']|'')*'|[^;|\r\n""'])*)", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingPwshRegex();

    // -Command / -c (and pwsh's accepted prefixes), -CommandWithArgs / -cwa,
    // -File / -f, -EncodedCommand / -e / -ec, as a whole token (optionally with
    // the `-Flag:value` form).
    [GeneratedRegex(@"(?<!\S)-(?:c(?:o|om|omm|omma|omman|ommand|ommandwithargs|wa)?|f(?:i|il|ile)?|e(?:c|ncodedcommand)?)(?=\s|:|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NestedPwshRunFlagRegex();

    [GeneratedRegex(@"""(?:[^""]|"""")*""|'(?:[^']|'')*'")]
    private static partial Regex QuotedSpanRegex();

    [GeneratedRegex(@"\.ps1(?=[""'\s]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Ps1ArgumentRegex();

    // TODO: Uncomment when JsonDuo is published to PS Gallery
    // /// <summary>
    // /// Check if input/output contains .json files and return a one-time hint about JsonDuo module (per agent).
    // /// </summary>
    // private static readonly HashSet<string> _jsonHintShownAgents = new(StringComparer.OrdinalIgnoreCase);
    // public static string? CheckJsonFileHint(string text, string agentId)
    // {
    //     lock (_jsonHintShownAgents)
    //     {
    //         if (_jsonHintShownAgents.Contains(agentId)) return null;
    //         if (!JsonFileRegex().IsMatch(text)) return null;
    //
    //         _jsonHintShownAgents.Add(agentId);
    //     }
    //
    //     if (!OperatingSystem.IsWindows())
    //         return null;
    //
    //     if (IsModuleInstalled("JsonDuo"))
    //         return "💡 .json file(s) detected — Use JsonDuo to view and edit JSON (e.g., jd .\\config.json). It also includes diff and MCP server features.";
    //     else
    //         return null; // JsonDuo is not publicly available yet
    // }
    //
    // /// <summary>
    // /// Resets the JSON hint state. For testing only.
    // /// </summary>
    // internal static void ResetJsonHintState()
    // {
    //     lock (_jsonHintShownAgents) { _jsonHintShownAgents.Clear(); }
    // }

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
            var psRoot = Path.Combine(programFiles, "PowerShell");
            // Check both versioned paths (e.g., PowerShell/7/Modules) and generic
            if (Directory.Exists(psRoot))
            {
                foreach (var psDir in Directory.EnumerateDirectories(psRoot))
                {
                    var modulesDir = Path.Combine(psDir, "Modules");
                    if (Directory.Exists(modulesDir))
                        searchDirs.Add(modulesDir);
                }
            }
        }

        foreach (var dir in searchDirs)
        {
            if (Directory.Exists(Path.Combine(dir, moduleName)))
                return true;
        }
        return false;
    }

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
            return "ERROR: Add-LinesToFile requires the var1 argument of execute_command for -Content, to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the content as the var1 tool argument and reference it as $var1 in the pipeline; assigning $var1 inside the pipeline does not count.";
        }

        // Update-LinesInFile: always requires var1 (for -Content)
        if (UpdateLinesInFileRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Update-LinesInFile requires the var1 argument of execute_command for -Content, to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the content as the var1 tool argument and reference it as $var1 in the pipeline; assigning $var1 inside the pipeline does not count.";
        }

        // Update-MatchInFile: requires var1 (-OldText) and var2 (-Replacement)
        if (UpdateMatchInFileRegex().IsMatch(pipeline))
        {
            if (var1 == null)
                return "ERROR: Update-MatchInFile requires the var1 argument of execute_command for -OldText, to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the old text as the var1 tool argument and reference it as $var1 in the pipeline; assigning $var1 inside the pipeline does not count.";
            if (var2 == null)
                return "ERROR: Update-MatchInFile requires the var2 argument of execute_command for -Replacement, to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the replacement text as the var2 tool argument and reference it as $var2 in the pipeline; assigning $var2 inside the pipeline does not count.";
        }

        // Remove-LinesFromFile: requires var1 only when -Pattern or -Contains is used
        if (RemoveLinesFromFileRegex().IsMatch(pipeline))
        {
            if (PatternOrContainsParamRegex().IsMatch(pipeline) && var1 == null)
            {
                return "ERROR: Remove-LinesFromFile with -Pattern or -Contains requires the var1 argument of execute_command, to avoid PowerShell parser expansion of $, backtick, or double-quote characters. Pass the pattern/text as the var1 tool argument and reference it as $var1 in the pipeline; assigning $var1 inside the pipeline does not count.";
            }
        }

        // Set-Content: requires var1 (for -Value)
        if (SetContentRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Use Add-LinesToFile instead of Set-Content. Example: Add-LinesToFile \"path\" -Content $var1 (pass content via the var1 parameter of execute_command). Add-LinesToFile creates new files and handles $, backtick, and double-quote characters safely.";
        }

        // Add-Content: requires var1 (for -Value)
        if (AddContentRegex().IsMatch(pipeline) && var1 == null)
        {
            return "ERROR: Use Add-LinesToFile instead of Add-Content. Example: Add-LinesToFile \"path\" -Content $var1 (pass content via the var1 parameter of execute_command). Add-LinesToFile creates new files and handles $, backtick, and double-quote characters safely.";
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

    // TODO: Uncomment when JsonDuo is published to PS Gallery
    // [GeneratedRegex(@"\S+\.jsonc?\b", RegexOptions.IgnoreCase)]
    // private static partial Regex JsonFileRegex();
}
