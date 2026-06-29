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
    /// Create a unique zero-length temp file in the SAME directory as <paramref name="targetPath"/>.
    /// Keeping the temp on the target's volume guarantees the subsequent File.Replace / File.Move
    /// stays an atomic same-volume rename (no cross-volume "not same device" failure) and lets a
    /// newly created file inherit the destination directory's ACEs. Replaces Path.GetTempFileName(),
    /// which always lands in %TEMP% and may be on a different volume than the file being edited.
    /// </summary>
    public static string CreateTempFileNextTo(string targetPath)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(dir))
            dir = Directory.GetCurrentDirectory();

        var name = Path.GetFileName(fullPath);
        for (int attempt = 0; ; attempt++)
        {
            var candidate = Path.Combine(dir, $".{name}.{Guid.NewGuid():N}.tmp");
            try
            {
                // CreateNew guarantees uniqueness; matches Path.GetTempFileName()'s "file exists" contract.
                using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                return candidate;
            }
            catch (IOException) when (attempt < 5 && File.Exists(candidate))
            {
                // Astronomically unlikely GUID collision — retry with a fresh name.
            }
        }
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
