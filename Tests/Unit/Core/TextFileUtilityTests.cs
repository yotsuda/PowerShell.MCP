using Xunit;
using PowerShell.MCP.Cmdlets;
using System.Text;

namespace PowerShell.MCP.Tests;

public class TextFileUtilityTests
{
    [Fact]
    public void DetectEncoding_WithUtf8File_ReturnsUtf8()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var content = "This is a UTF-8 test file with 日本語";
        File.WriteAllText(tempFile, content, new UTF8Encoding(false));

        try
        {
            // Act
            var encoding = TextFileUtility.DetectEncoding(tempFile);

            // Assert
            Assert.NotNull(encoding);
            Assert.True(encoding is UTF8Encoding || encoding.CodePage == 65001);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFileMetadata_WithValidFile_ReturnsMetadata()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var lines = new[] { "Line 1", "Line 2", "Line 3" };
        File.WriteAllLines(tempFile, lines, new UTF8Encoding(false));

        try
        {
            // Act
            var metadata = TextFileUtility.DetectFileMetadata(tempFile);

            // Assert
            Assert.NotNull(metadata);
            Assert.NotNull(metadata.Encoding);
            Assert.NotNull(metadata.NewlineSequence);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFileMetadata_WithEmptyFile_ReturnsDefaultMetadata()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, string.Empty);

        try
        {
            // Act
            var metadata = TextFileUtility.DetectFileMetadata(tempFile);

            // Assert
            Assert.NotNull(metadata);
            Assert.NotNull(metadata.Encoding);
            Assert.False(metadata.HasTrailingNewline);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(1, 3, 1, 3)]
    [InlineData(2, 2, 2, 2)]
    [InlineData(5, 10, 5, 10)]
    public void ParseLineRange_WithValidRange_ReturnsCorrectValues(int input1, int input2, int expectedStart, int expectedEnd)
    {
        // Arrange
        var lineRange = new int[] { input1, input2 };

        // Act
        var (startLine, endLine) = TextFileUtility.ParseLineRange(lineRange);

        // Assert
        Assert.Equal(expectedStart, startLine);
        Assert.Equal(expectedEnd, endLine);
    }

    [Fact]
    public void ParseLineRange_WithNullRange_ReturnsZeroForBoth()
    {
        // Arrange
        int[]? lineRange = null;

        // Act
        var (startLine, endLine) = TextFileUtility.ParseLineRange(lineRange);

        // Assert
        Assert.Equal(1, startLine);
        Assert.Equal(int.MaxValue, endLine);
    }

    [Fact]
    public void ConvertToStringArray_WithString_ReturnsArray()
    {
        // Arrange
        var input = "test string";

        // Act
        var result = TextFileUtility.ConvertToStringArray(input);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("test string", result[0]);
    }

    [Fact]
    public void ConvertToStringArray_WithStringArray_ReturnsArray()
    {
        // Arrange
        var input = new[] { "line1", "line2" };

        // Act
        var result = TextFileUtility.ConvertToStringArray(input);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
        Assert.Equal("line1", result[0]);
        Assert.Equal("line2", result[1]);
    }

    [Fact]
    public void ConvertToStringArray_WithNull_ReturnsEmptyArray()
    {
        // Arrange
        object? input = null;

        // Act
        var result = TextFileUtility.ConvertToStringArray(input);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void GetRelativePath_WithValidPaths_ReturnsRelativePath()
    {
        // Arrange
        var fromPath = "C:\\Projects\\MyProject";
        var toPath = "C:\\Projects\\MyProject\\SubFolder\\file.txt";

        // Act
        var result = TextFileUtility.GetRelativePath(fromPath, toPath);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("SubFolder", result);
    }

    [Fact]
    public void CreateBackup_WithValidFile_CreatesBackupFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test content");

        try
        {
            // Act
            var backupPath = TextFileUtility.CreateBackup(tempFile);

            // Assert
            Assert.True(File.Exists(backupPath));
            Assert.Equal("test content", File.ReadAllText(backupPath));

            // Cleanup
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetEncoding_WithUtf8Name_ReturnsUtf8Encoding()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test");

        try
        {
            // Act
            var encoding = TextFileUtility.GetEncoding(tempFile, "utf-8");

            // Assert
            Assert.NotNull(encoding);
            Assert.True(encoding is UTF8Encoding || encoding.CodePage == 65001);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
    [Fact]
    public void DetectEncoding_WithShiftJISFile_ReturnsCorrectEncoding()
    {
        // Skip if Shift_JIS is not available (e.g., in .NET 9 without CodePages provider)
        try
        {
            var _ = System.Text.Encoding.GetEncoding("shift_jis");
        }
        catch (ArgumentException)
        {
            // Shift_JIS not available, skip test
            return;
        }

        // Arrange
        var tempFile = Path.GetTempFileName();
        var encoding = System.Text.Encoding.GetEncoding("shift_jis");
        var content = "これはShift_JISのテストです";
        File.WriteAllText(tempFile, content, encoding);

        try
        {
            // Act
            var detectedEncoding = TextFileUtility.DetectEncoding(tempFile);

            // Assert
            Assert.NotNull(detectedEncoding);
            // Shift_JIS might be detected or fall back to another encoding
            Assert.NotEqual(System.Text.Encoding.ASCII, detectedEncoding);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetEncoding_WithInvalidEncodingName_ReturnsDetectedEncoding()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "test content");

        try
        {
            // Act
            var encoding = TextFileUtility.GetEncoding(tempFile, "invalid-encoding-name");

            // Assert
            Assert.NotNull(encoding);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void ParseLineRange_WithSingleElement_ReturnsSameForBoth(int value)
    {
        // Arrange
        var lineRange = new int[] { value };

        // Act
        var (startLine, endLine) = TextFileUtility.ParseLineRange(lineRange);

        // Assert
        // Single value is treated as both start and end line
        Assert.Equal(value, startLine);
        Assert.Equal(value, endLine);
    }

    [Fact]
    public void DetectFileMetadata_WithWindowsLineEndings_DetectsCRLF()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Line1\r\nLine2\r\n");

        try
        {
            // Act
            var metadata = TextFileUtility.DetectFileMetadata(tempFile);

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal("\r\n", metadata.NewlineSequence);
            Assert.True(metadata.HasTrailingNewline);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFileMetadata_WithUnixLineEndings_DetectsLF()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Line1\nLine2\n");

        try
        {
            // Act
            var metadata = TextFileUtility.DetectFileMetadata(tempFile);

            // Assert
            Assert.NotNull(metadata);
            Assert.Equal("\n", metadata.NewlineSequence);
            Assert.True(metadata.HasTrailingNewline);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void DetectFileMetadata_WithoutTrailingNewline_HasTrailingNewlineIsFalse()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Line1\nLine2");

        try
        {
            // Act
            var metadata = TextFileUtility.DetectFileMetadata(tempFile);

            // Assert
            Assert.NotNull(metadata);
            Assert.False(metadata.HasTrailingNewline);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }



    [Fact]
    public void CreateBackup_CreatesFileWithBakExtension()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "original content");

        try
        {
            // Act
            var backupPath = TextFileUtility.CreateBackup(tempFile);

            // Assert
            Assert.True(File.Exists(backupPath));
            Assert.EndsWith(".bak", backupPath);
            Assert.Equal("original content", File.ReadAllText(backupPath));

            // Cleanup
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ======================================================================
    // ProcessFileStreaming Tests
    // ======================================================================

    [Fact]
    public void ProcessFileStreaming_WithNormalFile_ProcessesAllLines()
    {
        // Arrange
        var inputFile = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();
        var lines = new[] { "Line 1", "Line 2", "Line 3" };
        File.WriteAllLines(inputFile, lines, new UTF8Encoding(false));

        var metadata = TextFileUtility.DetectFileMetadata(inputFile);
        var processedLines = new List<string>();

        try
        {
            // Act
            TextFileUtility.ProcessFileStreaming(
                inputFile,
                outputFile,
                metadata,
                (line, lineNum) => {
                    processedLines.Add(line);
                    return line.ToUpper();
                });

            // Assert
            Assert.Equal(3, processedLines.Count);
            var result = File.ReadAllLines(outputFile);
            Assert.Equal(3, result.Length);
            Assert.Equal("LINE 1", result[0]);
            Assert.Equal("LINE 2", result[1]);
            Assert.Equal("LINE 3", result[2]);
        }
        finally
        {
            File.Delete(inputFile);
            File.Delete(outputFile);
        }
    }

    [Fact]
    public void ProcessFileStreaming_WithEmptyFile_HandlesGracefully()
    {
        // Arrange
        var inputFile = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();
        File.WriteAllText(inputFile, string.Empty);

        var metadata = TextFileUtility.DetectFileMetadata(inputFile);

        try
        {
            // Act
            TextFileUtility.ProcessFileStreaming(
                inputFile,
                outputFile,
                metadata,
                (line, lineNum) => line.ToUpper());

            // Assert
            var result = File.ReadAllText(outputFile);
            Assert.Empty(result);
        }
        finally
        {
            File.Delete(inputFile);
            File.Delete(outputFile);
        }
    }

    [Fact]
    public void ProcessFileStreaming_PreservesTrailingNewline()
    {
        // Arrange
        var inputFile = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();
        File.WriteAllText(inputFile, "Line 1\nLine 2\n", new UTF8Encoding(false));

        var metadata = TextFileUtility.DetectFileMetadata(inputFile);

        try
        {
            // Act
            TextFileUtility.ProcessFileStreaming(
                inputFile,
                outputFile,
                metadata,
                (line, lineNum) => line);

            // Assert
            var result = File.ReadAllText(outputFile);
            Assert.EndsWith("\n", result);
        }
        finally
        {
            File.Delete(inputFile);
            File.Delete(outputFile);
        }
    }

    [Fact]
    public void ProcessFileStreaming_WithoutTrailingNewline_DoesNotAddOne()
    {
        // Arrange
        var inputFile = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();
        File.WriteAllText(inputFile, "Line 1\nLine 2", new UTF8Encoding(false));

        var metadata = TextFileUtility.DetectFileMetadata(inputFile);

        try
        {
            // Act
            TextFileUtility.ProcessFileStreaming(
                inputFile,
                outputFile,
                metadata,
                (line, lineNum) => line);

            // Assert
            var result = File.ReadAllText(outputFile);
            Assert.DoesNotMatch(@"\n$", result);
        }
        finally
        {
            File.Delete(inputFile);
            File.Delete(outputFile);
        }
    }

    [Fact]
    public void ProcessFileStreaming_PreservesEncoding()
    {
        // Arrange
        var inputFile = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();
        var content = "日本語テスト";
        File.WriteAllText(inputFile, content, new UTF8Encoding(false));

        var metadata = TextFileUtility.DetectFileMetadata(inputFile);

        try
        {
            // Act
            TextFileUtility.ProcessFileStreaming(
                inputFile,
                outputFile,
                metadata,
                (line, lineNum) => line);

            // Assert
            var result = File.ReadAllText(outputFile, new UTF8Encoding(false));
            Assert.Equal(content, result);
        }
        finally
        {
            File.Delete(inputFile);
            File.Delete(outputFile);
        }
    }

    [Fact]
    public void ProcessFileStreaming_PreservesWindowsLineEndings()
    {
        // Arrange
        var inputFile = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();
        File.WriteAllText(inputFile, "Line 1\r\nLine 2\r\nLine 3\r\n", new UTF8Encoding(false));

        var metadata = TextFileUtility.DetectFileMetadata(inputFile);

        try
        {
            // Act
            TextFileUtility.ProcessFileStreaming(
                inputFile,
                outputFile,
                metadata,
                (line, lineNum) => line);

            // Assert
            var result = File.ReadAllText(outputFile);
            Assert.Contains("\r\n", result);
            Assert.DoesNotContain("\n\n", result); // Should not have double newlines
        }
        finally
        {
            File.Delete(inputFile);
            File.Delete(outputFile);
        }
    }

    // ======================================================================
    // ReplaceFileAtomic Tests
    // ======================================================================

    [Fact]
    public void ReplaceFileAtomic_WithNewFile_CreatesFile()
    {
        // Arrange
        var targetFile = Path.Combine(Path.GetTempPath(), "test_new_" + Guid.NewGuid() + ".txt");
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "new content");

        try
        {
            // Act
            TextFileUtility.ReplaceFileAtomic(targetFile, tempFile);

            // Assert
            Assert.True(File.Exists(targetFile));
            Assert.Equal("new content", File.ReadAllText(targetFile));
            Assert.False(File.Exists(tempFile)); // Temp file should be moved
        }
        finally
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReplaceFileAtomic_WithExistingFile_ReplacesAtomically()
    {
        // Arrange
        var targetFile = Path.GetTempFileName();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(targetFile, "original content");
        File.WriteAllText(tempFile, "new content");

        try
        {
            // Act
            TextFileUtility.ReplaceFileAtomic(targetFile, tempFile);

            // Assert
            Assert.True(File.Exists(targetFile));
            Assert.Equal("new content", File.ReadAllText(targetFile));
            Assert.False(File.Exists(tempFile)); // Temp file should be moved
        }
        finally
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void ReplaceFileAtomic_RemovesTemporaryBackupFile()
    {
        // Arrange
        var targetFile = Path.GetTempFileName();
        var tempFile = Path.GetTempFileName();
        File.WriteAllText(targetFile, "original");
        File.WriteAllText(tempFile, "new");
        var backupTemp = targetFile + ".tmp";

        try
        {
            // Act
            TextFileUtility.ReplaceFileAtomic(targetFile, tempFile);

            // Assert
            Assert.False(File.Exists(backupTemp)); // Temporary backup should be deleted
        }
        finally
        {
            if (File.Exists(targetFile))
                File.Delete(targetFile);
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            if (File.Exists(backupTemp))
                File.Delete(backupTemp);
        }
    }

    // ======================================================================
    // TryUpgradeEncodingIfNeeded Tests
    // ======================================================================

    [Fact]
    public void TryUpgradeEncodingIfNeeded_WithNonAsciiContent_UpgradesToUtf8()
    {
        // Arrange
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\n",
            HasTrailingNewline = true
        };
        var contentLines = new[] { "Hello", "日本語", "World" };

        // Act
        var upgraded = TextFileUtility.TryUpgradeEncodingIfNeeded(
            metadata,
            contentLines,
            encodingExplicitlySpecified: false,
            out var message);

        // Assert
        Assert.True(upgraded);
        Assert.NotNull(message);
        Assert.Contains("UTF-8", message);
        Assert.IsType<UTF8Encoding>(metadata.Encoding);
    }

    [Fact]
    public void TryUpgradeEncodingIfNeeded_WithAsciiContent_DoesNotUpgrade()
    {
        // Arrange
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\n",
            HasTrailingNewline = true
        };
        var contentLines = new[] { "Hello", "World", "ASCII only" };

        // Act
        var upgraded = TextFileUtility.TryUpgradeEncodingIfNeeded(
            metadata,
            contentLines,
            encodingExplicitlySpecified: false,
            out var message);

        // Assert
        Assert.False(upgraded);
        Assert.Null(message);
        Assert.Equal(Encoding.ASCII, metadata.Encoding);
    }

    [Fact]
    public void TryUpgradeEncodingIfNeeded_WithExplicitEncoding_DoesNotUpgrade()
    {
        // Arrange
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\n",
            HasTrailingNewline = true
        };
        var contentLines = new[] { "Hello", "日本語", "World" };

        // Act
        var upgraded = TextFileUtility.TryUpgradeEncodingIfNeeded(
            metadata,
            contentLines,
            encodingExplicitlySpecified: true, // Explicitly specified
            out var message);

        // Assert
        Assert.False(upgraded);
        Assert.Null(message);
        Assert.Equal(Encoding.ASCII, metadata.Encoding);
    }

    [Fact]
    public void TryUpgradeEncodingIfNeeded_WithUtf8Encoding_DoesNotUpgrade()
    {
        // Arrange
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = new UTF8Encoding(false),
            NewlineSequence = "\n",
            HasTrailingNewline = true
        };
        var contentLines = new[] { "Hello", "日本語", "World" };

        // Act
        var upgraded = TextFileUtility.TryUpgradeEncodingIfNeeded(
            metadata,
            contentLines,
            encodingExplicitlySpecified: false,
            out var message);

        // Assert
        Assert.False(upgraded);
        Assert.Null(message);
    }

    [Fact]
    public void TryUpgradeEncodingIfNeeded_WithEmptyContent_DoesNotUpgrade()
    {
        // Arrange
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\n",
            HasTrailingNewline = false
        };
        var contentLines = Array.Empty<string>();

        // Act
        var upgraded = TextFileUtility.TryUpgradeEncodingIfNeeded(
            metadata,
            contentLines,
            encodingExplicitlySpecified: false,
            out var message);

        // Assert
        Assert.False(upgraded);
        Assert.Null(message);
    }

    // ======================================================================
    // ParseLineRange Tests - Zero and Negative Values (End of File)
    // ======================================================================

    [Theory]
    [InlineData(100, -1, 100, int.MaxValue)]  // -1 means end of file
    [InlineData(100, 0, 100, int.MaxValue)]   // 0 means end of file
    [InlineData(50, -99, 50, int.MaxValue)]   // Any negative value means end of file
    [InlineData(1, -1, 1, int.MaxValue)]      // From first line to end
    public void ParseLineRange_WithZeroOrNegativeEnd_ReturnsMaxValue(int start, int end, int expectedStart, int expectedEnd)
    {
        // Arrange
        var lineRange = new int[] { start, end };

        // Act
        var (startLine, endLine) = TextFileUtility.ParseLineRange(lineRange);

        // Assert
        Assert.Equal(expectedStart, startLine);
        Assert.Equal(expectedEnd, endLine);
    }

    [Fact]
    public void ParseLineRange_WithSingleValue_ReturnsSameStartAndEnd()
    {
        // Arrange
        var lineRange = new int[] { 100 };

        // Act
        var (startLine, endLine) = TextFileUtility.ParseLineRange(lineRange);

        // Assert
        Assert.Equal(100, startLine);
        Assert.Equal(100, endLine);  // Single value = that line only
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(50, 100)]
    [InlineData(200, 300)]
    public void ParseLineRange_WithPositiveRange_ReturnsExactRange(int start, int end)
    {
        // Arrange
        var lineRange = new int[] { start, end };

        // Act
        var (startLine, endLine) = TextFileUtility.ParseLineRange(lineRange);

        // Assert
        Assert.Equal(start, startLine);
        Assert.Equal(end, endLine);
    }

    [Fact]
    public void ParseLineRange_WithMoreThanTwoValues_ThrowsArgumentException()
    {
        // Arrange
        var lineRange = new int[] { 1, 2, 3 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            TextFileUtility.ParseLineRange(lineRange));
        Assert.Contains("LineRange accepts 1 or 2 values", exception.Message);
    }
}
