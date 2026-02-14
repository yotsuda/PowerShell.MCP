namespace PowerShell.MCP.Proxy.Helpers;

/// <summary>
/// Truncates large command output to preserve AI context window budget.
/// Saves the full output to a temp file so the AI can retrieve it via Read tool if needed.
/// </summary>
public static class OutputTruncationHelper
{
    internal const int TruncationThreshold = 5_000;
    internal const int PreviewHeadSize = 1000;
    internal const int PreviewTailSize = 1000;
    internal const string OutputDirectoryName = "PowerShell.MCP.Output";
    internal const int MaxFileAgeMinutes = 120;
    internal const int NewlineScanLimit = 200;

    /// <summary>
    /// Returns the output unchanged if within threshold, otherwise saves the full content
    /// to a temp file and returns a head+tail preview with the file path.
    /// </summary>
    public static string TruncateIfNeeded(string output, string? outputDirectory = null)
    {
        if (output.Length <= TruncationThreshold)
            return output;

        // Compute newline-aligned head boundary
        var headEnd = FindHeadBoundary(output, PreviewHeadSize);
        var head = output[..headEnd];

        // Compute newline-aligned tail boundary
        var tailStart = FindTailBoundary(output, PreviewTailSize);
        var tail = output[tailStart..];

        var omitted = output.Length - head.Length - tail.Length;

        var filePath = SaveOutputToFile(output, outputDirectory);

        var sb = new System.Text.StringBuilder();

        if (filePath != null)
        {
            sb.AppendLine($"Output too large ({output.Length} characters). Full output saved to: {filePath}");
            sb.AppendLine($"Use invoke_expression('Get-Content \"{filePath}\"') or Read tool to access the full output.");
        }
        else
        {
            // Disk save failed — still provide the preview without a file path
            sb.AppendLine($"Output too large ({output.Length} characters). Could not save full output to file.");
        }

        sb.AppendLine();
        sb.AppendLine("--- Preview (first ~1000 chars) ---");
        sb.AppendLine(head);
        sb.AppendLine($"--- truncated ({omitted} chars omitted) ---");
        sb.AppendLine("--- Preview (last ~1000 chars) ---");
        sb.Append(tail);

        return sb.ToString();
    }

    /// <summary>
    /// Saves the full output to a timestamped temp file and triggers opportunistic cleanup.
    /// Returns the file path on success, null on failure.
    /// </summary>
    internal static string? SaveOutputToFile(string output, string? outputDirectory = null)
    {
        try
        {
            var directory = outputDirectory
                ?? Path.Combine(Path.GetTempPath(), OutputDirectoryName);

            Directory.CreateDirectory(directory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var random = Path.GetRandomFileName();
            var fileName = $"pwsh_output_{timestamp}_{random}.txt";
            var filePath = Path.Combine(directory, fileName);

            File.WriteAllText(filePath, output);

            // Opportunistic cleanup — never let it block or fail the save
            CleanupOldOutputFiles(directory);

            return filePath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes pwsh_output_*.txt files older than <see cref="MaxFileAgeMinutes"/> minutes.
    /// Each deletion is individually guarded so a locked file does not prevent other cleanups.
    /// </summary>
    internal static void CleanupOldOutputFiles(string? directory = null)
    {
        try
        {
            var dir = directory
                ?? Path.Combine(Path.GetTempPath(), OutputDirectoryName);

            if (!Directory.Exists(dir))
                return;

            var cutoff = DateTime.Now.AddMinutes(-MaxFileAgeMinutes);

            foreach (var file in Directory.EnumerateFiles(dir, "pwsh_output_*.txt"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch (IOException)
                {
                    // Another thread may be writing — safe to ignore
                }
            }
        }
        catch
        {
            // Directory enumeration itself failed — nothing to clean up
        }
    }

    /// <summary>
    /// Finds a head cut position aligned to the nearest preceding newline within scan limit.
    /// </summary>
    private static int FindHeadBoundary(string output, int nominalSize)
    {
        if (nominalSize >= output.Length)
            return output.Length;

        // Search backward from nominalSize for a newline, up to NewlineScanLimit chars
        var searchStart = Math.Max(0, nominalSize - NewlineScanLimit);
        var lastNewline = output.LastIndexOf('\n', nominalSize - 1, nominalSize - searchStart);

        // Cut after the newline to keep complete lines in the head
        return lastNewline >= 0 ? lastNewline + 1 : nominalSize;
    }

    /// <summary>
    /// Finds a tail start position aligned to the nearest following newline within scan limit.
    /// </summary>
    private static int FindTailBoundary(string output, int nominalSize)
    {
        var nominalStart = output.Length - nominalSize;
        if (nominalStart <= 0)
            return 0;

        // Search forward from nominalStart for a newline, up to NewlineScanLimit chars
        var searchEnd = Math.Min(output.Length, nominalStart + NewlineScanLimit);
        var nextNewline = output.IndexOf('\n', nominalStart, searchEnd - nominalStart);

        // Start at the character after the newline to begin on a fresh line
        return nextNewline >= 0 ? nextNewline + 1 : nominalStart;
    }
}
