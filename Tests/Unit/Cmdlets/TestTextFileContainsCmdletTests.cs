using System;
using System.IO;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class TestTextFileContainsCmdletTests : IDisposable
{
    private readonly string _testFile;

    public TestTextFileContainsCmdletTests()
    {
        _testFile = Path.GetTempFileName();
        File.WriteAllLines(_testFile, new[] { "Line 1", "Test Line", "Line 3" });
    }

    public void Dispose()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        var cmdlet = new TestTextFileContainsCmdlet();
        Assert.NotNull(cmdlet);
        Assert.IsAssignableFrom<TextFileCmdletBase>(cmdlet);
    }

    [Fact]
    public void Path_SetValue_StoresCorrectly()
    {
        var cmdlet = new TestTextFileContainsCmdlet();
        cmdlet.Path = new[] { "test.txt" };
        Assert.NotNull(cmdlet.Path);
    }

    [Fact]
    public void Contains_SetValue_StoresCorrectly()
    {
        var cmdlet = new TestTextFileContainsCmdlet();
        cmdlet.Contains = "search";
        Assert.Equal("search", cmdlet.Contains);
    }

    [Fact]
    public void Pattern_SetValue_StoresCorrectly()
    {
        var cmdlet = new TestTextFileContainsCmdlet();
        cmdlet.Pattern = @"^\d+";
        Assert.Equal(@"^\d+", cmdlet.Pattern);
    }
}
