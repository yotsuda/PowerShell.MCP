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

    // The scope warning ("$x was assigned without $script: …") used to be
    // produced here by regex, at every new local assignment. It now comes
    // from the console itself (ScopeAnalyzer + MCPPollingEngine.ps1 in the
    // module), which sees the live session and warns only when a command
    // READS a variable that does not exist — the moment the mistake bites —
    // instead of at every harmless temporary.

    /// <summary>
    /// Detects a <c>Start-Process</c> that redirects the child's output but
    /// does not pass <c>-NoNewWindow</c>, and returns a hint. On Windows
    /// such a child runs in a NEW console, and a new console starts with the
    /// system OEM code page (932 on Japanese Windows, 437 on US English),
    /// not the UTF-8 this console was switched to — so the redirected log is
    /// written in that legacy code page and every non-ASCII character in it
    /// is garbled when read back as UTF-8. Measured here: the same command
    /// with <c>-NoNewWindow</c>, or run through <c>cmd /c … &gt; log</c>,
    /// produces UTF-8. A console's code page cannot be inherited by a new
    /// console, so the only sound fix is on the command, hence a hint rather
    /// than a setting. Windows only; elsewhere Start-Process has no console
    /// code page to lose.
    /// </summary>
    public static string? CheckStartProcessNewConsole(string pipeline)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var stripped = QuotedSpanRegex().Replace(pipeline, " ");
        if (!StartProcessRegex().IsMatch(stripped)) return null;
        if (!StartProcessRedirectRegex().IsMatch(stripped)) return null;
        if (NoNewWindowRegex().IsMatch(stripped)) return null;
        return "💡 `Start-Process` without `-NoNewWindow` runs the child in a NEW console, which starts with the system OEM code page (e.g. 932 or 437) rather than this console's UTF-8 — so the redirected output file is not UTF-8 and any non-ASCII text in it comes back garbled. " +
               "Add `-NoNewWindow` (the child then shares this console's UTF-8 code page; with its output redirected, nothing extra is shown here), or run it as `cmd /d /s /c \"<command> < NUL > out.log 2>&1\"`.";
    }

    [GeneratedRegex(@"(?<![\w\\/.-])Start-Process\b", RegexOptions.IgnoreCase)]
    private static partial Regex StartProcessRegex();

    // -RedirectStandardOutput / -RedirectStandardError / -RedirectStandardInput
    // and their aliases -RSO / -RSE / -RSI, as whole tokens (prefix forms such
    // as -RedirectStandardO are accepted by PowerShell and matched here too).
    [GeneratedRegex(@"(?<!\S)-(?:RedirectStandard\w*|RSO|RSE|RSI)(?=\s|:|$)", RegexOptions.IgnoreCase)]
    private static partial Regex StartProcessRedirectRegex();

    [GeneratedRegex(@"(?<!\S)-(?:NoNewWindow|nnw)(?=\s|:|$)", RegexOptions.IgnoreCase)]
    private static partial Regex NoNewWindowRegex();

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

    // TODO: Uncomment when JsonDuo is published to PS Gallery
    // [GeneratedRegex(@"\S+\.jsonc?\b", RegexOptions.IgnoreCase)]
    // private static partial Regex JsonFileRegex();
}
