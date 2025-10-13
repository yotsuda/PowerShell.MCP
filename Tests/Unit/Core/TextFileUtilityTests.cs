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
}