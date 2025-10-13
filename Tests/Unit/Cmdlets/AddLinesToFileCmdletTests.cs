using System;
using System.IO;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class AddLinesToFileCmdletTests : IDisposable
{
    private readonly string _testFile;

    public AddLinesToFileCmdletTests()
    {
        _testFile = Path.GetTempFileName();
        File.WriteAllLines(_testFile, new[] { "Existing line" });
    }

    public void Dispose()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        var cmdlet = new AddLinesToFileCmdlet();
        Assert.NotNull(cmdlet);
        Assert.IsAssignableFrom<TextFileCmdletBase>(cmdlet);
    }

    [Fact]
    public void Path_SetValue_StoresCorrectly()
    {
        var cmdlet = new AddLinesToFileCmdlet();
        cmdlet.Path = new[] { "test.txt" };
        Assert.NotNull(cmdlet.Path);
        Assert.Single(cmdlet.Path);
    }

    [Fact]
    public void Content_SetValue_StoresCorrectly()
    {
        var cmdlet = new AddLinesToFileCmdlet();
        cmdlet.Content = new object[] { "Line 1", "Line 2" };
        Assert.NotNull(cmdlet.Content);
        Assert.Equal(2, cmdlet.Content.Length);
    }

    [Fact]
    public void LineNumber_SetValue_StoresCorrectly()
    {
        var cmdlet = new AddLinesToFileCmdlet();
        cmdlet.LineNumber = 5;
        Assert.Equal(5, cmdlet.LineNumber);
    }

    [Fact]
    public void Backup_DefaultValue_IsFalse()
    {
        var cmdlet = new AddLinesToFileCmdlet();
        Assert.False(cmdlet.Backup);
    }
}
