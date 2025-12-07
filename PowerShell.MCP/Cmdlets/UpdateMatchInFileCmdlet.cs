using System.Management.Automation;
using System.Text.RegularExpressions;
using System.IO;

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

    [Parameter]
    public string? Contains { get; set; }

    [Parameter]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Replacement { get; set; }

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        bool hasLiteral = !string.IsNullOrEmpty(Contains);
        bool hasRegex = !string.IsNullOrEmpty(Pattern);
        
        // Neither specified
        if (!hasLiteral && !hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Either -Contains/-Replacement or -Pattern/-Replacement must be specified."),
                "ParameterRequired",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // Both specified
        if (hasLiteral && hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Cannot specify both -Contains/-Replacement and -Pattern/-Replacement."),
                "ConflictingParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // Literal mode with only one specified
        if (hasLiteral && Replacement == null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -Contains and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }

        // Error if Contains includes newline (processing is line-by-line)
        if (hasLiteral && (Contains!.Contains('\n') || Contains.Contains('\r')))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Contains cannot contain newline characters. Update-MatchInFile processes files line by line."),
                "InvalidContains",
                ErrorCategory.InvalidArgument,
                Contains));
        }
        
        // Regex mode with only one specified
        if (hasRegex && Replacement == null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -Pattern and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }

        // Error if Pattern includes newline (processing is line-by-line)
        if (hasRegex && (Pattern!.Contains('\n') || Pattern.Contains('\r')))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Pattern cannot contain newline characters. Update-MatchInFile processes files line by line."),
                "InvalidPattern",
                ErrorCategory.InvalidArgument,
                Pattern));
        }
    }

    protected override void ProcessRecord()
    {
        // LineRange validation
        ValidateLineRange(LineRange);

        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
        {
            try
            {
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
        
        var isLiteral = !string.IsNullOrEmpty(Contains);
        var regex = isLiteral ? null : new Regex(Pattern!, RegexOptions.Compiled);
        
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // More specific action description
        string actionDescription;
        if (isLiteral)
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace '{Contains}' with '{Replacement}'{rangeInfo}";
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
        string? tempFile = dryRun ? null : System.IO.Path.GetTempFileName();
        int replacementCount = 0;
        bool headerPrinted = false;
        
        // For context display
        var preContextBuffer = new RotateBuffer<(string line, int lineNum)>(2);
        int afterMatchCounter = 0;
        int lastOutputLine = 0;
        

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
                if (tempFile != null) File.Delete(tempFile);
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
                        if (lineNumber >= startLine && lineNumber <= endLine)
                        {
                            if (isLiteral)
                            {
                                isMatched = currentLine.Contains(Contains!);
                            }
                            else
                            {
                                isMatched = regex!.IsMatch(currentLine);
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
                                    WriteObject($"{ctx.lineNum,3}- {ctxDisplayLine}");
                                    lastOutputLine = ctx.lineNum;
                                }
                            }

                            // Execute replacement
                            if (isLiteral)
                            {
                                int count = (currentLine.Length - currentLine.Replace(Contains!, "").Length) / 
                                            Math.Max(1, Contains!.Length);
                                replacementCount += count;
                                outputLine = currentLine.Replace(Contains, Replacement);
                            }
                            else
                            {
                                var matches = regex!.Matches(currentLine);
                                replacementCount += matches.Count;
                                outputLine = regex.Replace(currentLine, Replacement!);
                            }

                            // Display match line (WhatIf: red+green, normal: green only)
                            string displayLine;
                            if (dryRun)
                            {
                                // WhatIf: display both before (red) and after (green) replacement
                                if (isLiteral)
                                {
                                    displayLine = currentLine.Replace(Contains!, 
                                        $"{AnsiColors.Red}{Contains}{AnsiColors.Reset}{AnsiColors.Green}{Replacement}{AnsiColors.Reset}");
                                }
                                else
                                {
                                    displayLine = BuildRegexDisplayLine(currentLine, regex!, Replacement!);
                                }
                            }
                            else
                            {
                                // Normal execution: display only replacement result (highlighted in green)
                                if (isLiteral)
                                {
                                    displayLine = currentLine.Replace(Contains!, 
                                        $"{AnsiColors.Green}{Replacement}{AnsiColors.Reset}");
                                }
                                else
                                {
                                    displayLine = regex!.Replace(currentLine, 
                                        match => $"{AnsiColors.Green}{Replacement}{AnsiColors.Reset}");
                                }
                            }

                            WriteObject($"{lineNumber,3}: {displayLine}");
                            lastOutputLine = lineNumber;
                            afterMatchCounter = 2;
                        }
                        else
                        {
                            // Output subsequent context
                            if (afterMatchCounter > 0)
                            {
                                var displayContextLine = BuildContextDisplayLine(currentLine, isLiteral, regex);
                                WriteObject($"{lineNumber,3}- {displayContextLine}");
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
                if (tempFile != null) File.Delete(tempFile);
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
            if (tempFile != null && File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
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
    /// Build context line display (highlight original string in yellow if matched)
    /// </summary>
    private string BuildContextDisplayLine(string line, bool isLiteral, Regex? regex)
    {
        if (isLiteral)
        {
            if (line.Contains(Contains!))
            {
                return line.Replace(Contains!, $"{AnsiColors.Yellow}{Contains}{AnsiColors.Reset}");
            }
        }
        else
        {
            if (regex!.IsMatch(line))
            {
                // Highlight regex match in yellow (original string unchanged)
                return regex.Replace(line, match => $"{AnsiColors.Yellow}{match.Value}{AnsiColors.Reset}");
            }
        }
        
        return line;
    }
}