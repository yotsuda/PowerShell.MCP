using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

public class GetSetContentWarningTests
{
    [Theory]
    [InlineData("Get-Content a.txt")]
    [InlineData("gc a.txt | Select-Object -First 3")]
    [InlineData("cat a.txt")]
    [InlineData("type a.txt")]
    [InlineData("Microsoft.PowerShell.Management\\Get-Content a.txt")]
    [InlineData("if ($x) { Get-Content a.txt }")]
    public void GetContentInvocation_Warns(string pipeline)
    {
        var warning = NamedPipeServer.BuildGetSetContentWarning(pipeline);
        Assert.NotNull(warning);
        Assert.Contains("Show-TextFiles", warning);
    }

    [Fact]
    public void SetContentInvocation_Warns()
    {
        Assert.Contains("instead of Set-Content", NamedPipeServer.BuildGetSetContentWarning("Set-Content a.txt 'x'"));
    }

    [Theory]
    [InlineData("[pscustomobject]@{ Type = 1 }")]
    [InlineData("Add-Type -AssemblyName System.Web")]
    [InlineData("'the cat sat' | Write-Output")]
    [InlineData("$x.GetType().Name")]
    [InlineData("Get-Help Get-Content")]
    [InlineData("Show-TextFiles a.txt")]
    public void NoInvocation_NoWarning(string pipeline)
    {
        Assert.Null(NamedPipeServer.BuildGetSetContentWarning(pipeline));
    }
}
