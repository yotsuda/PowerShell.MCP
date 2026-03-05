using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Update lines in file
/// LLM optimized: line range or full replacement, omit Content to delete
/// When LineRange not specified: replace entire file (existing) or create new (new)
/// </summary>
[Cmdlet(VerbsData.Update, "LinesInFile", SupportsShouldProcess = true)]
public class UpdateLinesInFileCmdlet : ContentAccumulatingCmdletBase
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

    [Parameter(Position = 1, ValueFromPipeline = true)][Alias("NewLines")]
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

        // LineRange validation
        ValidateLineRange(LineRange);

        // Existing file required when LineRange is specified
        bool allowNewFiles = (LineRange == null);

        if (!allowNewFiles)
        {
            // LineRange requires existing file, custom error if not found
            foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
            {
                ProcessFile(fileInfo.InputPath, fileInfo.ResolvedPath);
            }
        }
        else
        {
            // New file creation allowed when LineRange not specified
            foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: true, requireExisting: false))
            {
                ProcessFile(fileInfo.InputPath, fileInfo.ResolvedPath);
            }
        }
    }

    private void ProcessFile(string originalPath, string resolvedPath)
    {
        bool fileExists = File.Exists(resolvedPath);

        try
        {
            // Get or generate metadata
            TextFileUtility.FileMetadata metadata = fileExists
                ? TextFileUtility.DetectFileMetadata(resolvedPath, Encoding)
                : CreateNewFileMetadata(resolvedPath);

            string[] contentLines = TextFileUtility.ConvertToStringArray(Content);

            // If Content contains non-ASCII chars, upgrade encoding to UTF-8
            if (TextFileUtility.TryUpgradeEncodingIfNeeded(metadata, contentLines, Encoding != null, out var upgradeMessage))
            {
                WriteInformation(upgradeMessage, ["EncodingUpgrade"]);
            }

            var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
            bool isFullFileReplace = LineRange == null;

            string actionDescription = GetActionDescription(fileExists, isFullFileReplace, startLine, endLine);

            if (ShouldProcess(resolvedPath, actionDescription))
            {
                if (Backup && fileExists)
                {
                    var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                    WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
                }

                ExecuteFileOperation(
                    resolvedPath,
                    metadata,
                    contentLines,
                    isFullFileReplace,
                    startLine,
                    endLine,
                    fileExists,
                    originalPath);
            }
        }
        catch (Exception ex)
        {
            string errorId = fileExists ? "SetFailed" : "CreateFailed";
            WriteError(new ErrorRecord(ex, errorId, ErrorCategory.WriteError, resolvedPath));
        }
    }

    private TextFileUtility.FileMetadata CreateNewFileMetadata(string resolvedPath)
    {
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding != null
                ? TextFileUtility.GetEncoding(resolvedPath, Encoding)
                : new System.Text.UTF8Encoding(false), // UTF8 without BOM
            NewlineSequence = Environment.NewLine,
            HasTrailingNewline = true  // Default has trailing newline
        };

        // Create directory if it does not exist
        var directory = System.IO.Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            WriteInformation($"Created directory: {directory}", new string[] { "DirectoryCreated" });
        }
        return metadata;
    }

    private static string GetActionDescription(bool fileExists, bool isFullFileReplace, int startLine, int endLine)
    {
        if (!fileExists)
        {
            return "Create new file";
        }

        if (isFullFileReplace)
        {
            return "Set entire file content";
        }

        return $"Set content of lines {startLine}-{endLine}";
    }

    private void ExecuteFileOperation(
        string resolvedPath,
        TextFileUtility.FileMetadata metadata,
        string[] contentLines,
        bool isFullFileReplace,
        int startLine,
        int endLine,
        bool fileExists,
        string originalPath)
    {
        var tempFile = System.IO.Path.GetTempFileName();
        int linesRemoved;
        int linesInserted;

        try
        {
            if (isFullFileReplace)
            {
                linesRemoved = 0;
                linesInserted = contentLines.Length;
                var displayPath = GetDisplayPath(originalPath, resolvedPath);
                bool headerShown = false;

                // Read old file: display deleted lines + count (single pass)
                if (fileExists && new FileInfo(resolvedPath).Length > 0)
                {
                    WriteObject(AnsiColors.Header(displayPath));
                    headerShown = true;
                    foreach (var line in File.ReadLines(resolvedPath, metadata.Encoding))
                    {
                        linesRemoved++;
                        WriteObject($"   : {AnsiColors.Deleted(line)}");
                    }
                }

                // Write new content to temp file
                using (var writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536))
                {
                    writer.NewLine = metadata.NewlineSequence;
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        writer.Write(contentLines[i]);
                        if (i < contentLines.Length - 1 || metadata.HasTrailingNewline)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                    }
                }

                // Display new content in green
                if (contentLines.Length > 0)
                {
                    if (!headerShown)
                    {
                        WriteObject(AnsiColors.Header(displayPath));
                        headerShown = true;
                    }
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        WriteObject($"{i + 1,3}: {AnsiColors.Inserted(contentLines[i])}");
                    }
                }

                if (headerShown) WriteObject("");
            }
            else
            {
                // Replace line range with real-time display (single pass)
                string? warningMessage;
                (linesRemoved, linesInserted, warningMessage) = ReplaceLineRangeWithDisplay(
                    originalPath,
                    resolvedPath,
                    tempFile,
                    metadata,
                    startLine,
                    endLine,
                    contentLines);

                if (!string.IsNullOrEmpty(warningMessage))
                {
                    WriteWarning(warningMessage);
                }
            }

            // Replace atomically
            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

            // Result message
            string message = GenerateResultMessage(fileExists, linesRemoved, linesInserted);
            string prefix = fileExists ? "Updated" : "Created";
            WriteObject(AnsiColors.Success($"{prefix} {GetDisplayPath(originalPath, resolvedPath)}: {message}"));
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
    private static string GenerateResultMessage(bool fileExists, int linesRemoved, int linesInserted)
    {
        if (!fileExists)
        {
            return $"{linesInserted} line(s) (net: +{linesInserted})";
        }

        // linesRemoved is actual processed line count
        if (linesInserted == 0)
        {
            return $"Removed {linesRemoved} line(s) (net: -{linesRemoved})";
        }

        // Generate message based on line count (always show net)
        int netChange = linesInserted - linesRemoved;
        string netStr = netChange > 0 ? $"+{netChange}" : netChange.ToString();
        return $"Replaced {linesRemoved} line(s) with {linesInserted} line(s) (net: {netStr})";
    }

    /// <summary>
    /// Replace line range with real-time display (single pass)
    /// Display order: pre-context → deleted lines (red) → new lines (green) → post-context
    /// </summary>
    private (int linesRemoved, int linesInserted, string? warningMessage) ReplaceLineRangeWithDisplay(
        string originalPath,
        string inputPath,
        string outputPath,
        TextFileUtility.FileMetadata metadata,
        int startLine,
        int endLine,
        string[] contentLines)
    {
        var displayPath = GetDisplayPath(originalPath, inputPath);
        bool headerPrinted = false;
        bool displayedNewContent = false;

        int currentLine = 1;
        int outputLine = 1;
        int linesRemoved = 0;
        int linesInserted = contentLines.Length;
        string? warningMessage = null;
        bool insertedContent = false;

        // Pre-context rotate buffer
        var preContextBuffer = new RotateBuffer<(string line, int outputLineNum)>(2);
        int afterContextCounter = 0;

        bool hasLinesAfterRange = false;

        using (var reader = new StreamReader(inputPath, metadata.Encoding))
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            writer.NewLine = metadata.NewlineSequence;
            string? line;
            bool hasNextLine;

            while ((line = reader.ReadLine()) != null)
            {
                hasNextLine = reader.Peek() != -1;

                // Write new content to FILE at start of replacement range
                if (currentLine == startLine && !insertedContent)
                {
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        writer.Write(contentLines[i]);
                        if (i < contentLines.Length - 1)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                        outputLine++;
                    }
                    insertedContent = true;
                }

                // Lines within replacement range
                if (currentLine >= startLine && currentLine <= endLine)
                {
                    // First deleted line: print header + pre-context
                    if (linesRemoved == 0)
                    {
                        if (!headerPrinted)
                        {
                            WriteObject(AnsiColors.Header(displayPath));
                            headerPrinted = true;
                        }
                        foreach (var ctx in preContextBuffer)
                        {
                            WriteObject($"{ctx.outputLineNum,3}- {ctx.line}");
                        }
                    }

                    // Display deleted line in red (no line number)
                    WriteObject($"   : {AnsiColors.Deleted(line)}");
                    linesRemoved++;

                    // At final line of replacement range
                    if (currentLine == endLine)
                    {
                        hasLinesAfterRange = hasNextLine;

                        // Write newline for final line of new content
                        if (contentLines.Length > 0)
                        {
                            if (hasLinesAfterRange)
                            {
                                writer.Write(metadata.NewlineSequence);
                            }
                            else if (metadata.HasTrailingNewline)
                            {
                                writer.Write(metadata.NewlineSequence);
                            }
                        }

                        // If no more lines after range, display new content now
                        if (!hasLinesAfterRange)
                        {
                            DisplayNewContent(contentLines, startLine);
                            displayedNewContent = true;
                            WriteObject("");
                        }
                    }

                    currentLine++;
                    continue;
                }

                // First line after range: display new content in green
                if (insertedContent && !displayedNewContent)
                {
                    DisplayNewContent(contentLines, startLine);
                    displayedNewContent = true;
                    afterContextCounter = 2;
                }

                // Copy lines outside replacement range
                writer.Write(line);

                // Post-context
                if (afterContextCounter > 0)
                {
                    WriteObject($"{outputLine,3}- {line}");
                    afterContextCounter--;
                    if (afterContextCounter == 0)
                    {
                        WriteObject("");
                    }
                }

                // Update pre-context buffer
                preContextBuffer.Add((line, outputLine));

                // Determine newline addition
                if (hasNextLine)
                {
                    writer.Write(metadata.NewlineSequence);
                }
                else if (metadata.HasTrailingNewline)
                {
                    writer.Write(metadata.NewlineSequence);
                }

                currentLine++;
                outputLine++;
            }

            // Handle case where range extends beyond file end
            if (insertedContent && !displayedNewContent)
            {
                DisplayNewContent(contentLines, startLine);
                displayedNewContent = true;
                WriteObject("");
            }

            // If replacement range not reached at file end
            if (currentLine <= startLine && !insertedContent)
            {
                int actualLineCount = currentLine - 1;

                if (startLine > actualLineCount)
                {
                    throw new ArgumentException(
                        $"Line range {startLine}-{endLine} is out of bounds. File has only {actualLineCount} line(s).",
                        nameof(startLine));
                }

                if (endLine > actualLineCount)
                {
                    warningMessage = $"End line {endLine} exceeds file length ({actualLineCount} lines). Will process up to line {actualLineCount}.";
                }

                // Append new content at end
                for (int i = 0; i < contentLines.Length; i++)
                {
                    writer.Write(contentLines[i]);
                    if (i < contentLines.Length - 1)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    else if (metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    outputLine++;
                }
            }
        }

        return (linesRemoved, linesInserted, warningMessage);
    }

    /// <summary>
    /// Display new content lines in green
    /// </summary>
    private void DisplayNewContent(string[] contentLines, int startLine)
    {
        for (int i = 0; i < contentLines.Length; i++)
        {
            int lineNum = startLine + i;
            WriteObject($"{lineNum,3}: {AnsiColors.Inserted(contentLines[i])}");
        }
    }

    protected override void EndProcessing()
    {
        // Do nothing if not in pipe input accumulation mode
        if (!IsAccumulatingMode)
            return;

        // If there was pipe input
        if (FinalizeAccumulatedContent())
        {
            // Content was set
        }
        // No pipe input + LineRange specified -> error
        else if (LineRange != null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new PSArgumentException("Content is required when LineRange is specified. Use -Content @() to explicitly delete lines."),
                "ContentRequired",
                ErrorCategory.InvalidArgument,
                null));
        }
        // No pipe input + no LineRange -> do nothing
        else
        {
            return;
        }

        // LineRange validation
        ValidateLineRange(LineRange);

        // Existing file required when LineRange is specified
        bool allowNewFiles = (LineRange == null);

        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles, requireExisting: !allowNewFiles))
        {
            ProcessFile(fileInfo.InputPath, fileInfo.ResolvedPath);
        }
    }
}
