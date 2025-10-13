using System;
using System.IO;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class ShowTextFileCmdletTests : IDisposable
{
    private readonly string _testFile;

    public ShowTextFileCmdletTests()
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
        var cmdlet = new ShowTextFileCmdlet();
        Assert.NotNull(cmdlet);
        Assert.IsAssignableFrom<TextFileCmdletBase>(cmdlet);
    }

    [Fact]
    public void Path_SetValue_StoresCorrectly()
    {
        var cmdlet = new ShowTextFileCmdlet();
        cmdlet.Path = new[] { "test.txt" };
        Assert.NotNull(cmdlet.Path);
        Assert.Single(cmdlet.Path);
    }

    [Fact]
    public void LineRange_SetValue_StoresCorrectly()
    {
        var cmdlet = new ShowTextFileCmdlet();
        cmdlet.LineRange = new[] { 1, 5 };
        Assert.NotNull(cmdlet.LineRange);
        Assert.Equal(2, cmdlet.LineRange.Length);
    }

    [Theory]
    [InlineData("UTF8")]
    [InlineData("Shift_JIS")]
    public void Encoding_ValidEncodings_StoresCorrectly(string encoding)
    {
        var cmdlet = new ShowTextFileCmdlet();
        cmdlet.Encoding = encoding;
        Assert.Equal(encoding, cmdlet.Encoding);
    }
}
