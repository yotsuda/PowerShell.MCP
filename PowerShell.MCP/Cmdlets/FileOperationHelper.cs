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
    /// Number of File.Replace attempts before giving up. The swap can lose a race with a
    /// transient holder of the target (antivirus scan, search indexer, cloud sync, an editor
    /// mid-save), which surfaces as ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176) even though nothing
    /// is durably wrong. Those holders release within milliseconds, so a short backoff turns a
    /// hard failure into a retry.
    /// </summary>
    private const int ReplaceAttempts = 3;

    /// <summary>
    /// Delete a file we created ourselves, ignoring any failure. Cleanup runs on paths that are
    /// already reporting some other outcome; an exception here would replace the real error (or
    /// fail an operation that actually succeeded) with a misleading one about our scratch file.
    /// </summary>
    public static void TryDeleteQuietly(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { File.Delete(path); } catch { }
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

        // Randomized like the temp file rather than a fixed "<target>.tmp": a fixed name collides
        // when the same file is edited concurrently, and — because File.Replace can create the
        // backup before failing — a leftover from an earlier failure makes every later replace
        // fail the same way, turning a one-off error into a permanent one.
        var backupTemp = CreateBackupTempPath(targetPath);
        try
        {
            ReplaceWithRetry(tempFile, targetPath, backupTemp);
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback for platforms where File.Replace is unsupported
            TryDeleteQuietly(backupTemp);
            File.Move(targetPath, backupTemp);
            try
            {
                File.Move(tempFile, targetPath);
            }
            catch
            {
                // The original now exists only as backupTemp, and the finally below would delete
                // it. Put it back so a failure here leaves the file as it was, not missing.
                File.Move(backupTemp, targetPath);
                throw;
            }
        }
        finally
        {
            // In a finally so a failed swap does not leave the backup next to the user's file.
            // Safe on every path above: either the swap completed and this is the stale original,
            // or File.Replace failed with the target untouched, or the fallback restored it.
            TryDeleteQuietly(backupTemp);
        }
    }

    private static string CreateBackupTempPath(string targetPath)
    {
        var fullPath = Path.GetFullPath(targetPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(dir))
            dir = Directory.GetCurrentDirectory();

        return Path.Combine(dir, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.bak");
    }

    private static void ReplaceWithRetry(string tempFile, string targetPath, string backupTemp)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Replace(tempFile, targetPath, backupTemp);
                return;
            }
            catch (IOException) when (attempt < ReplaceAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (IOException ex)
            {
                throw new IOException(DescribeReplaceFailure(ex, tempFile, targetPath, backupTemp), ex);
            }
        }
    }

    /// <summary>
    /// The raw .NET message for a failed File.Replace names none of the three paths involved and
    /// omits the Win32 code, which leaves nothing to diagnose from. Restate it with both.
    /// </summary>
    private static string DescribeReplaceFailure(IOException ex, string tempFile, string targetPath, string backupTemp)
    {
        int win32 = ex.HResult & 0xFFFF;

        var sb = new StringBuilder();
        sb.Append(ex.Message.TrimEnd());
        sb.Append($" (Win32 error {win32}, after {ReplaceAttempts} attempts)");
        sb.AppendLine();
        sb.AppendLine($"  target      : {targetPath}");
        sb.AppendLine($"  replacement : {tempFile}");
        sb.Append($"  backup      : {backupTemp}");

        if (win32 == 1176) // ERROR_UNABLE_TO_MOVE_REPLACEMENT
        {
            sb.AppendLine();
            sb.Append("  The target is most likely held open by another process — an editor, an antivirus");
            sb.Append(" real-time scan, the search indexer, or a cloud-sync client. The file was left unchanged.");
        }

        return sb.ToString();
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
