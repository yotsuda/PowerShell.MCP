using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// File operation helper for atomic replacements and backups
/// OPTIMIZED: Removed unnecessary line counting
/// </summary>
internal static class FileOperationHelper
{
    /// <summary>
    /// Create backup file
    /// </summary>
    public static string CreateBackup(string filePath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        var backupPath = $"{filePath}.{timestamp}.bak";
        File.Copy(filePath, backupPath);
        return backupPath;
    }

    /// <summary>
    /// Replace file atomically using File.Replace (NTFS transaction on Windows,
    /// rename(2) on Unix). Falls back to move-based replacement on error.
    /// </summary>
    public static void ReplaceFileAtomic(string targetPath, string tempFile)
    {
        // For new files, simply move
        if (!File.Exists(targetPath))
        {
            File.Move(tempFile, targetPath);
            return;
        }

        // File.Replace atomically swaps tempFile → targetPath with backup
        var backupTemp = targetPath + ".tmp";
        try
        {
            File.Replace(tempFile, targetPath, backupTemp);
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback for platforms where File.Replace is unsupported
            if (File.Exists(backupTemp)) File.Delete(backupTemp);
            File.Move(targetPath, backupTemp);
            File.Move(tempFile, targetPath);
        }
        try { File.Delete(backupTemp); } catch { }
    }

    /// <summary>
    /// Replace entire file with new content
    /// </summary>
    public static (int LinesRemoved, int LinesInserted) ReplaceEntireFile(
        string inputPath,
        string outputPath,
        TextFileUtility.FileMetadata metadata,
        string[] contentLines)
    {
        // Count original lines for accurate reporting
        int originalLineCount = 0;
        if (File.Exists(inputPath))
        {
            using var reader = new StreamReader(inputPath, metadata.Encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 65536);
            while (reader.ReadLine() != null)
                originalLineCount++;
        }

        // Write new content
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            writer.NewLine = metadata.NewlineSequence;

            if (contentLines.Length > 0)
            {
                for (int i = 0; i < contentLines.Length; i++)
                {
                    writer.Write(contentLines[i]);
                    if (i < contentLines.Length - 1 || metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                }
            }
        }

        return (originalLineCount, contentLines.Length);
    }
}
