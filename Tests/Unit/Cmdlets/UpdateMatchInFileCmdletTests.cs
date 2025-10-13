using System;
using System.IO;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class UpdateMatchInFileCmdletTests : IDisposable
{
    private readonly string _testFile;

    public UpdateMatchInFileCmdletTests()
    {
        _testFile = Path.GetTempFileName();
        File.WriteAllLines(_testFile, new[] { "Hello World", "Test Line" });
    }

    public void Dispose()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        var cmdlet = new UpdateMatchInFileCmdlet();
        Assert.NotNull(cmdlet);
        Assert.IsAssignableFrom<TextFileCmdletBase>(cmdlet);
    }

    [Fact]
    public void Path_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateMatchInFileCmdlet();
        cmdlet.Path = new[] { "test.txt" };
        Assert.NotNull(cmdlet.Path);
    }

    [Fact]
    public void Pattern_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateMatchInFileCmdlet();
        cmdlet.Pattern = @"\d+";
        Assert.Equal(@"\d+", cmdlet.Pattern);
    }

    [Fact]
    public void Replacement_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateMatchInFileCmdlet();
        cmdlet.Replacement = "replaced";
        Assert.Equal("replaced", cmdlet.Replacement);
    }
}
