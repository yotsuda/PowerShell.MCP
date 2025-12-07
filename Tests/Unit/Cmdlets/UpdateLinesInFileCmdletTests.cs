using System;
using System.IO;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class UpdateLinesInFileCmdletTests : IDisposable
{
    private readonly string _testFile;

    public UpdateLinesInFileCmdletTests()
    {
        _testFile = Path.GetTempFileName();
        File.WriteAllLines(_testFile, new[] { "Line 1", "Line 2", "Line 3" });
    }

    public void Dispose()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        var cmdlet = new UpdateLinesInFileCmdlet();
        Assert.NotNull(cmdlet);
        Assert.IsAssignableFrom<TextFileCmdletBase>(cmdlet);
    }

    [Fact]
    public void Path_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateLinesInFileCmdlet();
        cmdlet.Path = new[] { "test.txt" };
        Assert.NotNull(cmdlet.Path);
    }

    [Fact]
    public void LineRange_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateLinesInFileCmdlet();
        cmdlet.LineRange = new[] { 1, 3 };
        Assert.NotNull(cmdlet.LineRange);
        Assert.Equal(2, cmdlet.LineRange.Length);
    }

    [Fact]
    public void Content_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateLinesInFileCmdlet();
        cmdlet.Content = new object[] { "New content" };
        Assert.NotNull(cmdlet.Content);
    }

    [Fact]
    public void ReplaceEntireFile_PreservesBom_WhenFileHasBom()
    {
        // Arrange: Create a file with UTF-8 BOM
        var tempFile = Path.GetTempFileName();
        var tempOutput = Path.GetTempFileName();
        try
        {
            var utf8WithBom = new System.Text.UTF8Encoding(true);
            File.WriteAllText(tempFile, "Line1\nLine2\nLine3\n", utf8WithBom);
            
            // Verify file has BOM before
            var bytesBefore = File.ReadAllBytes(tempFile);
            Assert.True(bytesBefore.Length >= 3 && bytesBefore[0] == 0xEF && bytesBefore[1] == 0xBB && bytesBefore[2] == 0xBF,
                "Test file should have BOM");
            
            // Act: Detect metadata and replace entire file
            var metadata = TextFileUtility.DetectFileMetadata(tempFile);
            TextFileUtility.ReplaceEntireFile(tempFile, tempOutput, metadata, new[] { "NewA", "NewB" });
            
            // Assert: Output file should have BOM
            var bytesAfter = File.ReadAllBytes(tempOutput);
            Assert.True(bytesAfter.Length >= 3 && bytesAfter[0] == 0xEF && bytesAfter[1] == 0xBB && bytesAfter[2] == 0xBF,
                "Output file should preserve BOM");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public void ReplaceEntireFile_PreservesNoBom_WhenFileHasNoBom()
    {
        // Arrange: Create a file without BOM
        var tempFile = Path.GetTempFileName();
        var tempOutput = Path.GetTempFileName();
        try
        {
            var utf8NoBom = new System.Text.UTF8Encoding(false);
            File.WriteAllText(tempFile, "Line1\nLine2\nLine3\n", utf8NoBom);
            
            // Verify file has no BOM before
            var bytesBefore = File.ReadAllBytes(tempFile);
            Assert.False(bytesBefore.Length >= 3 && bytesBefore[0] == 0xEF && bytesBefore[1] == 0xBB && bytesBefore[2] == 0xBF,
                "Test file should not have BOM");
            
            // Act: Detect metadata and replace entire file
            var metadata = TextFileUtility.DetectFileMetadata(tempFile);
            TextFileUtility.ReplaceEntireFile(tempFile, tempOutput, metadata, new[] { "NewA", "NewB" });
            
            // Assert: Output file should not have BOM
            var bytesAfter = File.ReadAllBytes(tempOutput);
            Assert.False(bytesAfter.Length >= 3 && bytesAfter[0] == 0xEF && bytesAfter[1] == 0xBB && bytesAfter[2] == 0xBF,
                "Output file should not have BOM");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }
}
