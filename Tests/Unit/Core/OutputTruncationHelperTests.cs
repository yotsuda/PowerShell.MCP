using PowerShell.MCP;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

public class OutputTruncationHelperTests : IDisposable
{
    private readonly string _testDir;

    public OutputTruncationHelperTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"OutputTruncationHelperTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    #region TruncateIfNeeded Tests

    [Fact]
    public void TruncateIfNeeded_Empty_ReturnsEmpty()
    {
        var result = OutputTruncationHelper.TruncateIfNeeded("", _testDir);
        Assert.Equal("", result);
    }

    [Fact]
    public void TruncateIfNeeded_UnderThreshold_ReturnsUnchanged()
    {
        var output = new string('A', OutputTruncationHelper.TruncationThreshold - 1);
        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);
        Assert.Equal(output, result);
    }

    [Fact]
    public void TruncateIfNeeded_ExactThreshold_ReturnsUnchanged()
    {
        var output = new string('B', OutputTruncationHelper.TruncationThreshold);
        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);
        Assert.Equal(output, result);
    }

    [Fact]
    public void TruncateIfNeeded_OverThreshold_ReturnsTruncatedWithFilePath()
    {
        var output = new string('C', OutputTruncationHelper.TruncationThreshold + 1);
        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        // Should contain file path info
        Assert.Contains("Full output saved to:", result);
        Assert.Contains("pwsh_output_", result);
        Assert.Contains(".txt", result);
    }

    [Fact]
    public void TruncateIfNeeded_OverThreshold_SavesFullContentToFile()
    {
        var output = new string('D', OutputTruncationHelper.TruncationThreshold + 500);
        OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        // Verify file was saved with full content
        var files = Directory.GetFiles(_testDir, "pwsh_output_*.txt");
        Assert.Single(files);
        var savedContent = File.ReadAllText(files[0]);
        Assert.Equal(output, savedContent);
    }

    [Fact]
    public void TruncateIfNeeded_OverThreshold_PreviewContainsHeadAndTail()
    {
        // Build output with identifiable head and tail regions
        var head = "HEAD_MARKER_" + new string('X', 500);
        var middle = new string('M', OutputTruncationHelper.TruncationThreshold);
        var tail = new string('Y', 500) + "_TAIL_MARKER";
        var output = head + middle + tail;

        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        Assert.Contains("HEAD_MARKER_", result);
        Assert.Contains("_TAIL_MARKER", result);
        Assert.Contains("truncated", result);
    }

    [Fact]
    public void TruncateIfNeeded_OverThreshold_PreviewTailMatchesOutputEnd()
    {
        // The tail preview should end with the same content as the original output
        var tailContent = "FINAL_LINE_OF_OUTPUT";
        var output = new string('Z', OutputTruncationHelper.TruncationThreshold + 2000) + tailContent;

        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        Assert.EndsWith(tailContent, result);
    }

    [Fact]
    public void TruncateIfNeeded_OverThreshold_HeadAlignsToNewline()
    {
        // Place a newline within the scan range before PreviewHeadSize
        // Head should cut at the newline boundary rather than at exactly PreviewHeadSize
        var headSize = OutputTruncationHelper.PreviewHeadSize;
        var newlinePos = headSize - 50; // newline within scan range

        var chars = new char[OutputTruncationHelper.TruncationThreshold + 2000];
        Array.Fill(chars, 'A');
        chars[newlinePos] = '\n';
        var output = new string(chars);

        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        // Extract the head content: appears after "--- Preview (first ~1000 chars) ---" + newline
        var headMarker = "--- Preview (first ~1000 chars) ---" + Environment.NewLine;
        var headStart = result.IndexOf(headMarker);
        Assert.True(headStart >= 0, "Should contain head preview marker");
        var headContentStart = headStart + headMarker.Length;

        var truncatedMarker = "--- truncated";
        var truncatedIndex = result.IndexOf(truncatedMarker, headContentStart);
        Assert.True(truncatedIndex > 0, "Should contain 'truncated' marker");

        // Head content is between the marker and the truncated line (minus trailing newline from AppendLine)
        var headContent = result.Substring(headContentStart, truncatedIndex - headContentStart)
            .TrimEnd('\n', '\r');
        // Head should have been cut at the newline (position 950+1 = 951 chars)
        Assert.Equal(newlinePos + 1, headContent.Length + 1); // +1 because the \n is the cut point
    }

    [Fact]
    public void TruncateIfNeeded_OverThreshold_TailAlignsToNewline()
    {
        // Place a newline near the tail start position
        var tailSize = OutputTruncationHelper.PreviewTailSize;
        var outputLength = OutputTruncationHelper.TruncationThreshold + 2000;
        var tailStartPos = outputLength - tailSize;
        var newlinePos = tailStartPos + 50; // newline shortly after tail start, within scan range

        var chars = new char[outputLength];
        Array.Fill(chars, 'B');
        chars[newlinePos] = '\n';
        var output = new string(chars);

        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        // Extract the tail content: appears after "--- Preview (last ~2000 chars) ---" + newline
        var tailMarker = "--- Preview (last ~2000 chars) ---" + Environment.NewLine;
        var tailMarkerPos = result.LastIndexOf(tailMarker);
        Assert.True(tailMarkerPos >= 0, "Should contain tail preview marker");
        var tailContent = result.Substring(tailMarkerPos + tailMarker.Length);

        // Tail should start at the character after the planted newline
        var expectedTail = output[(newlinePos + 1)..];
        Assert.Equal(expectedTail, tailContent);
    }

    [Fact]
    public void TruncateIfNeeded_OverThreshold_NoNewlineInScanRange_HardCuts()
    {
        // Output with no newlines at all — should hard-cut at exact positions
        var output = new string('Q', OutputTruncationHelper.TruncationThreshold + 3000);

        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        // Should still produce a valid truncated result with head and tail
        Assert.Contains("truncated", result);
        Assert.Contains("Preview", result);
        // The total result should be much shorter than the original output
        Assert.True(result.Length < output.Length, "Truncated result should be shorter than original");
    }

    [Fact]
    public void TruncateIfNeeded_FileSaveFails_StillReturnsPreview()
    {
        // Null characters are universally invalid in file paths across all platforms
        var invalidDir = Path.Combine(_testDir, "invalid\0dir");

        var output = new string('E', OutputTruncationHelper.TruncationThreshold + 1000);
        var result = OutputTruncationHelper.TruncateIfNeeded(output, invalidDir);

        // Should still return a preview even though file save failed
        Assert.Contains("truncated", result);
        Assert.Contains("Preview", result);
        // Should NOT contain "saved to" since the save failed
        Assert.DoesNotContain("Full output saved to:", result);
    }

    [Fact]
    public void TruncateIfNeeded_CustomOutputDirectory_SavesToSpecifiedDir()
    {
        var customDir = Path.Combine(_testDir, "custom_output");
        Directory.CreateDirectory(customDir);

        var output = new string('F', OutputTruncationHelper.TruncationThreshold + 500);
        var result = OutputTruncationHelper.TruncateIfNeeded(output, customDir);

        // File should be saved in the custom directory
        var files = Directory.GetFiles(customDir, "pwsh_output_*.txt");
        Assert.Single(files);
        Assert.Contains(customDir.Replace("\\", "/"), result.Replace("\\", "/"));
    }

    [Fact]
    public void TruncateIfNeeded_MessageShowsCharacters_NotKB()
    {
        var output = new string('G', OutputTruncationHelper.TruncationThreshold + 1000);
        var result = OutputTruncationHelper.TruncateIfNeeded(output, _testDir);

        Assert.Contains("characters", result);
        Assert.DoesNotContain("KB", result);
        Assert.DoesNotContain("bytes", result);
    }

    #endregion

    #region CleanupOldOutputFiles Tests

    [Fact]
    public void CleanupOldOutputFiles_RemovesOldFiles()
    {
        // Create a file and set its last write time to beyond MaxFileAgeMinutes
        var oldFile = Path.Combine(_testDir, "pwsh_output_old_test.txt");
        File.WriteAllText(oldFile, "old content");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddMinutes(-(OutputTruncationHelper.MaxFileAgeMinutes + 10)));

        OutputTruncationHelper.CleanupOldOutputFiles(_testDir);

        Assert.False(File.Exists(oldFile), "Old file should have been deleted");
    }

    [Fact]
    public void CleanupOldOutputFiles_KeepsRecentFiles()
    {
        // Create a recent file (within MaxFileAgeMinutes)
        var recentFile = Path.Combine(_testDir, "pwsh_output_recent_test.txt");
        File.WriteAllText(recentFile, "recent content");
        // Last write time is now (default), well within the threshold

        OutputTruncationHelper.CleanupOldOutputFiles(_testDir);

        Assert.True(File.Exists(recentFile), "Recent file should NOT have been deleted");
    }

    [Fact]
    public void CleanupOldOutputFiles_NonexistentDir_NoThrow()
    {
        var nonexistentDir = Path.Combine(_testDir, "does_not_exist_" + Guid.NewGuid().ToString("N"));

        // Should not throw even when directory doesn't exist
        var exception = Record.Exception(() => OutputTruncationHelper.CleanupOldOutputFiles(nonexistentDir));
        Assert.Null(exception);
    }

    [Fact]
    public void CleanupOldOutputFiles_IgnoresIOException()
    {
        // Create an old file
        var lockedFile = Path.Combine(_testDir, "pwsh_output_locked_test.txt");
        File.WriteAllText(lockedFile, "locked content");
        File.SetLastWriteTime(lockedFile, DateTime.Now.AddMinutes(-(OutputTruncationHelper.MaxFileAgeMinutes + 10)));

        // Lock the file by opening it with exclusive access
        using var stream = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None);

        // Should not throw even though the file is locked and cannot be deleted
        var exception = Record.Exception(() => OutputTruncationHelper.CleanupOldOutputFiles(_testDir));
        Assert.Null(exception);
    }

    #endregion
}
