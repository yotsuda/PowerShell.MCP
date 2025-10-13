using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// File operation helper for atomic replacements and backups
/// </summary>
internal static class FileOperationHelper
{
    /// <summary>
    /// Create backup file
    /// </summary>
    public static string CreateBackup(string filePath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var backupPath = $"{filePath}.{timestamp}.bak";
        File.Copy(filePath, backupPath);
        return backupPath;
    }

    /// <summary>
    /// Replace file atomically
    /// </summary>
    public static void ReplaceFileAtomic(string targetPath, string tempFile)
    {
        // For new files, simply move
        if (!File.Exists(targetPath))
        {
            File.Move(tempFile, targetPath);
            return;
        }

        // For existing files, atomic replacement
        var backupTemp = targetPath + ".tmp";
        if (File.Exists(backupTemp))
        {
            File.Delete(backupTemp);
        }

        File.Move(targetPath, backupTemp);
        File.Move(tempFile, targetPath);
        File.Delete(backupTemp);
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
        int originalLineCount = 0;
        
        // Count original lines (for information)
        if (File.Exists(inputPath))
        {
            using (var reader = new StreamReader(inputPath, metadata.Encoding))
            {
                while (reader.ReadLine() != null)
                {
                    originalLineCount++;
                }
            }
        }

        // Replace entire file
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
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
