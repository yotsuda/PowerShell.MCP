using System;
using System.IO;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class RemoveLinesFromFileCmdletTests : IDisposable
{
    private readonly string _testFile;

    public RemoveLinesFromFileCmdletTests()
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
        var cmdlet = new RemoveLinesFromFileCmdlet();
        Assert.NotNull(cmdlet);
        Assert.IsAssignableFrom<TextFileCmdletBase>(cmdlet);
    }

    [Fact]
    public void Path_SetValue_StoresCorrectly()
    {
        var cmdlet = new RemoveLinesFromFileCmdlet();
        cmdlet.Path = new[] { "test.txt" };
        Assert.NotNull(cmdlet.Path);
    }

    [Fact]
    public void LineRange_SetValue_StoresCorrectly()
    {
        var cmdlet = new RemoveLinesFromFileCmdlet();
        cmdlet.LineRange = new[] { 2, 3 };
        Assert.NotNull(cmdlet.LineRange);
    }

    [Fact]
    public void LineRange_NegativeValue_StoresCorrectly()
    {
        var cmdlet = new RemoveLinesFromFileCmdlet();
        cmdlet.LineRange = new[] { -3 };
        Assert.NotNull(cmdlet.LineRange);
        Assert.Single(cmdlet.LineRange);
        Assert.Equal(-3, cmdlet.LineRange[0]);
    }

    [Fact]
    public void LineRange_NegativeValue_RepresentsTailRemoval()
    {
        // Negative LineRange indicates tail removal mode
        var cmdlet = new RemoveLinesFromFileCmdlet();
        cmdlet.LineRange = new[] { -5 };

        // Verify the value is stored as negative
        Assert.True(cmdlet.LineRange[0] < 0);
        // The absolute value represents the number of lines to remove from tail
        Assert.Equal(5, -cmdlet.LineRange[0]);
    }
}
