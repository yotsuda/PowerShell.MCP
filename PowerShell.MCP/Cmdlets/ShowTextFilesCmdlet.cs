using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Displays text file content with line numbers
/// LLM-optimized: 3-digit line numbers, : for matches, - for context (grep standard; -Context sets how many lines, 0 for none), shows relative path
/// </summary>
[Cmdlet(VerbsCommon.Show, "TextFiles")]
public class ShowTextFilesCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter]
    [ValidateLineRange]
    public string[]? LineRange { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Pattern { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Contains { get; set; }

    /// <summary>
    /// Lines of context shown before and after each -Pattern / -Contains
    /// match (the `N-` lines). 2 by default, like grep -C2. 0 lists only the
    /// matching lines — the grep-style form for locating lines across many
    /// files, where context would multiply the output.
    /// </summary>
    [Parameter]
    [ValidateRange(0, 1000)]
    public int Context { get; set; } = 2;

    [Parameter]
    public int? Skip { get; set; }

    [Parameter]
    public int? First { get; set; }

    [Parameter]
    public SwitchParameter Recurse { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    private bool _lastFileHadOutput = false;
    private Regex? _compiledRegex = null;
    private bool _isMultilineContains = false;
    // For the no-match warning at the end of a -Pattern / -Contains search.
    private int _filesSearched = 0;
    private int _binaryFilesSkipped = 0;
    private bool _anyMatch = false;

    private bool IsSearch => _isMultilineContains || !string.IsNullOrEmpty(Pattern) || !string.IsNullOrEmpty(Contains);


    protected override void BeginProcessing()
    {
        // Contains and Pattern can be combined (OR condition) for Show-TextFiles

        // -Recurse requires -Pattern or -Contains
        if (Recurse && string.IsNullOrEmpty(Pattern) && string.IsNullOrEmpty(Contains))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("-Recurse requires -Pattern or -Contains parameter."),
                "RecurseRequiresPattern",
                ErrorCategory.InvalidArgument,
                null));
        }

        // Error if Pattern contains newlines (regex multiline not supported)
        ValidateNoNewlines(Pattern, "Pattern");
        // Contains with newlines is allowed (multiline mode)

        // Detect multiline Contains mode
        _isMultilineContains = !string.IsNullOrEmpty(Contains) && (Contains.Contains('\n') || Contains.Contains('\r'));

        // multiline Contains + Pattern is not supported
        if (_isMultilineContains && !string.IsNullOrEmpty(Pattern))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Multi-line -Contains cannot be combined with -Pattern."),
                "MultilineContainsWithPattern",
                ErrorCategory.InvalidArgument,
                null));
        }

        // -Recurse + multiline Contains is not supported
        if (Recurse && _isMultilineContains)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("-Recurse cannot be combined with multi-line -Contains."),
                "RecurseWithMultilineContains",
                ErrorCategory.InvalidArgument,
                null));
        }

        // -Skip/-First compatibility: map to -LineRange
        LineRange = MapSkipFirstToLineRange(Skip, First, LineRange);

        // Pre-compile regex for performance (used across all files)
        // Combine Contains and Pattern with OR if both specified (single-line mode only)
        if (!_isMultilineContains && (!string.IsNullOrEmpty(Pattern) || !string.IsNullOrEmpty(Contains)))
        {
            string combinedPattern;
            if (!string.IsNullOrEmpty(Contains) && !string.IsNullOrEmpty(Pattern))
            {
                // Both specified: OR condition (escaped Contains | Pattern).
                // Wrap each side in (?:...) so quantifiers/alternation in Pattern don't bind
                // across the | — without this, `-Pattern 'x{0,3}'` would let the right side
                // match an empty string and hit every line.
                combinedPattern = "(?:" + Regex.Escape(Contains) + ")|(?:" + Pattern + ")";
            }
            else if (!string.IsNullOrEmpty(Pattern))
            {
                combinedPattern = Pattern;
            }
            else
            {
                combinedPattern = Regex.Escape(Contains!);
            }
            _compiledRegex = new Regex(combinedPattern, RegexOptions.Compiled);
        }
    }

    protected override void ProcessRecord()
    {
        // LineRange validation
        ValidateLineRange(LineRange);

        // Get files to process
        IEnumerable<ResolvedFileInfo> files;
        if (Recurse)
        {
            files = EnumerateFilesRecursively(Path, LiteralPath);
        }
        else
        {
            files = ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true);
        }

        foreach (var fileInfo in files)
        {
            try
            {
                // -Recurse walks a whole tree, build output included; a match
                // inside a DLL is never wanted and prints as megabytes of noise.
                if (Recurse && IsBinaryFile(fileInfo.ResolvedPath))
                {
                    _binaryFilesSkipped++;
                    continue;
                }

                // Blank line between files (only if last file had output)
                if (_lastFileHadOutput)
                {
                    WriteObject("");
                    _lastFileHadOutput = false;
                }

                // Get encoding: if specified use it, otherwise null (auto-detect BOM in StreamReader)
                System.Text.Encoding? encoding = string.IsNullOrEmpty(Encoding)
                    ? null
                    : EncodingHelper.GetEncodingForReading(fileInfo.ResolvedPath, Encoding);

                if (IsSearch)
                    _filesSearched++;

                // Handle empty file - skip silently when Pattern/Contains is specified
                var fileInfoObj = new FileInfo(fileInfo.ResolvedPath);
                if (fileInfoObj.Length == 0)
                {
                    if (string.IsNullOrEmpty(Pattern) && string.IsNullOrEmpty(Contains))
                    {
                        var displayPath = GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath);
                        WriteObject(AnsiColors.Header(displayPath));
                        WriteWarning("File is empty");
                        _lastFileHadOutput = true;
                    }
                    continue;
                }

                if (_isMultilineContains)
                {
                    _lastFileHadOutput = ShowWithContainsMultiline(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                    _anyMatch |= _lastFileHadOutput;
                }
                else if (!string.IsNullOrEmpty(Pattern) || !string.IsNullOrEmpty(Contains))
                {
                    // Both Pattern and Contains use _compiledRegex (combined with OR if both specified)
                    _lastFileHadOutput = ShowWithPattern(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                    _anyMatch |= _lastFileHadOutput;
                }
                else
                {
                    ShowWithLineRange(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                    _lastFileHadOutput = true;
                }
            }
            // A downstream `Select-Object -First` stops the pipeline by throwing
            // PipelineStoppedException out of WriteObject. It must propagate:
            // caught here, it was reported as a read error and the loop went on
            // to read every remaining file.
            catch (Exception ex) when (ex is not PipelineStoppedException)
            {
                WriteError(new ErrorRecord(ex, "ShowTextFileFailed", ErrorCategory.ReadError, fileInfo.ResolvedPath));
            }
        }
    }


    protected override void EndProcessing()
    {
        // A search that finds nothing printed nothing, which reads the same
        // as a wrong path. Said on the warning stream so a caller consuming
        // the output is unaffected. Not said when no file was even read:
        // the path errors already explain that.
        if (IsSearch && !_anyMatch && (_filesSearched > 0 || _binaryFilesSkipped > 0))
        {
            var message = $"No matches in {_filesSearched} file(s).";
            if (_binaryFilesSkipped > 0)
                message += $" {_binaryFilesSkipped} binary file(s) skipped.";
            WriteWarning(message);
        }
    }

    private void ShowWithLineRange(string inputPath, string filePath, System.Text.Encoding? encoding)
    {
        var displayPath = GetDisplayPath(inputPath, filePath);
        WriteObject(AnsiColors.Header(displayPath));

        // Default to (1, int.MaxValue) if LineRange is null or empty
        var (requestedStart, requestedEnd) = TextFileUtility.ParseLineRange(LineRange);

        // Tail specification (-10 or -10,-1)
        if (requestedStart < 0)
        {
            ShowTailLines(filePath, encoding, -requestedStart);
            return;
        }

            // Normal range specification
        int skipCount = requestedStart - 1;
        int takeCount = requestedEnd == int.MaxValue ? int.MaxValue : requestedEnd - requestedStart + 1;

        var lines = TextFileUtility.ReadLinesShared(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        int currentLine = requestedStart;
        bool hasOutput = false;

        foreach (var line in lines)
        {
            WriteObject($"{currentLine,3}: {line}");
            currentLine++;
            hasOutput = true;
        }

        if (!hasOutput && LineRange != null)
        {
            WriteWarning($"Line range {requestedStart}-{requestedEnd} is beyond file length. No output.");
        }
    }

    private void ShowTailLines(string filePath, System.Text.Encoding? encoding, int tailCount)
    {
        // Get last N lines in single pass using RotateBuffer
        var buffer = new RotateBuffer<(string line, int lineNumber)>(tailCount);

        int lineNumber = 1;
        foreach (var line in TextFileUtility.ReadLinesShared(filePath, encoding))
        {
            buffer.Add((line, lineNumber));
            lineNumber++;
        }

        if (buffer.Count == 0)
        {
            WriteWarning("File is empty. No output.");
            return;
        }

        foreach (var item in buffer)
        {
            WriteObject($"{item.lineNumber,3}: {item.line}");
        }
    }

    private bool ShowWithPattern(string inputPath, string filePath, System.Text.Encoding? encoding)
    {
        return ShowWithMatch(inputPath, filePath, encoding, line => _compiledRegex!.IsMatch(line), Pattern ?? "", _compiledRegex);
    }

    /// <summary>
    /// Multiline Contains search: reads entire file content and finds multi-line literal matches.
    /// Displays matched lines with yellow highlighting and context lines.
    /// </summary>
    private bool ShowWithContainsMultiline(string inputPath, string filePath, System.Text.Encoding? encoding)
    {
        var metadata = TextFileUtility.DetectFileMetadata(filePath, Encoding);
        var content = File.ReadAllText(filePath, encoding ?? metadata.Encoding);

        // Normalize Contains newlines to match file's newline sequence
        var normalizedContains = Contains!
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", metadata.NewlineSequence);

        // Find all occurrences
        var matches = new List<(int startIdx, int endIdx)>();
        int searchIdx = 0;
        while ((searchIdx = content.IndexOf(normalizedContains, searchIdx, StringComparison.Ordinal)) >= 0)
        {
            matches.Add((searchIdx, searchIdx + normalizedContains.Length));
            searchIdx += normalizedContains.Length;
        }

        if (matches.Count == 0) return false;

        // Split content into lines for display
        var allLines = content.Split(metadata.NewlineSequence);

        // Calculate line number for each character offset
        // Build cumulative offset array: lineStartOffsets[i] = start offset of line i (0-based)
        var lineStartOffsets = new int[allLines.Length];
        int offset = 0;
        for (int i = 0; i < allLines.Length; i++)
        {
            lineStartOffsets[i] = offset;
            offset += allLines[i].Length + metadata.NewlineSequence.Length;
        }

        // Parse LineRange
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        var displayPath = GetDisplayPath(inputPath, filePath);
        var outputBuffer = new List<string>();
        outputBuffer.Add(AnsiColors.Header(displayPath));

        int lastOutputLine = 0; // 1-based

        foreach (var (matchStart, matchEnd) in matches)
        {
            // Find start and end line numbers (1-based)
            int matchStartLine = TextFileUtility.GetLineNumberFromOffset(lineStartOffsets, matchStart) + 1;
            int matchEndLine = TextFileUtility.GetLineNumberFromOffset(lineStartOffsets, matchEnd > 0 ? matchEnd - 1 : matchEnd) + 1;

            // LineRange filter: match start line must be within range
            if (matchStartLine < startLine || matchStartLine > endLine) continue;

            // Context lines before and after (-Context, default 2)
            int contextStart = Math.Max(1, matchStartLine - Context);
            int contextEnd = Math.Min(allLines.Length, matchEndLine + Context);

            // Gap detection. With -Context 0 the listing is grep-style and the
            // line numbers already show the gaps, so no separator lines.
            if (Context > 0 && lastOutputLine > 0 && contextStart > lastOutputLine + 1)
            {
                outputBuffer.Add("");
            }

            // Pre-context lines
            for (int ln = contextStart; ln < matchStartLine; ln++)
            {
                if (ln > lastOutputLine)
                {
                    outputBuffer.Add($"{ln,3}- {allLines[ln - 1]}");
                    lastOutputLine = ln;
                }
            }

            // Match lines (highlighted in yellow)
            for (int ln = matchStartLine; ln <= matchEndLine && ln <= allLines.Length; ln++)
            {
                if (ln > lastOutputLine)
                {
                    // Highlight entire matched line in yellow
                    outputBuffer.Add($"{ln,3}: {AnsiColors.Yellow}{allLines[ln - 1]}{AnsiColors.Reset}");
                    lastOutputLine = ln;
                }
            }

            // Post-context lines
            for (int ln = matchEndLine + 1; ln <= contextEnd; ln++)
            {
                if (ln > lastOutputLine)
                {
                    outputBuffer.Add($"{ln,3}- {allLines[ln - 1]}");
                    lastOutputLine = ln;
                }
            }
        }

        if (outputBuffer.Count > 1) // More than just header
        {
            WriteObject(outputBuffer, enumerateCollection: true);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Searches lines based on match conditions and displays with context (true single-pass implementation using rotate buffer)
    /// Returns true if any matches were found
    /// </summary>
    private bool ShowWithMatch(string inputPath, string filePath,
        System.Text.Encoding? encoding,
        Func<string, bool> matchPredicate,
        string matchValue,
        Regex? regex)
    {
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
        var displayPath = GetDisplayPath(inputPath, filePath);
        var outputBuffer = new List<string>();
        bool anyMatch = false;

        // Use encoding if specified, otherwise default (UTF-8 with BOM auto-detection)
        // ReadLinesShared allows reading files locked by other processes
        var lines = TextFileUtility.ReadLinesShared(filePath, encoding);

        using (var enumerator = lines.GetEnumerator())
        {
            if (!enumerator.MoveNext())
            {
                // Empty file
                return false;
            }

            int lineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();

            // Rotate buffer: holds the previous -Context lines (line content and
            // line number pairs). With -Context 0 nothing is ever added, so the
            // buffer stays empty and the leading-context loops are no-ops.
            var preContextBuffer = new RotateBuffer<(string line, int lineNumber)>(Math.Max(1, Context));

            // Gap candidate (1 line after trailing context)
            (string line, int lineNumber)? gapLineInfo = null;

            // Trailing context counter
            int afterMatchCounter = 0;

            // Last output line number (for gap detection)
            int lastOutputLine = 0;

            // Gap detection: empty line output flag (output when next match is found)
            bool needsGapSeparator = false;

            while (true)
            {
                // Match check only within original LineRange
                Match? currentMatch = null;
                bool matched = false;
                if (lineNumber >= startLine && lineNumber <= endLine)
                {
                    if (regex != null)
                    {
                        currentMatch = regex.Match(currentLine);
                        matched = currentMatch.Success;
                    }
                    else
                    {
                        matched = matchPredicate(currentLine);
                    }
                }

                if (matched)
                {
                    anyMatch = true;

                    // Output header (first time only)
                    if (outputBuffer.Count == 0)
                    {
                        outputBuffer.Add(AnsiColors.Header(displayPath));
                    }

                    // Gap processing: if gapLineInfo exists, check and output
                    if (gapLineInfo.HasValue)
                    {
                        // Calculate start line of leading context
                        int preContextStart = lineNumber;
                        for (int i = preContextBuffer.Count - 1; i >= 0; i--)
                        {
                            var ctx = preContextBuffer[i];
                            if (ctx.lineNumber > lastOutputLine)
                            {
                                preContextStart = ctx.lineNumber;
                            }
                        }

                        var gapInfo = gapLineInfo.Value;

                        // If gapLine next line is start of leading context, output gapLine (continuous).
                        // Not with -Context 0: the caller asked for matching lines only.
                        if (Context > 0 && gapInfo.lineNumber + 1 == preContextStart)
                        {
                            string gapDisplay = ApplyHighlighting(gapInfo.line, regex, regex == null ? matchValue : null);
                            outputBuffer.Add($"{gapInfo.lineNumber,3}- {gapDisplay}");
                            lastOutputLine = gapInfo.lineNumber;
                        }
                        else if (gapInfo.lineNumber >= preContextStart)
                        {
                            // gapLine is included in leading context -> will be output as leading context, do nothing
                        }
                        else if (Context > 0)
                        {
                            // Gap is 2+ lines -> separate with empty line.
                            // Not with -Context 0: grep-style, the line
                            // numbers already show the gap.
                            outputBuffer.Add("");
                        }
                        gapLineInfo = null;
                        needsGapSeparator = false;
                    }
                    else if (needsGapSeparator)
                    {
                        // Empty line output when no gapLine
                        if (Context > 0) outputBuffer.Add("");
                        needsGapSeparator = false;
                    }

                    // Output previous 2 lines (from rotate buffer)
                    foreach (var ctx in preContextBuffer)
                    {
                        if (ctx.lineNumber > lastOutputLine)
                        {
                            string ctxDisplay = ApplyHighlighting(ctx.line, regex, regex == null ? matchValue : null);
                            outputBuffer.Add($"{ctx.lineNumber,3}- {ctxDisplay}");
                        }
                    }

                    // Output match line (highlighted) - use cached match if available
                    string displayLine = currentMatch != null
                        ? ApplyHighlightingWithMatch(currentLine, currentMatch)
                        : ApplyHighlighting(currentLine, regex, matchValue);
                    outputBuffer.Add($"{lineNumber,3}: {displayLine}");

                    afterMatchCounter = Context;
                    lastOutputLine = lineNumber;
                    gapLineInfo = null; // Reset gap
                    needsGapSeparator = false; // Reset flag
                }
                else
                {
                    // Output trailing context
                    if (afterMatchCounter > 0)
                    {
                        string display = ApplyHighlighting(currentLine, regex, regex == null ? matchValue : null);
                        outputBuffer.Add($"{lineNumber,3}- {display}");
                        afterMatchCounter--;
                        lastOutputLine = lineNumber;
                    }
                    else if (lastOutputLine > 0)
                    {
                        // Gap detection mode
                        if (lineNumber == lastOutputLine + 1)
                        {
                            // First line after last output -> store as gap candidate
                            gapLineInfo = (currentLine, lineNumber);
                        }
                        else if (lineNumber == lastOutputLine + 2)
                        {
                            // Second line after last output -> gap is 2+ lines -> set empty line flag
                            needsGapSeparator = true;
                            // Keep gapLineInfo (used later for check)
                        }
                    }
                }

                // Update rotate buffer (save with line number)
                if (Context > 0) preContextBuffer.Add((currentLine, lineNumber));

                // Continue to next
                if (hasNext)
                {
                    lineNumber++;
                    currentLine = enumerator.Current;
                    hasNext = enumerator.MoveNext();
                }
                else
                {
                    break;
                }
            }
        }

        // Batch output for better performance
        if (outputBuffer.Count > 0)
        {
            WriteObject(outputBuffer, enumerateCollection: true);
        }

        return anyMatch;
    }
    /// <summary>
    /// Applies yellow highlighting using index-based approach (faster than Regex.Replace)
    /// </summary>
    private string ApplyHighlighting(string line, Regex? regex, string? containsValue)
    {
        if (regex != null)
        {
            var match = regex.Match(line);
            if (!match.Success) return line;

            var sb = new StringBuilder(line.Length + 32);
            int lastEnd = 0;
            while (match.Success)
            {
                sb.Append(line, lastEnd, match.Index - lastEnd);
                sb.Append(AnsiColors.Yellow);
                sb.Append(match.Value);
                sb.Append(AnsiColors.Reset);
                lastEnd = match.Index + match.Length;
                match = match.NextMatch();
            }
            sb.Append(line, lastEnd, line.Length - lastEnd);
            return sb.ToString();
        }
        else if (containsValue != null)
        {
            int index = line.IndexOf(containsValue, StringComparison.Ordinal);
            if (index < 0) return line;

            var sb = new StringBuilder(line.Length + 32);
            int lastEnd = 0;
            while (index >= 0)
            {
                sb.Append(line, lastEnd, index - lastEnd);
                sb.Append(AnsiColors.Yellow);
                sb.Append(containsValue);
                sb.Append(AnsiColors.Reset);
                lastEnd = index + containsValue.Length;
                index = line.IndexOf(containsValue, lastEnd, StringComparison.Ordinal);
            }
            sb.Append(line, lastEnd, line.Length - lastEnd);
            return sb.ToString();
        }
        return line;
    }

    /// <summary>
    /// Applies yellow highlighting using pre-computed Match object (avoids re-matching)
    /// </summary>
    private string ApplyHighlightingWithMatch(string line, Match match)
    {
        if (!match.Success) return line;

        var sb = new StringBuilder(line.Length + 32);
        int lastEnd = 0;
        while (match.Success)
        {
            sb.Append(line, lastEnd, match.Index - lastEnd);
            sb.Append(AnsiColors.Yellow);
            sb.Append(match.Value);
            sb.Append(AnsiColors.Reset);
            lastEnd = match.Index + match.Length;
            match = match.NextMatch();
        }
        sb.Append(line, lastEnd, line.Length - lastEnd);
        return sb.ToString();
    }

    /// <summary>
    /// Enumerates files recursively from directories or returns files directly
    /// </summary>
    private IEnumerable<ResolvedFileInfo> EnumerateFilesRecursively(string[]? path, string[]? literalPath)
    {
        string[] inputPaths = path ?? literalPath ?? [];

        foreach (var inputPath in inputPaths)
        {
            string resolvedPath;
            try
            {
                resolvedPath = GetUnresolvedProviderPathFromPSPath(inputPath);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "PathResolutionFailed", ErrorCategory.InvalidArgument, inputPath));
                continue;
            }

            if (Directory.Exists(resolvedPath))
            {
                // Directory: enumerate all files recursively
                foreach (var filePath in EnumerateFilesSkippingGit(resolvedPath, "*"))
                {
                    yield return new ResolvedFileInfo
                    {
                        InputPath = inputPath,
                        ResolvedPath = filePath,
                        IsNewFile = false
                    };
                }
            }
            else if (File.Exists(resolvedPath))
            {
                // File: return directly
                yield return new ResolvedFileInfo
                {
                    InputPath = inputPath,
                    ResolvedPath = resolvedPath,
                    IsNewFile = false
                };
            }
            else
            {
                // Check if path contains wildcard pattern (e.g., *.cs -> C:\path\*.cs)
                var directory = System.IO.Path.GetDirectoryName(resolvedPath);
                var filePattern = System.IO.Path.GetFileName(resolvedPath);

                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory) &&
                    !string.IsNullOrEmpty(filePattern) && (filePattern.Contains('*') || filePattern.Contains('?')))
                {
                    // Wildcard: enumerate matching files recursively
                    foreach (var filePath in EnumerateFilesSkippingGit(directory, filePattern))
                    {
                        yield return new ResolvedFileInfo
                        {
                            InputPath = inputPath,
                            ResolvedPath = filePath,
                            IsNewFile = false
                        };
                    }
                }
                else
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException($"Path not found: {inputPath}"),
                        "PathNotFound",
                        ErrorCategory.ObjectNotFound,
                        inputPath));
                }
            }
        }
    }

    /// <summary>
    /// Recursive file walk for -Recurse that does not descend into .git:
    /// its object store is binary and its text files (refs, logs, index
    /// state) are never what a search of a working tree is after.
    /// A directory that cannot be read is reported and skipped; the walk
    /// continues with the rest.
    /// </summary>
    private IEnumerable<string> EnumerateFilesSkippingGit(string root, string searchPattern)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            var dir = pending.Dequeue();
            string[] files, subdirs;
            try
            {
                files = Directory.GetFiles(dir, searchPattern);
                subdirs = Directory.GetDirectories(dir);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "DirectoryEnumerationFailed", ErrorCategory.ReadError, dir));
                continue;
            }

            foreach (var file in files)
                yield return file;

            foreach (var subdir in subdirs)
            {
                if (!string.Equals(System.IO.Path.GetFileName(subdir), ".git", StringComparison.OrdinalIgnoreCase))
                    pending.Enqueue(subdir);
            }
        }
    }

    /// <summary>
    /// Binary test for -Recurse, the one git and ripgrep use: a NUL byte in
    /// the first 8000 bytes. A UTF-16/32 BOM means the NULs are text.
    /// On a read failure the file is treated as text, so the normal read
    /// reports the error.
    /// </summary>
    private static bool IsBinaryFile(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[8000];
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF)))
                return false;
            if (read >= 4 && buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0xFE && buffer[3] == 0xFF)
                return false;
            return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
        }
        catch
        {
            return false;
        }
    }
}
