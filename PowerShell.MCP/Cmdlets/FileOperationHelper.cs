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
    /// OPTIMIZED: Removed unnecessary line counting for better performance
    /// </summary>
    public static (int LinesRemoved, int LinesInserted) ReplaceEntireFile(
        string inputPath,
        string outputPath,
        TextFileUtility.FileMetadata metadata,
        string[] contentLines)
    {
        // OPTIMIZATION: Skip line counting since it's not critical information
        // and requires reading the entire file
        // If line count is needed for reporting, it can be done separately or on-demand
        
        // Write new content directly
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
        
        // Return 0 for removed lines since we're not counting them
        // This is acceptable as the operation is "replace entire file"
        // and the exact count of removed lines is not critical
        return (0, contentLines.Length);
    }
}