using System.Management.Automation;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Update pattern matches in text file
/// LLM optimized: two modes - literal string replacement and regex replacement
/// </summary>
[Cmdlet(VerbsData.Update, "MatchInFile", SupportsShouldProcess = true)]
public class UpdateMatchInFileCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter][Alias("Contains")]
    public string? OldText { get; set; }

    [Parameter]
    public string? Pattern { get; set; }

    [Parameter][Alias("NewText")]
    public string? Replacement { get; set; }

    [Parameter]
    [ValidateLineRange]
    public string[]? LineRange { get; set; }

    [Parameter]
    public int? Skip { get; set; }

    [Parameter]
    public int? First { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        // -Skip/-First compatibility: map to -LineRange
        LineRange = MapSkipFirstToLineRange(Skip, First, LineRange);
        bool hasLiteral = !string.IsNullOrEmpty(OldText);
        bool hasRegex = !string.IsNullOrEmpty(Pattern);

        // Neither specified
        if (!hasLiteral && !hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Either -OldText/-Replacement or -Pattern/-Replacement must be specified."),
                "ParameterRequired",
                ErrorCategory.InvalidArgument,
                null));
        }

        // Both specified
        if (hasLiteral && hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Cannot specify both -OldText/-Replacement and -Pattern/-Replacement."),
                "ConflictingParameters",
                ErrorCategory.InvalidArgument,
                null));
        }

        // Literal mode with only one specified
        if (hasLiteral && Replacement == null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -OldText and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }

        // Error if Pattern includes newline (regex multiline not supported yet)
        // OldText multiline is supported via whole-file replacement mode

        // Regex mode with only one specified
        if (hasRegex && Replacement == null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -Pattern and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }

        ValidateNoNewlines(Pattern, "Pattern");
    }

    protected override void ProcessRecord()
    {
        // LineRange validation
        ValidateLineRange(LineRange);

        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
        {
            try
            {
                bool isMultiline = !string.IsNullOrEmpty(OldText) && (OldText.Contains('\n') || OldText.Contains('\r'));
                if (isMultiline)
                    ProcessMultilineReplacement(fileInfo.InputPath, fileInfo.ResolvedPath);
                else
                    ProcessStringReplacement(fileInfo.InputPath, fileInfo.ResolvedPath);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "UpdateFailed", ErrorCategory.WriteError, fileInfo.ResolvedPath));
            }
        }
    }

    /// <summary>
    /// Process literal string or regex replacement (single pass implementation)
    /// Reads file once while performing match check, replacement, and context display simultaneously
    /// </summary>
    private void ProcessStringReplacement(string originalPath, string resolvedPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);

        // If Replacement contains non-ASCII chars, upgrade encoding to UTF-8
        if (!string.IsNullOrEmpty(Replacement) &&
            EncodingHelper.TryUpgradeEncodingIfNeeded(metadata, [Replacement], Encoding != null, out var upgradeMessage))
        {
            WriteInformation(upgradeMessage, ["EncodingUpgrade"]);
        }

        // Normalize newlines in Replacement to match file's newline sequence
        var normalizedReplacement = Replacement ?? "";
        if (normalizedReplacement.Contains('\n') || normalizedReplacement.Contains('\r'))
        {
            normalizedReplacement = normalizedReplacement
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", metadata.NewlineSequence);
        }

        var isLiteral = !string.IsNullOrEmpty(OldText);
        var regex = isLiteral ? null : new Regex(Pattern!, RegexOptions.Compiled);

        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // More specific action description
        string actionDescription;
        if (isLiteral)
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace '{OldText}' with '{Replacement}'{rangeInfo}";
        }
        else
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace pattern '{Pattern}' with '{Replacement}'{rangeInfo}";
        }

        bool isWhatIf = IsWhatIfMode();

        // Confirm with ShouldProcess (-Confirm and -WhatIf handling)
        if (!ShouldProcess(resolvedPath, actionDescription))
        {
            // If -Confirm No was selected, or -WhatIf
            if (!isWhatIf)
            {
                // -Confirm No: exit without displaying anything
                return;
            }
            // Continue for -WhatIf to display diff preview
        }

        bool dryRun = isWhatIf;

        // Backup (only if not dryRun)
        if (!dryRun && Backup)
        {
            var backupPath = TextFileUtility.CreateBackup(resolvedPath);
            WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
        }

        // ===== Single pass: match check, replacement, and context display simultaneously =====
        string? tempFile = dryRun ? null : TextFileUtility.CreateTempFileNextTo(resolvedPath);
        int replacementCount = 0;
        bool headerPrinted = false;

        // For context display
        var preContextBuffer = new RotateBuffer<(string line, int lineNum)>(2);
        int afterMatchCounter = 0;
        int lastOutputLine = 0;
        // Lines added by earlier replacements whose text contains newlines.
        // lineNumber/lastOutputLine count lines of the original file (gap
        // detection); displayed numbers add this so they match the result.
        int lineOffset = 0;

        try
        {
            // Check for empty file
            var fileInfoObj = new FileInfo(resolvedPath);
            if (fileInfoObj.Length == 0)
            {
                if (dryRun)
                {
                    WriteWarning("File is empty. Nothing to replace.");
                }
                else
                {
                    WriteObject(AnsiColors.Info($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made"));
                }
                TextFileUtility.TryDeleteQuietly(tempFile);
                return;
            }

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

                    if (!enumerator.MoveNext())
                    {
                        throw new InvalidOperationException("Unexpected empty file");
                    }

                    int lineNumber = 1;
                    string currentLine = enumerator.Current;
                    bool hasNext = enumerator.MoveNext();
                    bool isFirstOutputLine = true;

                    while (true)
                    {
                        // Match check
                        bool isMatched = false;
                        MatchCollection? regexMatches = null;
                        if (lineNumber >= startLine && lineNumber <= endLine)
                        {
                            if (isLiteral)
                            {
                                isMatched = currentLine.Contains(OldText!);
                            }
                            else
                            {
                                // Use Matches instead of IsMatch to avoid redundant regex call
                                regexMatches = regex!.Matches(currentLine);
                                isMatched = regexMatches.Count > 0;
                            }
                        }

                        string outputLine = currentLine;

                        if (isMatched)
                        {
                            // Output header (first match only)
                            if (!headerPrinted)
                            {
                                var displayPath = GetDisplayPath(originalPath, resolvedPath);
                                WriteObject(AnsiColors.Header(displayPath));
                                headerPrinted = true;
                            }

                            // Gap detection: if more than 2 lines from last output
                            if (lastOutputLine > 0 && lineNumber - 2 > lastOutputLine + 1)
                            {
                                WriteObject("");
                            }

                            // Output previous 2 lines as context
                            foreach (var ctx in preContextBuffer)
                            {
                                if (ctx.lineNum > lastOutputLine)
                                {
                                    var ctxDisplayLine = BuildContextDisplayLine(ctx.line, isLiteral, regex);
                                    WriteObject($"{ctx.lineNum + lineOffset,3}- {TextFileUtility.ClipContextLine(ctxDisplayLine)}");
                                    lastOutputLine = ctx.lineNum;
                                }
                            }

                            // Execute replacement
                            if (isLiteral)
                            {
                                int count = (currentLine.Length - currentLine.Replace(OldText!, "").Length) /
                                            Math.Max(1, OldText!.Length);
                                replacementCount += count;
                                outputLine = currentLine.Replace(OldText, normalizedReplacement);
                            }
                            else
                            {
                                // Reuse cached matches from match check
                                replacementCount += regexMatches!.Count;
                                outputLine = regex!.Replace(currentLine, normalizedReplacement);
                            }

                            // Display match line (WhatIf: red+green, normal: green only)
                            string displayLine;
                            if (dryRun)
                            {
                                // WhatIf: display both before (red) and after (green) replacement
                                if (isLiteral)
                                {
                                    displayLine = currentLine.Replace(OldText!,
                                        $"{AnsiColors.Red}{OldText}{AnsiColors.Reset}{AnsiColors.Green}{normalizedReplacement}{AnsiColors.Reset}");
                                }
                                else
                                {
                                    displayLine = BuildRegexDisplayLine(currentLine, regex!, normalizedReplacement);
                                }
                            }
                            else
                            {
                                // Normal execution: display only replacement result (highlighted in green)
                                if (isLiteral)
                                {
                                    displayLine = currentLine.Replace(OldText!,
                                        $"{AnsiColors.Green}{normalizedReplacement}{AnsiColors.Reset}");
                                }
                                else
                                {
                                    // Use match.Result(replacement) so $1/$2/etc. capture-group references
                                    // expand the same way Regex.Replace did when writing the file. A naive
                                    // {Green}{normalizedReplacement}{Reset} would show literal "$1" instead
                                    // of the substituted text, so display would diverge from file content.
                                    displayLine = regex!.Replace(currentLine,
                                        match => $"{AnsiColors.Green}{match.Result(normalizedReplacement)}{AnsiColors.Reset}");
                                }
                            }

                            // A replacement containing newlines turns one line into
                            // several: number each one, as Update-LinesInFile does.
                            var displayLines = SplitColoredLines(displayLine, metadata.NewlineSequence);
                            for (int i = 0; i < displayLines.Count; i++)
                            {
                                WriteObject($"{lineNumber + lineOffset + i,3}: {displayLines[i]}");
                            }
                            lineOffset += displayLines.Count - 1;
                            lastOutputLine = lineNumber;
                            afterMatchCounter = 2;
                        }
                        else
                        {
                            // Output subsequent context
                            if (afterMatchCounter > 0)
                            {
                                var displayContextLine = BuildContextDisplayLine(currentLine, isLiteral, regex);
                                WriteObject($"{lineNumber + lineOffset,3}- {TextFileUtility.ClipContextLine(displayContextLine)}");
                                lastOutputLine = lineNumber;
                                afterMatchCounter--;
                            }

                            // Update rotate buffer
                            preContextBuffer.Add((currentLine, lineNumber));
                        }

                        // Write to file (only if not dryRun)
                        if (writer != null)
                        {
                            if (!isFirstOutputLine)
                            {
                                writer.Write(metadata.NewlineSequence);
                            }
                            writer.Write(outputLine);
                            isFirstOutputLine = false;
                        }

                        if (hasNext)
                        {
                            lineNumber++;
                            currentLine = enumerator.Current;
                            hasNext = enumerator.MoveNext();
                        }
                        else
                        {
                            // Process final line
                            if (writer != null && metadata.HasTrailingNewline)
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

            if (replacementCount == 0)
            {
                if (dryRun)
                {
                    WriteWarning("No lines matched. File not modified.");
                }
                else
                {
                    WriteObject(AnsiColors.Info($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made"));
                }
                TextFileUtility.TryDeleteQuietly(tempFile);
                return;
            }

            // Separate context and summary with empty line
            WriteObject("");

            if (dryRun)
            {
                // WhatIf: do not modify file
                WriteObject(AnsiColors.WhatIf($"What if: Would update {GetDisplayPath(originalPath, resolvedPath)}: {replacementCount} replacement(s)"));
            }
            else
            {
                // Replace atomically
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile!);
                WriteObject(AnsiColors.Success($"Updated {GetDisplayPath(originalPath, resolvedPath)}: {replacementCount} replacement(s) made"));
            }
        }
        catch
        {
            TextFileUtility.TryDeleteQuietly(tempFile);
            throw;
        }
    }


    /// <summary>
    /// Build diff display line for regex replacement (deleted in red, added in green)
    /// </summary>
    private static string BuildRegexDisplayLine(string originalLine, Regex regex, string replacement)
    {
        // Display each match deletion->addition consecutively (supports capture groups)
        var result = regex.Replace(originalLine, match =>
        {
            // Get expanded result of $1, $2 etc. with match.Result()
            var replacedText = match.Result(replacement);

            // Display deleted part (original match) in red+strikethrough, added part (replacement result) in green
            return $"{AnsiColors.Red}{match.Value}{AnsiColors.Reset}{AnsiColors.Green}{replacedText}{AnsiColors.Reset}";
        });

        return result;
    }

    /// <summary>
    /// Split a display line at the file's newline sequence, closing a color
    /// span at the end of each piece and reopening it on the next, so every
    /// piece renders on its own with the colors it had in the whole line.
    /// </summary>
    internal static List<string> SplitColoredLines(string text, string newline)
    {
        var pieces = text.Split(newline);
        var result = new List<string>(pieces.Length);
        if (pieces.Length == 1)
        {
            result.Add(text);
            return result;
        }

        string open = "";
        foreach (var piece in pieces)
        {
            var line = open + piece;
            int resetAt = piece.LastIndexOf(AnsiColors.Reset, StringComparison.Ordinal);
            int redAt = piece.LastIndexOf(AnsiColors.Red, StringComparison.Ordinal);
            int greenAt = piece.LastIndexOf(AnsiColors.Green, StringComparison.Ordinal);
            if (redAt > resetAt && redAt > greenAt) open = AnsiColors.Red;
            else if (greenAt > resetAt && greenAt > redAt) open = AnsiColors.Green;
            else if (resetAt >= 0) open = "";
            if (open.Length > 0) line += AnsiColors.Reset;
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// Build context line display (highlight original string in yellow if matched)
    /// </summary>
    private string BuildContextDisplayLine(string line, bool isLiteral, Regex? regex)
    {
        if (isLiteral)
        {
            if (line.Contains(OldText!))
            {
                return line.Replace(OldText!, $"{AnsiColors.Yellow}{OldText}{AnsiColors.Reset}");
            }
        }
        else
        {
            // Use Match instead of IsMatch + Replace to avoid redundant regex call
            var match = regex!.Match(line);
            if (match.Success)
            {
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
        }

        return line;
    }

    /// <summary>
    /// Shows every occurrence of a multi-line replacement the way
    /// Update-LinesInFile shows an edit: up to two context lines on each side
    /// (<c>N-</c>), the original lines the match touched in red with no number,
    /// and the lines that replace them in green with their number in the
    /// result. A match that starts or ends mid-line shows those whole lines.
    /// </summary>
    private void WriteMultilineDiff(string content, string newContent, string oldText, string replacement, string newline)
    {
        const int contextLines = 2;
        var oldLines = content.Split(newline);
        var newLines = newContent.Split(newline);
        // A trailing newline ends the last line; it does not start another
        int newLineCount = newContent.EndsWith(newline, StringComparison.Ordinal) ? newLines.Length - 1 : newLines.Length;
        int oldTextBreaks = CountOccurrences(oldText, newline);
        int replacementBreaks = CountOccurrences(replacement, newline);
        // Whole lines swapped for whole lines: an -OldText that ends with a
        // newline stops at the start of the next line, which is untouched
        // when the replacement also ends with one (or is empty, removing the
        // lines). Counted as a break, that next line showed as both removed
        // and inserted, and at the end of the file a phantom empty line
        // showed as removed.
        bool wholeLines = oldText.EndsWith(newline, StringComparison.Ordinal) &&
                          (replacement.Length == 0 || replacement.EndsWith(newline, StringComparison.Ordinal));
        int oldSpan = wholeLines ? oldTextBreaks - 1 : oldTextBreaks;
        int newSpan = wholeLines ? replacementBreaks - 1 : replacementBreaks;

        // Occurrences in the order string.Replace takes them: left to right,
        // not overlapping. Two that share a line become one hunk.
        var hunks = new List<(int oldStart, int oldEnd, int newStart, int newEnd)>();
        int searchFrom = 0, scanned = 0, oldLine = 1, lineShift = 0, idx;
        while ((idx = content.IndexOf(oldText, searchFrom, StringComparison.Ordinal)) >= 0)
        {
            oldLine += CountOccurrences(content.AsSpan(scanned, idx - scanned), newline);
            scanned = idx;
            var hunk = (oldStart: oldLine, oldEnd: oldLine + oldSpan, newStart: oldLine + lineShift, newEnd: oldLine + lineShift + newSpan);
            if (hunks.Count > 0 && hunk.oldStart <= hunks[^1].oldEnd)
                hunks[^1] = (hunks[^1].oldStart, hunk.oldEnd, hunks[^1].newStart, hunk.newEnd);
            else
                hunks.Add(hunk);
            lineShift += replacementBreaks - oldTextBreaks;
            searchFrom = idx + oldText.Length;
        }

        int lastShown = 0; // last line of the result printed
        for (int h = 0; h < hunks.Count; h++)
        {
            var (oldStart, oldEnd, newStart, newEnd) = hunks[h];

            int preFrom = Math.Max(Math.Max(1, lastShown + 1), newStart - contextLines);
            if (lastShown > 0 && preFrom > lastShown + 1) WriteObject("");
            for (int n = preFrom; n < newStart; n++)
                WriteObject($"{n,3}- {TextFileUtility.ClipContextLine(newLines[n - 1])}");

            for (int o = oldStart; o <= oldEnd && o <= oldLines.Length; o++)
                WriteObject($"   : {AnsiColors.Deleted(oldLines[o - 1])}");
            for (int n = newStart; n <= newEnd && n <= newLineCount; n++)
                WriteObject($"{n,3}: {AnsiColors.Inserted(newLines[n - 1])}");
            lastShown = newEnd;

            // Trailing context stops short of the next hunk's own lines
            int postTo = Math.Min(newLineCount, newEnd + contextLines);
            if (h + 1 < hunks.Count) postTo = Math.Min(postTo, hunks[h + 1].newStart - 1);
            for (int n = newEnd + 1; n <= postTo; n++)
            {
                WriteObject($"{n,3}- {TextFileUtility.ClipContextLine(newLines[n - 1])}");
                lastShown = n;
            }
        }
    }

    private static int CountOccurrences(ReadOnlySpan<char> text, string value)
    {
        int count = 0, i;
        while ((i = text.IndexOf(value, StringComparison.Ordinal)) >= 0)
        {
            count++;
            text = text[(i + value.Length)..];
        }
        return count;
    }

    /// <summary>
    /// Process multiline literal string replacement (whole-file mode)
    /// Used when OldText contains newline characters
    /// </summary>
    private void ProcessMultilineReplacement(string originalPath, string resolvedPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);

        // Read entire file content
        var content = File.ReadAllText(resolvedPath, metadata.Encoding);

        // Normalize OldText newlines to match file's newline sequence
        var normalizedOldText = OldText!
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", metadata.NewlineSequence);

        // Normalize Replacement newlines
        var normalizedReplacement = Replacement ?? "";
        if (normalizedReplacement.Contains('\n') || normalizedReplacement.Contains('\r'))
        {
            normalizedReplacement = normalizedReplacement
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", metadata.NewlineSequence);
        }

        // If Replacement contains non-ASCII chars, upgrade encoding to UTF-8
        if (!string.IsNullOrEmpty(Replacement) &&
            EncodingHelper.TryUpgradeEncodingIfNeeded(metadata, [Replacement], Encoding != null, out var upgradeMessage))
        {
            WriteInformation(upgradeMessage, ["EncodingUpgrade"]);
        }

        // Count occurrences
        int replacementCount = 0;
        int idx = 0;
        while ((idx = content.IndexOf(normalizedOldText, idx, StringComparison.Ordinal)) >= 0)
        {
            replacementCount++;
            idx += normalizedOldText.Length;
        }

        var displayPath = GetDisplayPath(originalPath, resolvedPath);

        if (replacementCount == 0)
        {
            WriteObject(AnsiColors.Info($"{displayPath}: 0 replacement(s) made"));
            return;
        }

        // Build action description
        var actionDescription = $"Replace multiline text ({replacementCount} occurrence(s))";

        bool isWhatIf = IsWhatIfMode();
        if (!ShouldProcess(resolvedPath, actionDescription))
        {
            if (!isWhatIf) return;
        }

        bool dryRun = isWhatIf;

        // Backup
        if (!dryRun && Backup)
        {
            var backupPath = TextFileUtility.CreateBackup(resolvedPath);
            WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
        }

        // Perform replacement
        var newContent = content.Replace(normalizedOldText, normalizedReplacement);

        // Display header and diff context
        WriteObject(AnsiColors.Header(displayPath));

        WriteMultilineDiff(content, newContent, normalizedOldText, normalizedReplacement, metadata.NewlineSequence);

        WriteObject("");

        if (dryRun)
        {
            WriteObject(AnsiColors.WhatIf($"What if: Would update {displayPath}: {replacementCount} replacement(s)"));
        }
        else
        {
            // Write atomically
            var tempFile = TextFileUtility.CreateTempFileNextTo(resolvedPath);
            try
            {
                File.WriteAllText(tempFile, newContent, metadata.Encoding);
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);
                WriteObject(AnsiColors.Success($"Updated {displayPath}: {replacementCount} replacement(s) made"));
            }
            catch
            {
                TextFileUtility.TryDeleteQuietly(tempFile);
                throw;
            }
        }
    }
}
