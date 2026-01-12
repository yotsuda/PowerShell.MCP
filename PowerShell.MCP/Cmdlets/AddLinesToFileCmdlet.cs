using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Add lines to file (can also create new file)
/// LLM optimized: parameters optional for both new/existing files (defaults to append, use -LineNumber to specify insert position)
/// </summary>
[Cmdlet(VerbsCommon.Add, "LinesToFile", SupportsShouldProcess = true)]
public class AddLinesToFileCmdlet : ContentAccumulatingCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter(Position = 1, ValueFromPipeline = true)]
    public object[]? Content { get; set; }

    /// <summary>
    /// Accessor to Content property (for base class)
    /// </summary>
    protected override object[]? ContentProperty
    {
        get => Content;
        set => Content = value;
    }

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int LineNumber { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        InitializeContentAccumulation();
    }

    protected override void ProcessRecord()
    {
        // If in pipeline accumulation mode, accumulate and exit
        if (TryAccumulateContent())
            return;

        // Error if Content not specified as argument
        if (Content == null || Content.Length == 0)
        {
            ThrowTerminatingError(new ErrorRecord(
                new PSArgumentException("Content is required. Provide via -Content parameter or pipeline input."),
                "ContentRequired",
                ErrorCategory.InvalidArgument,
                null));
        }

        ProcessAllPaths();
    }

    /// <summary>
    /// Writes new content to file (common for new and empty files)
    /// </summary>
    private static void WriteNewContent(string outputPath, string[] contentLines, TextFileUtility.FileMetadata metadata)
    {
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            for (int i = 0; i < contentLines.Length; i++)
            {
                writer.Write(contentLines[i]);

                if (i < contentLines.Length - 1)
                {
                    writer.Write(metadata.NewlineSequence);
                }
            }
        }
    }

    /// <summary>
    /// Add lines to file (common for new and existing files)
    /// </summary>
    private void AddToFile(string resolvedPath, string originalPath, bool isNewFile)
    {

        // Get or create metadata
        TextFileUtility.FileMetadata metadata;
        if (isNewFile)
        {
            // New file: use default metadata
            metadata = new TextFileUtility.FileMetadata
            {
                Encoding = string.IsNullOrEmpty(Encoding) ? new System.Text.UTF8Encoding(false) : TextFileUtility.GetEncoding(resolvedPath, Encoding),
                NewlineSequence = Environment.NewLine,
                HasTrailingNewline = false
            };
        }
        else
        {
            // Existing file: detect metadata from file
            metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);
        }

        string[] contentLines = TextFileUtility.ConvertToStringArray(Content);

        // If Content contains non-ASCII chars, upgrade encoding to UTF-8
        if (TextFileUtility.TryUpgradeEncodingIfNeeded(metadata, contentLines, Encoding != null, out var upgradeMessage))
        {
            WriteInformation(upgradeMessage, ["EncodingUpgrade"]);
        }

        // If no LineNumber specified, append to end (common for new/existing)
        int insertAt;
        bool effectiveAtEnd;

        if (isNewFile)
        {
            // New file creation: warn if LineNumber > 1
            if (LineNumber > 1)
            {
                WriteWarning($"File does not exist. Creating new file. LineNumber {LineNumber} will be treated as line 1.");
            }

            // New file: if LineNumber not specified append to end, if > 1 treat as 1
            insertAt = (LineNumber > 1) ? 1 : (LineNumber > 0 ? LineNumber : int.MaxValue);
            effectiveAtEnd = LineNumber == 0;
        }
        else
        {
            // Existing file: if LineNumber not specified, default to append
            insertAt = LineNumber > 0 ? LineNumber : int.MaxValue;
            effectiveAtEnd = LineNumber == 0;
        }

        string actionDescription = effectiveAtEnd
            ? $"Add {contentLines.Length} line(s) at end"
            : $"Add {contentLines.Length} line(s) at line {insertAt}";

        if (ShouldProcess(resolvedPath, actionDescription))
        {
            if (Backup)
            {
                if (isNewFile)
                {
                    WriteWarning($"-Backup parameter is ignored for new file creation: {GetDisplayPath(originalPath, resolvedPath)}");
                }
                else
                {
                    var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                    WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
                }
            }

            var tempFile = System.IO.Path.GetTempFileName();
            int actualInsertAt = insertAt;

            try
            {
                if (isNewFile || new FileInfo(resolvedPath).Length == 0)
                {
                    // New file or empty file: write only new content
                    WriteNewContent(tempFile, contentLines, metadata);
                }
                else
                {
                    // Existing non-empty file: insert processing (context output in real-time)
                    int totalLines;
                    (totalLines, actualInsertAt) = InsertLinesWithContext(originalPath, resolvedPath,
                        tempFile,
                        contentLines,
                        metadata,
                        insertAt);
                }

                // Replace atomically (or create new)
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                string message = isNewFile
                    ? AnsiColors.Success($"Created {GetDisplayPath(originalPath, resolvedPath)}: {contentLines.Length} line(s) (net: +{contentLines.Length})")
                    : AnsiColors.Success($"Added {contentLines.Length} line(s) to {GetDisplayPath(originalPath, resolvedPath)} {(effectiveAtEnd ? "at end" : $"at line {actualInsertAt}")} (net: +{contentLines.Length})");

                WriteObject(message);

            }
            catch
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                throw;
            }
        }
    }

    /// <summary>
    /// Normal file line insertion processing (real-time context output, single pass)
    /// </summary>
    private (int totalLines, int actualInsertAt) InsertLinesWithContext(string originalPath, string inputPath,
        string outputPath,
        string[] contentLines,
        TextFileUtility.FileMetadata metadata,
        int insertAt)
    {
        var displayPath = GetDisplayPath(originalPath, inputPath);
        bool contextHeaderPrinted = false;

        using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            writer.NewLine = metadata.NewlineSequence;

            bool hasLines = enumerator.MoveNext();
            if (!hasLines)
            {
                // This should not happen (empty file handled separately)
                for (int i = 0; i < contentLines.Length; i++)
                {
                    writer.Write(contentLines[i]);
                    if (i < contentLines.Length - 1)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                }
                return (contentLines.Length, 1);
            }

            int inputLineNumber = 1;
            int outputLineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            bool inserted = false;
            int actualInsertAt = insertAt;
            int afterContextCounter = 0;

            // Rotate buffer: for context display (line content and output line number pairs)
            var preContextBuffer = new RotateBuffer<(string line, int outputLineNum)>(2);

            while (true)
            {
                // When reaching insert position, write new content first
                if (!inserted && inputLineNumber == insertAt)
                {
                    actualInsertAt = outputLineNumber;

                    // Output context header
                    if (!contextHeaderPrinted)
                    {
                        WriteObject(AnsiColors.Header(displayPath));
                        contextHeaderPrinted = true;
                    }

                    // Output previous 2 lines (from rotate buffer)
                    foreach (var ctx in preContextBuffer)
                    {
                        WriteObject($"{ctx.outputLineNum,3}- {ctx.line}");
                    }

                    // Output inserted lines
                    if (contentLines.Length <= 5)
                    {
                        // 1-5 lines: show all
                        for (int i = 0; i < contentLines.Length; i++)
                        {
                            writer.Write(contentLines[i]);
                            WriteObject($"{outputLineNumber,3}: {AnsiColors.Inserted(contentLines[i])}");

                            if (i < contentLines.Length - 1)
                            {
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }
                        }
                    }
                    else
                    {
                        // 6+ lines: show only first 2 and last 2
                        // First 2 lines
                        for (int i = 0; i < 2; i++)
                        {
                            writer.Write(contentLines[i]);
                            WriteObject($"{outputLineNumber,3}: {AnsiColors.Inserted(contentLines[i])}");
                            writer.Write(metadata.NewlineSequence);
                            outputLineNumber++;
                        }

                        // Output ellipsis marker
                        WriteObject("   :");

                        // Write middle lines (no output)
                        for (int i = 2; i < contentLines.Length - 2; i++)
                        {
                            writer.Write(contentLines[i]);
                            writer.Write(metadata.NewlineSequence);
                            outputLineNumber++;
                        }

                        // Last 2 lines
                        for (int i = contentLines.Length - 2; i < contentLines.Length; i++)
                        {
                            writer.Write(contentLines[i]);
                            WriteObject($"{outputLineNumber,3}: {AnsiColors.Inserted(contentLines[i])}");

                            if (i < contentLines.Length - 1)
                            {
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }
                        }
                    }

                    writer.Write(metadata.NewlineSequence);
                    outputLineNumber++;
                    inserted = true;
                    afterContextCounter = 2; // Output next 2 lines
                }

                // Write current line
                writer.Write(currentLine);

                // Output trailing context
                if (afterContextCounter > 0)
                {
                    WriteObject($"{outputLineNumber,3}- {currentLine}");
                    afterContextCounter--;
                    if (afterContextCounter == 0)
                    {
                    // Context output complete, add empty line
                        WriteObject("");
                    }
                }

                // Update rotate buffer: keep for context display
                preContextBuffer.Add((currentLine, outputLineNumber));

                if (hasNext)
                {
                    writer.Write(metadata.NewlineSequence);
                    inputLineNumber++;
                    outputLineNumber++;
                    currentLine = enumerator.Current;
                    hasNext = enumerator.MoveNext();
                }
                else
                {
                    // Process final line
                    if (!inserted)
                    {
                        // Append to end (default)
                        writer.Write(metadata.NewlineSequence);
                        outputLineNumber++;
                        actualInsertAt = outputLineNumber;

                        // Output context header
                        if (!contextHeaderPrinted)
                        {
                            WriteObject(AnsiColors.Header(displayPath));
                            contextHeaderPrinted = true;
                        }

                        // Output previous 2 lines (from rotate buffer)
                        foreach (var ctx in preContextBuffer)
                        {
                            WriteObject($"{ctx.outputLineNum,3}- {ctx.line}");
                        }

                        // Output inserted lines
                        if (contentLines.Length <= 5)
                        {
                            // 1-5 lines: show all
                            for (int i = 0; i < contentLines.Length; i++)
                            {
                                writer.Write(contentLines[i]);
                                WriteObject($"{outputLineNumber,3}: {AnsiColors.Inserted(contentLines[i])}");

                                if (i < contentLines.Length - 1)
                                {
                                    writer.Write(metadata.NewlineSequence);
                                    outputLineNumber++;
                                }
                            }

                            // Preserve original file trailing newline
                            if (metadata.HasTrailingNewline)
                            {
                                writer.Write(metadata.NewlineSequence);
                            }
                        }
                        else
                        {
                            // 6+ lines: show only first 2 and last 2
                            // First 2 lines
                            for (int i = 0; i < 2; i++)
                            {
                                writer.Write(contentLines[i]);
                                WriteObject($"{outputLineNumber,3}: {AnsiColors.Inserted(contentLines[i])}");
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }

                            // Output ellipsis marker
                            WriteObject("   :");

                            // Write middle lines (no output)
                            for (int i = 2; i < contentLines.Length - 2; i++)
                            {
                                writer.Write(contentLines[i]);
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }

                            // Last 2 lines
                            for (int i = contentLines.Length - 2; i < contentLines.Length; i++)
                            {
                                writer.Write(contentLines[i]);
                                WriteObject($"{outputLineNumber,3}: {AnsiColors.Inserted(contentLines[i])}");

                                if (i < contentLines.Length - 1)
                                {
                                    writer.Write(metadata.NewlineSequence);
                                    outputLineNumber++;
                                }
                            }

                            // Preserve original file trailing newline
                            if (metadata.HasTrailingNewline)
                            {
                                writer.Write(metadata.NewlineSequence);
                            }
                        }

                        // No trailing context for append, just empty line
                        WriteObject("");

                        inserted = true;
                    }
                    else
                    {
                        // Preserve original file trailing newline
                        if (metadata.HasTrailingNewline)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                    }
                    break;
                }
            }

            return (outputLineNumber, actualInsertAt);
        }
    }

    /// <summary>
    /// Resolves path and executes add operation on file
    /// </summary>
    private void ProcessAllPaths()
    {
        string[] inputPaths = Path ?? LiteralPath;
        bool isLiteralPath = (LiteralPath != null);

        foreach (var inputPath in inputPaths)
        {
            bool isNewFile = false;
            string? resolvedPath = null;

            try
            {
                if (isLiteralPath)
                {
                    resolvedPath = GetUnresolvedProviderPathFromPSPath(inputPath);
                    isNewFile = !File.Exists(resolvedPath);
                }
                else
                {
                    try
                    {
                        var resolved = GetResolvedProviderPathFromPSPath(inputPath, out _);
                        foreach (var rPath in resolved)
                        {
                            try
                            {
                                AddToFile(rPath, inputPath, false);
                            }
                            catch (Exception ex)
                            {
                                WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, rPath));
                            }
                        }
                        continue;
                    }
                    catch (ItemNotFoundException)
                    {
                        if (WildcardPattern.ContainsWildcardCharacters(inputPath))
                        {
                            WriteError(new ErrorRecord(
                                new InvalidOperationException($"Cannot create new file with wildcard pattern: {inputPath}"),
                                "WildcardNotSupportedForNewFile",
                                ErrorCategory.InvalidArgument,
                                inputPath));
                            continue;
                        }
                        resolvedPath = GetUnresolvedProviderPathFromPSPath(inputPath);
                        isNewFile = true;
                    }
                }

                if (resolvedPath != null)
                {
                    try
                    {
                        AddToFile(resolvedPath, inputPath, isNewFile);
                    }
                    catch (Exception ex)
                    {
                        WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, resolvedPath));
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "PathResolutionFailed", ErrorCategory.InvalidArgument, inputPath));
            }
        }
    }

    protected override void EndProcessing()
    {
        // Do nothing if not in accumulation mode
        if (!IsAccumulatingMode)
            return;

        // Set accumulated content to Content
        if (!FinalizeAccumulatedContent())
        {
            ThrowTerminatingError(new ErrorRecord(
                new PSArgumentException("Content is required. Provide via -Content parameter or pipeline input."),
                "ContentRequired",
                ErrorCategory.InvalidArgument,
                null));
        }

        ProcessAllPaths();
    }
}
