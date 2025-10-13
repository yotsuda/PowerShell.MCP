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
}
