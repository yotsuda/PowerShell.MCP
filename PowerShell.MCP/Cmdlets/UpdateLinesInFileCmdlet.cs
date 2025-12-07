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
    /// <summary>
    /// Context info collected by rotate buffer
    /// </summary>
    private class ContextData
    {
        // Previous 2 lines context
        public string? ContextBefore2 { get; set; }
        public string? ContextBefore1 { get; set; }
        public int ContextBefore2Line { get; set; }
        public int ContextBefore1Line { get; set; }

        // Used only for deletion
        public string? DeletedFirst { get; set; }
        public string? DeletedSecond { get; set; }
        public string? DeletedThirdLast { get; set; }  // Ring buffer (last 3 lines)
        public string? DeletedSecondLast { get; set; }  // Ring buffer (last 2 lines)
        public string? DeletedLast { get; set; }
        public int DeletedCount { get; set; }
        public int DeletedStartLine { get; set; }

        // Next 2 lines context
        public string? ContextAfter1 { get; set; }
        public string? ContextAfter2 { get; set; }
        public int ContextAfter1Line { get; set; }
        public int ContextAfter2Line { get; set; }
    }

    private const string ErrorMessageLineRangeWithoutFile =
        "File not found: {0}. Cannot use -LineRange with non-existent file.";

    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter(Position = 1)]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter(ValueFromPipeline = true)]
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

        // Context info (collected by rotate buffer)
        ContextData? context = null;
        int totalLines = 0;

        try
        {
            if (isFullFileReplace)
            {
                // Replace entire file (same method for new and existing)
                (linesRemoved, linesInserted) = TextFileUtility.ReplaceEntireFile(
                    resolvedPath,
                    tempFile,
                    metadata,
                    contentLines);
            }
            else
            {
                // Replace line range + collect context (rotate buffer pattern)
                bool collectContext = fileExists && LineRange != null;

                string? warningMessage;
                (linesRemoved, linesInserted, totalLines, warningMessage, context) = ReplaceLineRangeWithContext(
                    resolvedPath,
                    tempFile,
                    metadata,
                    startLine,
                    endLine,
                    contentLines,
                    collectContext);

                // Output warning if any
                if (!string.IsNullOrEmpty(warningMessage))
                {
                    WriteWarning(warningMessage);
                }
            }

            // Replace atomically
            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

            // Context display (from rotate buffer, no file re-read)
            if (context != null)
            {
                OutputUpdateContext(originalPath, resolvedPath,
                    context,
                    startLine,
                    linesInserted,
                    totalLines,
                    contentLines);
            }

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
    /// Replace line range while building context buffer (single pass)
    /// </summary>
    private (int linesRemoved, int linesInserted, int totalLines, string? warningMessage, ContextData? context) ReplaceLineRangeWithContext(
        string inputPath,
        string outputPath,
        TextFileUtility.FileMetadata metadata,
        int startLine,
        int endLine,
        string[] contentLines,
        bool collectContext)
    {
        // Initialize variables for Context collection
        ContextData? context = collectContext ? new ContextData() : null;
        bool isDelete = contentLines.Length == 0;

        int currentLine = 1;
        int outputLine = 1;
        int linesRemoved = 0;  // Count actually processed lines
        int linesInserted = contentLines.Length;
        string? warningMessage = null;
        bool insertedContent = false;


        // For deletion info collection
        int deletedCount = 0;

        // After context counter
        int afterCounter = 0;

        // Record whether lines exist after replacement range
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

                // Collect previous 2 lines context (just before range)
                if (context != null)
                {
                    if (currentLine == startLine - 2)
                    {
                        context.ContextBefore2 = line;
                        context.ContextBefore2Line = currentLine;
                    }
                    else if (currentLine == startLine - 1)
                    {
                        context.ContextBefore1 = line;
                        context.ContextBefore1Line = currentLine;
                    }
                }

                // Insert new content at start of replacement range
                if (currentLine == startLine && !insertedContent)
                {
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        writer.Write(contentLines[i]);

                        // Add newline after each line (final line handled separately)
                        if (i < contentLines.Length - 1)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                        // Final line handling determined at end of replacement range
                        outputLine++;
                    }
                    insertedContent = true;
                }

                // Process lines within replacement range
                if (currentLine >= startLine && currentLine <= endLine)
                {
                    // Deletion: save lines being deleted
                    if (isDelete && context != null)
                    {
                        deletedCount++;

                        // Save first 2 lines
                        if (deletedCount == 1)
                        {
                            context.DeletedFirst = line;
                        }
                        else if (deletedCount == 2)
                        {
                            context.DeletedSecond = line;
                        }

                        // Update last 3 lines with ring buffer
                        context.DeletedThirdLast = context.DeletedSecondLast;
                        context.DeletedSecondLast = context.DeletedLast;
                        context.DeletedLast = line;
                    }
                    linesRemoved++;  // Count actually deleted/replaced lines

                    // At final line of replacement range, check if subsequent lines exist
                    if (currentLine == endLine)
                    {
                        hasLinesAfterRange = hasNextLine;

                        // Write newline for final line of new content
                        if (contentLines.Length > 0)
                        {
                            if (hasLinesAfterRange)
                            {
                                // Always add newline if subsequent lines exist
                                writer.Write(metadata.NewlineSequence);
                            }
                            else if (metadata.HasTrailingNewline)
                            {
                                // At file end, add newline only if original file had trailing newline
                                writer.Write(metadata.NewlineSequence);
                            }
                        }
                    }

                    currentLine++;
                    continue;
                }

                // Copy lines outside replacement range
                writer.Write(line);

                // Collect after context (2 lines just after range)
                if (context != null && currentLine > endLine && afterCounter < 2)
                {
                    if (afterCounter == 0)
                    {
                        context.ContextAfter1 = line;
                        context.ContextAfter1Line = outputLine;
                    }
                    else if (afterCounter == 1)
                    {
                        context.ContextAfter2 = line;
                        context.ContextAfter2Line = outputLine;
                    }
                    afterCounter++;
                }

                // Determine newline addition
                if (hasNextLine)
                {
                    // Always add newline if subsequent lines exist
                    writer.Write(metadata.NewlineSequence);
                }
                else if (metadata.HasTrailingNewline)
                {
                    // At final line, add newline only if original file had trailing newline
                    writer.Write(metadata.NewlineSequence);
                }

                currentLine++;
                outputLine++;
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
                    // Final line handling: add newline only if original file had trailing newline
                    else if (metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    outputLine++;
                }
            }
        }

        // Set Context info
        if (context != null && isDelete)
        {
            context.DeletedCount = deletedCount;
            context.DeletedStartLine = startLine;
        }

        int totalLines = outputLine - 1;
        return (linesRemoved, linesInserted, totalLines, warningMessage, context);
    }


    /// <summary>
    /// Display update context (from rotate buffer, no file re-read)
    /// </summary>
    private void OutputUpdateContext(string originalPath, string filePath,
        ContextData context,
        int startLine,
        int linesInserted,
        int totalLines,
        string[] contentLines)
    {
        var displayPath = GetDisplayPath(originalPath, filePath);

        // Output header
        WriteObject(AnsiColors.Header(displayPath));

        int endLine = startLine + linesInserted - 1;

        // Previous 2 lines context
        if (context.ContextBefore2Line > 0)
        {
            WriteObject($"{context.ContextBefore2Line,3}- {context.ContextBefore2}");
        }
        if (context.ContextBefore1Line > 0)
        {
            WriteObject($"{context.ContextBefore1Line,3}- {context.ContextBefore1}");
        }

        // Display updated lines
        if (linesInserted == 0)
        {
            // Empty array: display only :
            WriteObject($"   :");
        }
        else if (linesInserted <= 5)
        {
            // 1-5 lines: display all highlighted
            for (int i = 0; i < linesInserted; i++)
            {
                int lineNum = startLine + i;
                WriteObject($"{lineNum,3}: {AnsiColors.Inserted(contentLines[i])}");
            }
        }
        else
        {
            // 6+ lines: first 2 + ellipsis marker + last 2
            // First 2 lines
            WriteObject($"{startLine,3}: {AnsiColors.Inserted(contentLines[0])}");
            WriteObject($"{startLine + 1,3}: {AnsiColors.Inserted(contentLines[1])}");

            // Ellipsis marker
            WriteObject("   :");

            // Last 2 lines
            WriteObject($"{endLine - 1,3}: {AnsiColors.Inserted(contentLines[linesInserted - 2])}");
            WriteObject($"{endLine,3}: {AnsiColors.Inserted(contentLines[linesInserted - 1])}");
        }

        // Next 2 lines context
        if (context.ContextAfter1Line > 0)
        {
            WriteObject($"{context.ContextAfter1Line,3}- {context.ContextAfter1}");
        }
        if (context.ContextAfter2Line > 0)
        {
            WriteObject($"{context.ContextAfter2Line,3}- {context.ContextAfter2}");
        }

        // Separate context and summary with empty line
        WriteObject("");
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
