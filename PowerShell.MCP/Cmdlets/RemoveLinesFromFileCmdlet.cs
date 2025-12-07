using System.Management.Automation;
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

    protected override void BeginProcessing()
    {
        // Check for simultaneous -Contains and -Pattern specification
        ValidateContainsAndPatternMutuallyExclusive(Contains, Pattern);

        // Check that at least one of LineRange, Contains, or Pattern is specified
        if (LineRange == null && string.IsNullOrEmpty(Contains) && string.IsNullOrEmpty(Pattern))
        {
            throw new PSArgumentException("At least one of -LineRange, -Contains, or -Pattern must be specified.");
        }

        // Error if Contains includes newline (processing is line-by-line)
        if (!string.IsNullOrEmpty(Contains) && (Contains.Contains('\n') || Contains.Contains('\r')))
        {
            throw new PSArgumentException("Contains cannot contain newline characters. Remove-LinesFromFile processes files line by line.");
        }

        // Error if Pattern includes newline (processing is line-by-line)
        if (!string.IsNullOrEmpty(Pattern) && (Pattern.Contains('\n') || Pattern.Contains('\r')))
        {
            throw new PSArgumentException("Pattern cannot contain newline characters. Remove-LinesFromFile processes files line by line.");
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
                var metadata = TextFileUtility.DetectFileMetadata(fileInfo.ResolvedPath, Encoding);
                // Tail N lines deletion mode (negative LineRange)
                if (LineRange != null && LineRange.Length > 0 && LineRange[0] < 0)
                {
                    RemoveTailLines(fileInfo.InputPath, fileInfo.ResolvedPath, metadata, -LineRange[0]);
                    continue;
                }

                int startLine = int.MaxValue;
                int endLine = int.MaxValue;
                Regex? regex = null;

                // Prepare deletion conditions
                bool useLineRange = LineRange != null;
                bool useContains = !string.IsNullOrEmpty(Contains);
                bool usePattern = !string.IsNullOrEmpty(Pattern);

                string actionDescription;
                if (useLineRange && useContains)
                {
                    (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                    actionDescription = $"Remove lines {startLine}-{endLine} containing: {Contains}";
                }
                else if (useLineRange && usePattern)
                {
                    (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                    regex = new Regex(Pattern!, RegexOptions.Compiled);
                    actionDescription = $"Remove lines {startLine}-{endLine} matching pattern: {Pattern}";
                }
                else if (useLineRange)
                {
                    (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                    actionDescription = $"Remove lines {startLine}-{endLine}";
                }
                else if (useContains)
                {
                    actionDescription = $"Remove lines containing: {Contains}";
                }
                else // usePattern only
                {
                    regex = new Regex(Pattern!, RegexOptions.Compiled);
                    actionDescription = $"Remove lines matching pattern: {Pattern}";
                }

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

                                // Condition check
                                if (useLineRange && useContains)
                                {
                                    shouldRemove = (lineNumber >= startLine && lineNumber <= endLine) &&
                                                  currentLine.Contains(Contains!);
                                }
                                else if (useLineRange && usePattern)
                                {
                                    shouldRemove = (lineNumber >= startLine && lineNumber <= endLine) &&
                                                  regex!.IsMatch(currentLine);
                                }
                                else if (useLineRange)
                                {
                                    shouldRemove = lineNumber >= startLine && lineNumber <= endLine;
                                }
                                else if (useContains)
                                {
                                    shouldRemove = currentLine.Contains(Contains!);
                                }
                                else // usePattern only
                                {
                                    shouldRemove = regex!.IsMatch(currentLine);
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
                                        if (useContains)
                                        {
                                            // Contains: highlight match in yellow background
                                            displayLine = currentLine.Replace(Contains!, 
                                                $"{AnsiColors.RedOnYellow}{Contains}{AnsiColors.RedOnDefault}");
                                        }
                                        else if (usePattern)
                                        {
                                            // Pattern: highlight regex match in yellow background
                                            displayLine = regex!.Replace(currentLine, 
                                                match => $"{AnsiColors.RedOnYellow}{match.Value}{AnsiColors.RedOnDefault}");
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
