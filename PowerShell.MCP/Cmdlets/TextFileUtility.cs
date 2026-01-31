using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Utility class for text file editing
/// Provides metadata detection/retention, backup, and atomic writes
/// LLM-optimized: predictable and safe operations
/// </summary>
public static class TextFileUtility
{
    public class FileMetadata
    {
        public Encoding Encoding { get; set; } = Encoding.UTF8;
        public string NewlineSequence { get; set; } = Environment.NewLine;
        public bool HasTrailingNewline { get; set; }
    }

    /// <summary>
    /// Detects file metadata
    /// </summary>
    public static FileMetadata DetectFileMetadata(string filePath)
    {
        return FileMetadataHelper.DetectFileMetadata(filePath);
    }

    /// <summary>
    /// Detects encoding (BOM detection + heuristics)
    /// </summary>
    public static Encoding DetectEncoding(string filePath)
    {
        return EncodingHelper.DetectEncoding(filePath);
    }
    /// <summary>
    /// Gets encoding (explicit specification or auto-detection)
    /// </summary>
    public static Encoding GetEncoding(string filePath, string? encodingName)
    {
        return EncodingHelper.GetEncoding(filePath, encodingName);
    }

    /// <summary>
    /// Detects file metadata (with explicit encoding support)
    /// </summary>
    public static FileMetadata DetectFileMetadata(string filePath, string? encodingName)
    {
        return FileMetadataHelper.DetectFileMetadata(filePath, encodingName);
    }

    /// <summary>
    /// Detects line ending style and trailing newline
    /// </summary>
    public static (string NewlineSequence, bool HasTrailingNewline) DetectNewline(
        string filePath, Encoding encoding)
    {
        return FileMetadataHelper.DetectNewline(filePath, encoding);
    }

    /// <summary>
    /// Creates a backup file
    /// </summary>
    /// <returns>Path of created backup file</returns>
    public static string CreateBackup(string filePath)
    {
        return FileOperationHelper.CreateBackup(filePath);
    }

    /// <summary>
    /// Processes file with single-pass streaming (optimized)
    /// Processes lines one by one for memory efficiency
    /// </summary>
    public static void ProcessFileStreaming(
        string inputPath,
        string outputPath,
        FileMetadata metadata,
        Func<string, int, string> lineProcessor)
    {
        using var reader = new StreamReader(inputPath, metadata.Encoding, false, 65536); // 64KB buffer
        using var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536); // 64KB buffer

        string? currentLine = reader.ReadLine();
        if (currentLine == null)
        {
            // Empty file
            return;
        }

        int lineNumber = 1;
        string? nextLine = reader.ReadLine();

        while (true)
        {
            // Process line
            string processedLine = lineProcessor(currentLine, lineNumber);
            writer.Write(processedLine);

            if (nextLine != null)
            {
                // Add newline since there is a next line
                writer.Write(metadata.NewlineSequence);
                lineNumber++;
                currentLine = nextLine;
                nextLine = reader.ReadLine();
            }
            else
            {
                // Final line: add trailing newline only if originally present
                if (metadata.HasTrailingNewline)
                {
                    writer.Write(metadata.NewlineSequence);
                }
                break;
            }
        }
    }

    /// <summary>
    /// Converts objects to string array
    /// </summary>
    public static string[] ConvertToStringArray(object? content)
    {
        if (content == null)
            return [];

        if (content is string str)
        {
            return str.Split(["\r\n", "\n"], StringSplitOptions.None);
        }
        else if (content is string[] arr)
        {
            return arr;
        }
        else if (content is object[] objArr)
        {
            // For object[], process each element
            var result = new List<string>();
            foreach (var item in objArr)
            {
                if (item is string s)
                {
                    // For string, split by newlines
                    result.AddRange(s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
                }
                else if (item != null)
                {
                    result.Add(item.ToString() ?? string.Empty);
                }
            }
            return result.ToArray();
        }
        else if (content is System.Collections.IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToArray();
        }
        else
        {
            return [content.ToString() ?? ""];
        }
    }

    /// <summary>
    /// Replaces file atomically using temp file
    /// </summary>
    public static void ReplaceFileAtomic(string targetPath, string tempFile)
    {
        FileOperationHelper.ReplaceFileAtomic(targetPath, tempFile);
    }

    /// <summary>
    /// Gets start and end lines from LineRange parameter
    /// Values <= 0 represent last line (e.g., -LineRange 100,-1 for line 100 to end)
    /// </summary>
    public static (int StartLine, int EndLine) ParseLineRange(int[]? lineRange)
    {
        if (lineRange == null || lineRange.Length == 0)
            return (1, int.MaxValue);

        if (lineRange.Length > 2)
        {
            throw new ArgumentException("LineRange accepts 1 or 2 values: start line, or start and end line. For example: -LineRange 5 or -LineRange 10,20");
        }

        int startLine = lineRange[0];
        int endLine;

        if (lineRange.Length > 1)
        {
            // When two values specified
            endLine = lineRange[1] <= 0 ? int.MaxValue : lineRange[1];
        }
        else
        {
            // When only one value specified, that line only
            endLine = startLine;
        }

        return (startLine, endLine);
    }

    /// <summary>
    /// Replaces entire file with new content
    /// For LLM: simple and predictable behavior
    /// </summary>
    public static (int LinesRemoved, int LinesInserted) ReplaceEntireFile(
        string inputPath,
        string outputPath,
        FileMetadata metadata,
        string[] contentLines)
    {
        return FileOperationHelper.ReplaceEntireFile(inputPath, outputPath, metadata, contentLines);
    }

    /// <summary>
    /// Replaces specified line range (single-pass streaming)
    /// For LLM: memory efficient, handles large files
    /// </summary>
    public static (int LinesRemoved, int LinesInserted, string? WarningMessage) ReplaceLineRangeStreaming(
        string inputPath,
        string outputPath,
        FileMetadata metadata,
        int startLine,
        int endLine,
        string[] contentLines)
    {
        int linesChanged = 0;
        string? warningMessage = null;
        int actualLineCount = 0;

        using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            if (!enumerator.MoveNext())
            {
                // Empty file: error if startLine is specified
                if (startLine > 0)
                {
                    throw new ArgumentException(
                        $"Line range {startLine}-{endLine} is out of bounds. File has only 0 line(s).",
                        nameof(startLine));
                }
                return (0, 0, null);
            }

            int lineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            bool replacementDone = false;
            bool isFirstLine = true;

            while (true)
            {
                actualLineCount = lineNumber;

                if (lineNumber < startLine)
                {
                    // Before replacement range: write as-is
                    if (!isFirstLine) writer.Write(metadata.NewlineSequence);
                    writer.Write(currentLine);
                    isFirstLine = false;
                }
                else if (lineNumber >= startLine && lineNumber <= endLine)
                {
                    // Within replacement range: write replacement content at first line
                    if (!replacementDone)
                    {
                        if (contentLines.Length > 0)
                        {
                            if (!isFirstLine) writer.Write(metadata.NewlineSequence);
                            for (int i = 0; i < contentLines.Length; i++)
                            {
                                if (i > 0) writer.Write(metadata.NewlineSequence);
                                writer.Write(contentLines[i]);
                            }
                            isFirstLine = false;
                        }
                        replacementDone = true;
                    }
                    // Skip lines within replacement range
                }
                else
                {
                    // After replacement range: write as-is
                    if (!isFirstLine) writer.Write(metadata.NewlineSequence);
                    writer.Write(currentLine);
                    isFirstLine = false;
                }

                if (hasNext)
                {
                    lineNumber++;
                    currentLine = enumerator.Current;
                    hasNext = enumerator.MoveNext();
                }
                else
                {
                    // Final line processing complete
                    actualLineCount = lineNumber;

                    // Out of range check (determined at file end)
                    if (startLine > actualLineCount)
                    {
                        throw new ArgumentException(
                            $"Line range {startLine}-{endLine} is out of bounds. File has only {actualLineCount} line(s).",
                            nameof(startLine));
                    }

                    if (endLine > actualLineCount)
                    {
                        warningMessage = $"End line {endLine} exceeds file length ({actualLineCount} lines). Will process up to line {actualLineCount}.";
                        linesChanged = actualLineCount - startLine + 1;
                    }
                    else
                    {
                        linesChanged = endLine - startLine + 1;
                    }

                    if (metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    break;
                }
            }
        }

        return (linesChanged, contentLines.Length, warningMessage);
    }

    /// <summary>
    /// Gets relative path from current directory
    /// </summary>
    public static string GetRelativePath(string fromPath, string toPath)
    {
        try
        {
            // Normalize both paths
            fromPath = System.IO.Path.GetFullPath(fromPath);
            toPath = System.IO.Path.GetFullPath(toPath);

            // Check if same drive
            if (System.IO.Path.GetPathRoot(fromPath) != System.IO.Path.GetPathRoot(toPath))
            {
                // Return absolute path if different drives
                return toPath;
            }

            var fromUri = new Uri(fromPath.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString())
                ? fromPath
                : fromPath + System.IO.Path.DirectorySeparatorChar);
            var toUri = new Uri(toPath);

            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            // Convert to backslashes (Windows)
            return relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        }
        catch
        {
            // Return absolute path on error
            return toPath;
        }
    }

    /// <summary>
    /// Upgrades encoding to UTF-8 if necessary
    /// Upgrades to UTF-8 when Content contains non-ASCII and current encoding is ASCII
    /// </summary>
    /// <param name="metadata">File metadata (encoding may be updated)</param>
    /// <param name="contentLines">Array of lines to add/update</param>
    /// <param name="encodingExplicitlySpecified">Whether encoding is explicitly specified</param>
    /// <param name="upgradeMessage">Message if upgraded (null if not upgraded)</param>
    /// <returns>true if encoding was upgraded, false otherwise</returns>
    public static bool TryUpgradeEncodingIfNeeded(
        FileMetadata metadata,
        string[] contentLines,
        bool encodingExplicitlySpecified,
        out string? upgradeMessage)
    {
        return EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata, contentLines, encodingExplicitlySpecified, out upgradeMessage);
    }

    /// <summary>
    /// Reads lines from a file with FileShare.ReadWrite to allow reading files locked by other processes.
    /// Useful for reading log files that are being written to by another application.
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <param name="encoding">Encoding to use, or null for auto-detection</param>
    /// <returns>Enumerable of lines</returns>
    public static IEnumerable<string> ReadLinesShared(string path, Encoding? encoding)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: encoding == null);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            yield return line;
        }
    }
}
