using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Remove lines from file
/// LLM optimized: delete by line range, string containment, or regex match (combinations supported)
/// </summary>
[Cmdlet(VerbsCommon.Remove, "LinesFromFile", SupportsShouldProcess = true)]
public class RemoveLinesFromFileCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Contains { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    private bool _isMultilineContains = false;
    private Regex? _compiledRegex = null;

    protected override void BeginProcessing()
    {
        // Contains and Pattern can be combined (OR condition), same as Show-TextFile

        // Check that at least one of LineRange, Contains, or Pattern is specified
        if (LineRange == null && string.IsNullOrEmpty(Contains) && string.IsNullOrEmpty(Pattern))
        {
            throw new PSArgumentException("At least one of -LineRange, -Contains, or -Pattern must be specified.");
        }

        // Error if Pattern includes newline (regex multiline not supported)
        ValidateNoNewlines(Pattern, "Pattern");
        // Contains with newlines is allowed (multiline mode)

        // Detect multiline Contains mode
        _isMultilineContains = !string.IsNullOrEmpty(Contains) && (Contains.Contains('\n') || Contains.Contains('\r'));

        // multiline Contains + Pattern is not supported
        if (_isMultilineContains && !string.IsNullOrEmpty(Pattern))
        {
            throw new PSArgumentException("Multi-line -Contains cannot be combined with -Pattern.");
        }

        // Pre-compile regex for performance (used across all files)
        // Combine Contains and Pattern with OR if both specified (single-line mode only)
        if (!_isMultilineContains)
        {
            bool useContains = !string.IsNullOrEmpty(Contains);
            bool usePattern = !string.IsNullOrEmpty(Pattern);
            if (useContains && usePattern)
            {
                _compiledRegex = new Regex(Regex.Escape(Contains!) + "|" + Pattern!, RegexOptions.Compiled);
            }
            else if (usePattern)
            {
                _compiledRegex = new Regex(Pattern!, RegexOptions.Compiled);
            }
        }

        // LineRange validation
        if (LineRange != null)
        {
            ValidateLineRange(LineRange);
        }
        // Tail N lines deletion mode (negative LineRange) combined with Contains/Pattern is not supported
        if (LineRange != null && LineRange.Length > 0 && LineRange[0] < 0)
        {
            if (!string.IsNullOrEmpty(Contains) || !string.IsNullOrEmpty(Pattern))
            {
                throw new PSArgumentException("Negative LineRange (tail removal) cannot be combined with -Contains or -Pattern.");
            }
        }
    }

    protected override void ProcessRecord()
    {
        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
        {
            try
            {
                // Multiline Contains: whole-file mode
                if (_isMultilineContains)
                {
                    RemoveMultilineContains(fileInfo.InputPath, fileInfo.ResolvedPath);
                    continue;
                }

                var metadata = TextFileUtility.DetectFileMetadata(fileInfo.ResolvedPath, Encoding);
                // Tail N lines deletion mode (negative LineRange)
                if (LineRange != null && LineRange.Length > 0 && LineRange[0] < 0)
                {
                    RemoveTailLines(fileInfo.InputPath, fileInfo.ResolvedPath, metadata, -LineRange[0]);
                    continue;
                }

                int startLine = int.MaxValue;
                int endLine = int.MaxValue;

                // Prepare deletion conditions
                bool useLineRange = LineRange != null;
                bool useContains = !string.IsNullOrEmpty(Contains);
                bool usePattern = !string.IsNullOrEmpty(Pattern);

                if (useLineRange)
                {
                    (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                }

                // Build action description
                string actionDescription;
                string matchDesc = useContains && usePattern
                    ? $"containing '{Contains}' or matching: {Pattern}"
                    : useContains ? $"containing: {Contains}"
                    : usePattern ? $"matching pattern: {Pattern}"
                    : "";
                if (useLineRange && (useContains || usePattern))
                    actionDescription = $"Remove lines {startLine}-{endLine} {matchDesc}";
                else if (useLineRange)
                    actionDescription = $"Remove lines {startLine}-{endLine}";
                else
                    actionDescription = $"Remove lines {matchDesc}";

                bool isWhatIf = IsWhatIfMode();

                // Confirm with ShouldProcess (-Confirm and -WhatIf handling)
                if (!ShouldProcess(fileInfo.ResolvedPath, actionDescription))
                {
                    if (!isWhatIf)
                    {
                        // -Confirm No: exit without displaying anything
                        continue;
                    }
                    // Continue for -WhatIf to display diff preview
                }

                bool dryRun = isWhatIf;

                if (Backup && !dryRun)
                {
                    var backupPath = TextFileUtility.CreateBackup(fileInfo.ResolvedPath);
                    WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
                }

                string? tempFile = dryRun ? null : System.IO.Path.GetTempFileName();
                int linesRemoved = 0;

                // For context display (rotate buffer)
                // dryRun: line number before deletion, normal: line number after deletion
                var preContextBuffer = new RotateBuffer<(string line, int lineNum)>(2);
                int afterRemovalCounter = 0;
                int lastOutputLine = 0;
                bool headerPrinted = false;
                try
                {
                    // Check for empty file
                    var fileInfoObj = new FileInfo(fileInfo.ResolvedPath);
                    if (fileInfoObj.Length == 0)
                    {
                        WriteWarning("File is empty. Nothing to remove.");
                        if (tempFile != null) File.Delete(tempFile);
                        continue;
                    }

                    using (var enumerator = File.ReadLines(fileInfo.ResolvedPath, metadata.Encoding).GetEnumerator())
                    {
                        StreamWriter? writer = null;
                        try
                        {
                            if (!dryRun && tempFile != null)
                            {
                                writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536);
                                writer.NewLine = metadata.NewlineSequence;
                            }

                            if (!enumerator.MoveNext())
                            {
                                throw new InvalidOperationException("Unexpected empty file");
                            }

                            int lineNumber = 1;        // Line number before deletion
                            int outputLineNumber = 1;  // Line number after deletion
                            string currentLine = enumerator.Current;
                            bool hasNext = enumerator.MoveNext();
                            bool isFirstOutputLine = true;
                            bool wasRemoving = false;

                            while (true)
                            {
                                bool shouldRemove = false;
                                Match? currentMatch = null;

                                // Condition check (unified: LineRange is AND, Contains/Pattern is OR via regex)
                                bool inRange = !useLineRange || (lineNumber >= startLine && lineNumber <= endLine);
                                if (_compiledRegex != null)
                                {
                                    // Pattern, or Contains+Pattern combined as regex
                                    currentMatch = _compiledRegex.Match(currentLine);
                                    shouldRemove = inRange && currentMatch.Success;
                                }
                                else if (useContains)
                                {
                                    // Contains only (no regex needed)
                                    shouldRemove = inRange && currentLine.Contains(Contains!);
                                }
                                else
                                {
                                    // LineRange only
                                    shouldRemove = inRange;
                                }

                                // Detect start of deletion range
                                if (!wasRemoving && shouldRemove)
                                {
                                    if (!headerPrinted)
                                    {
                                        var displayPath = GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath);
                                        WriteObject(AnsiColors.Header(displayPath));
                                        headerPrinted = true;
                                    }

                                    // Determine line number for display
                                    int contextLineNum = dryRun ? lineNumber : outputLineNumber;

                                    // Gap detection: if more than 2 lines from last output
                                    if (lastOutputLine > 0 && contextLineNum - 2 > lastOutputLine + 1)
                                    {
                                        WriteObject("");
                                    }

                                    // Output previous 2 lines as context
                                    foreach (var ctx in preContextBuffer)
                                    {
                                        int ctxDisplayNum = dryRun ? ctx.lineNum : ctx.lineNum - linesRemoved;
                                        if (ctxDisplayNum > lastOutputLine)
                                        {
                                            WriteObject($"{ctxDisplayNum,3}- {ctx.line}");
                                            lastOutputLine = ctxDisplayNum;
                                        }
                                    }
                                }

                                // Detect end of deletion range
                                if (wasRemoving && !shouldRemove)
                                {
                                    afterRemovalCounter = 2;
                                }

                                if (shouldRemove)
                                {
                                    if (dryRun)
                                    {
                                        // -WhatIf: display deleted lines in red (highlight match in yellow background)
                                        string displayLine;
                                        if (_compiledRegex != null)
                                        {
                                            // Pattern or Contains+Pattern combined: highlight regex match in yellow background
                                            var sb = new StringBuilder(currentLine.Length + 32);
                                            int lastEnd = 0;
                                            var match = currentMatch!;
                                            while (match.Success)
                                            {
                                                sb.Append(currentLine, lastEnd, match.Index - lastEnd);
                                                sb.Append(AnsiColors.RedOnYellow);
                                                sb.Append(match.Value);
                                                sb.Append(AnsiColors.RedOnDefault);
                                                lastEnd = match.Index + match.Length;
                                                match = match.NextMatch();
                                            }
                                            sb.Append(currentLine, lastEnd, currentLine.Length - lastEnd);
                                            displayLine = sb.ToString();
                                        }
                                        else if (useContains)
                                        {
                                            // Contains only: highlight match in yellow background
                                            displayLine = currentLine.Replace(Contains!,
                                                $"{AnsiColors.RedOnYellow}{Contains}{AnsiColors.RedOnDefault}");
                                        }
                                        else
                                        {
                                            // LineRange only: display entire line in red
                                            displayLine = currentLine;
                                        }
                                        WriteObject($"{lineNumber,3}: {AnsiColors.Red}{displayLine}{AnsiColors.Reset}");
                                        lastOutputLine = lineNumber;
                                    }
                                    else
                                    {
                                        // Normal execution: display position marker only for first of consecutive deletions (no line number)
                                        if (!wasRemoving)
                                        {
                                            WriteObject("   :");
                                        }
                                    }
                                    linesRemoved++;
                                }
                                else
                                {
                                    // Non-deleted line: write (only if not dryRun)
                                    if (writer != null)
                                    {
                                        if (!isFirstOutputLine)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                        writer.Write(currentLine);
                                        isFirstOutputLine = false;
                                    }

                                    // Output 2 lines after deletion as context
                                    if (afterRemovalCounter > 0)
                                    {
                                        int displayNum = dryRun ? lineNumber : outputLineNumber;
                                        WriteObject($"{displayNum,3}- {currentLine}");
                                        lastOutputLine = displayNum;
                                        afterRemovalCounter--;
                                    }

                                    // Update rotate buffer
                                    preContextBuffer.Add((currentLine, lineNumber));

                                    outputLineNumber++;
                                }

                                wasRemoving = shouldRemove;

                                if (hasNext)
                                {
                                    lineNumber++;
                                    currentLine = enumerator.Current;
                                    hasNext = enumerator.MoveNext();
                                }
                                else
                                {
                                    // Process final line
                                    if (writer != null && !shouldRemove && metadata.HasTrailingNewline)
                                    {
                                        writer.Write(metadata.NewlineSequence);
                                    }
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            writer?.Dispose();
                        }
                    }

                    if (linesRemoved == 0)
                    {
                        WriteWarning("No lines matched. File not modified.");
                        if (tempFile != null) File.Delete(tempFile);
                        continue;
                    }

                    // Separate context and summary with empty line
                    WriteObject("");

                    if (dryRun)
                    {
                        // WhatIf: do not modify file
                        WriteObject(AnsiColors.WhatIf($"What if: Would remove {linesRemoved} line(s) from {GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath)}"));
                    }
                    else
                    {
                        // Replace atomically
                        TextFileUtility.ReplaceFileAtomic(fileInfo.ResolvedPath, tempFile!);
                        WriteObject(AnsiColors.Success($"Removed {linesRemoved} line(s) from {GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath)} (net: -{linesRemoved})"));
                    }
                }
                catch
                {
                    if (tempFile != null && File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                    throw;
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "RemoveLineFailed", ErrorCategory.WriteError, fileInfo.ResolvedPath));
            }
        }
    }

    /// <summary>
    /// Process multiline literal string removal (whole-file mode)
    /// Used when Contains contains newline characters
    /// </summary>
    private void RemoveMultilineContains(string inputPath, string resolvedPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);

        // Check for empty file
        var fileInfoObj = new FileInfo(resolvedPath);
        if (fileInfoObj.Length == 0)
        {
            WriteWarning("File is empty. Nothing to remove.");
            return;
        }

        // Read entire file content
        var content = File.ReadAllText(resolvedPath, metadata.Encoding);

        // Normalize Contains newlines to match file's newline sequence
        var normalizedContains = Contains!
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", metadata.NewlineSequence);

        // Find all occurrences and filter by LineRange
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
        var allLines = content.Split(metadata.NewlineSequence);

        // Build line start offsets for line number calculation
        var lineStartOffsets = new int[allLines.Length];
        int offset = 0;
        for (int i = 0; i < allLines.Length; i++)
        {
            lineStartOffsets[i] = offset;
            offset += allLines[i].Length + metadata.NewlineSequence.Length;
        }

        // Find all matches and their line ranges
        var matchesToRemove = new List<(int startIdx, int endIdx, int startLine, int endLine)>();
        int searchIdx = 0;
        while ((searchIdx = content.IndexOf(normalizedContains, searchIdx, StringComparison.Ordinal)) >= 0)
        {
            int matchEndIdx = searchIdx + normalizedContains.Length;
            int matchStartLine = TextFileUtility.GetLineNumberFromOffset(lineStartOffsets, searchIdx) + 1;
            int matchEndLine = TextFileUtility.GetLineNumberFromOffset(lineStartOffsets, matchEndIdx > 0 ? matchEndIdx - 1 : matchEndIdx) + 1;

            // LineRange filter
            if (matchStartLine >= startLine && matchStartLine <= endLine)
            {
                matchesToRemove.Add((searchIdx, matchEndIdx, matchStartLine, matchEndLine));
            }
            searchIdx += normalizedContains.Length;
        }

        var displayPath = GetDisplayPath(inputPath, resolvedPath);

        if (matchesToRemove.Count == 0)
        {
            WriteWarning("No matches found. File not modified.");
            return;
        }

        // Calculate total lines to remove
        int totalMatchLines = 0;
        foreach (var m in matchesToRemove)
        {
            totalMatchLines += m.endLine - m.startLine + 1;
        }

        var actionDescription = $"Remove {matchesToRemove.Count} occurrence(s) of multiline text ({totalMatchLines} lines)";

        bool isWhatIf = IsWhatIfMode();
        if (!ShouldProcess(resolvedPath, actionDescription))
        {
            if (!isWhatIf) return;
        }

        bool dryRun = isWhatIf;

        if (!dryRun && Backup)
        {
            var backupPath = TextFileUtility.CreateBackup(resolvedPath);
            WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
        }

        // Display header and context
        WriteObject(AnsiColors.Header(displayPath));

        int lastOutputLine = 0;
        foreach (var (matchStart, matchEnd, matchStartLine, matchEndLine) in matchesToRemove)
        {
            // Context: 2 lines before
            int contextStart = Math.Max(1, matchStartLine - 2);
            // Context: 2 lines after
            int contextEnd = Math.Min(allLines.Length, matchEndLine + 2);

            // Gap detection
            if (lastOutputLine > 0 && contextStart > lastOutputLine + 1)
            {
                WriteObject("");
            }

            // Pre-context lines
            for (int ln = contextStart; ln < matchStartLine; ln++)
            {
                if (ln > lastOutputLine)
                {
                    WriteObject($"{ln,3}- {allLines[ln - 1]}");
                    lastOutputLine = ln;
                }
            }

            // Match lines
            if (dryRun)
            {
                // WhatIf: show matched lines in red with match highlighted
                for (int ln = matchStartLine; ln <= matchEndLine && ln <= allLines.Length; ln++)
                {
                    if (ln > lastOutputLine)
                    {
                        WriteObject($"{ln,3}: {AnsiColors.Red}{allLines[ln - 1]}{AnsiColors.Reset}");
                        lastOutputLine = ln;
                    }
                }
            }
            else
            {
                // Normal: position marker
                if (matchStartLine > lastOutputLine)
                {
                    WriteObject("   :");
                    lastOutputLine = matchEndLine;
                }
            }

            // Post-context lines
            for (int ln = matchEndLine + 1; ln <= contextEnd; ln++)
            {
                if (ln > lastOutputLine)
                {
                    WriteObject($"{ln,3}- {allLines[ln - 1]}");
                    lastOutputLine = ln;
                }
            }
        }

        WriteObject("");

        if (dryRun)
        {
            WriteObject(AnsiColors.WhatIf($"What if: Would remove {matchesToRemove.Count} occurrence(s) ({totalMatchLines} lines) from {displayPath}"));
        }
        else
        {
            // Perform removal: forward-copy segments between matches (O(M) with StringBuilder)
            var sb = new StringBuilder(content.Length);
            int lastEnd = 0;
            foreach (var (startIdx, endIdx, _, _) in matchesToRemove)
            {
                sb.Append(content, lastEnd, startIdx - lastEnd);
                lastEnd = endIdx;
            }
            sb.Append(content, lastEnd, content.Length - lastEnd);
            var newContent = sb.ToString();

            // Write atomically
            var tempFile = System.IO.Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, newContent, metadata.Encoding);
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);
                WriteObject(AnsiColors.Success($"Removed {matchesToRemove.Count} occurrence(s) ({totalMatchLines} lines) from {displayPath}"));
            }
            catch
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                throw;
            }
        }
    }

    /// <summary>
    /// Delete last N lines (delayed write pattern using RotateBuffer)
    /// </summary>
    private void RemoveTailLines(string inputPath, string resolvedPath, TextFileUtility.FileMetadata metadata, int tailCount)
    {
        string actionDescription = $"Remove last {tailCount} line(s)";
        bool isWhatIf = IsWhatIfMode();

        if (!ShouldProcess(resolvedPath, actionDescription))
        {
            if (!isWhatIf)
            {
                return;
            }
        }

        bool dryRun = isWhatIf;

        if (Backup && !dryRun)
        {
            var backupPath = TextFileUtility.CreateBackup(resolvedPath);
            WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
        }

        string? tempFile = dryRun ? null : System.IO.Path.GetTempFileName();

        try
        {
            // Check for empty file
            var fileInfoObj = new FileInfo(resolvedPath);
            if (fileInfoObj.Length == 0)
            {
                WriteWarning("File is empty. Nothing to remove.");
                if (tempFile != null) File.Delete(tempFile);
                return;
            }

            // RotateBuffer: hold last N lines (for delayed write)
            var tailBuffer = new RotateBuffer<(string line, int lineNum)>(tailCount);
            int totalLines = 0;
            int linesWritten = 0;

            using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
            {
                StreamWriter? writer = null;
                try
                {
                    if (!dryRun && tempFile != null)
                    {
                        writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536);
                        writer.NewLine = metadata.NewlineSequence;
                    }

                    bool isFirstOutputLine = true;

                    while (enumerator.MoveNext())
                    {
                        totalLines++;
                        string currentLine = enumerator.Current;

                        // If buffer is full, write out oldest line
                        if (tailBuffer.IsFull)
                        {
                            var oldest = tailBuffer.Oldest;
                            if (writer != null)
                            {
                                if (!isFirstOutputLine)
                                {
                                    writer.Write(metadata.NewlineSequence);
                                }
                                writer.Write(oldest.line);
                                isFirstOutputLine = false;
                            }
                            linesWritten++;
                        }

                        // Add current line to buffer
                        tailBuffer.Add((currentLine, totalLines));
                    }

                    // Handle trailing newline
                    if (writer != null && linesWritten > 0 && metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                }
                finally
                {
                    writer?.Dispose();
                }
            }

            // Number of lines to delete
            int linesRemoved = tailBuffer.Count;

            if (linesRemoved == 0)
            {
                WriteWarning("File has no lines to remove.");
                if (tempFile != null) File.Delete(tempFile);
                return;
            }

            // Context display
            var displayPath = GetDisplayPath(inputPath, resolvedPath);
            WriteObject(AnsiColors.Header(displayPath));

            // Context before deleted lines (max 2 lines)
            int contextStart = totalLines - linesRemoved - 1;
            if (contextStart >= 1)
            {
                int contextCount = Math.Min(2, contextStart);
                int contextLineNum = totalLines - linesRemoved - contextCount + 1;

                // Re-read to get pre-context
                foreach (var line in File.ReadLines(resolvedPath, metadata.Encoding).Skip(contextLineNum - 1).Take(contextCount))
                {
                    WriteObject($"{contextLineNum,3}- {line}");
                    contextLineNum++;
                }
            }

            // Display lines to be deleted
            if (dryRun)
            {
                // WhatIf: display deleted lines in red
                foreach (var item in tailBuffer)
                {
                    WriteObject($"{item.lineNum,3}: {AnsiColors.Red}{item.line}{AnsiColors.Reset}");
                }
            }
            else
            {
                // Normal execution: position marker only
                WriteObject("   :");
            }

            // Separate context and summary with empty line
            WriteObject("");

            if (dryRun)
            {
                WriteObject(AnsiColors.WhatIf($"What if: Would remove {linesRemoved} line(s) from {displayPath}"));
            }
            else
            {
                // Replace atomically
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile!);
                WriteObject(AnsiColors.Success($"Removed {linesRemoved} line(s) from {displayPath} (net: -{linesRemoved})"));
            }
        }
        catch
        {
            if (tempFile != null && File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            throw;
        }
    }
}
