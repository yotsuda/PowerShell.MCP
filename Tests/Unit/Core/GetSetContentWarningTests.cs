using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

public class GetSetContentWarningTests
{
    [Theory]
    [InlineData("Get-Content a.txt")]
    [InlineData("gc a.txt | Select-Object -First 3")]
    [InlineData("gc a.txt | Select-String error")]
    [InlineData("Get-Content a.txt | Out-String")]
    [InlineData("Get-Content a.txt -Tail 20 | Where-Object { $_ -match 'x' } | Format-Table")]
    [InlineData("cat a.txt")]
    [InlineData("type a.txt")]
    [InlineData("Microsoft.PowerShell.Management\\Get-Content a.txt")]
    [InlineData("if ($x) { Get-Content a.txt }")]
    [InlineData("foreach ($f in $files) { Get-Content $f }")]
    public void GetContentInvocation_Warns(string pipeline)
    {
        var warning = NamedPipeServer.BuildGetSetContentWarning(pipeline);
        Assert.NotNull(warning);
        Assert.Contains("Show-TextFiles", warning);
    }

    // The lines are consumed as data, not viewed: Show-TextFiles' numbered
    // listing is the wrong tool there, so the hint stays quiet.
    [Theory]
    [InlineData("$text = Get-Content a.txt -Raw")]
    [InlineData("$lines = gc a.txt")]
    [InlineData("(Get-Content a.txt) -replace 'x','y'")]
    [InlineData("$(cat a.txt).Count")]
    [InlineData("@(Get-Content a.txt).Length")]
    [InlineData("Get-Content a.json | ConvertFrom-Json")]
    [InlineData("Get-Content a.txt | ForEach-Object { $_.Trim() }")]
    [InlineData("Get-Content a.txt | Measure-Object -Line")]
    [InlineData("Get-Content a.txt | Where-Object { $_ } | Set-Content b.txt")]
    [InlineData("foreach ($l in Get-Content a.txt) { $l }")]
    [InlineData("if (Get-Content a.txt) { 'non-empty' }")]
    [InlineData("Write-Output (Get-Content a.txt)")]
    public void GetContentConsumedInPipeline_NoGetContentHint(string pipeline)
    {
        var warning = NamedPipeServer.BuildGetSetContentWarning(pipeline);
        if (warning != null)
            Assert.DoesNotContain("Show-TextFiles", warning);
    }

    [Fact]
    public void SetContentInvocation_Warns()
    {
        Assert.Contains("instead of Set-Content", NamedPipeServer.BuildGetSetContentWarning("Set-Content a.txt 'x'"));
    }

    [Fact]
    public void SetContentFedByGetContent_WarnsForSetContentOnly()
    {
        var warning = NamedPipeServer.BuildGetSetContentWarning("Get-Content a.txt | Set-Content b.txt");
        Assert.NotNull(warning);
        Assert.Contains("instead of Set-Content", warning);
        Assert.DoesNotContain("Show-TextFiles", warning);
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
