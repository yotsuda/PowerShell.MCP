using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// The static half of the scope warning: which names a pipeline assigns
/// locally, which with a scope prefix, and which it reads without defining.
/// The engine combines this with a live Get-Variable check (MCPPollingEngine.ps1).
/// </summary>
public class ScopeAnalyzerTests
{
    [Fact]
    public void LocalAssignments_AreReported_ScopedOnesSeparately()
    {
        var a = ScopeAnalyzer.Analyze("$x = 1; [int]$y = 2; $script:s = 3; $global:g = 4; $env:P = 'x'; $a, $b = 1, 2");
        Assert.Equal(new[] { "x", "y", "a", "b" }, a.LocalAssigned);
        Assert.Equal(new[] { "s", "g" }, a.ScopeAssigned);
        Assert.Empty(a.UnboundReads);
    }

    [Fact]
    public void ReadOfAnUnassignedName_IsReported_Once()
    {
        var a = ScopeAnalyzer.Analyze("Write-Host $r1; $r1.Count; \"value=$r1\"");
        Assert.Equal(new[] { "r1" }, a.UnboundReads);
    }

    [Theory]
    [InlineData("$x = 1; $x + 1")]                                   // assigned in the same pipeline
    [InlineData("$x; $x = 1")]                                       // …even after the read
    [InlineData("foreach ($f in Get-ChildItem) { $f.Name }")]        // loop variable
    [InlineData("for ($i = 0; $i -lt 3; $i++) { $i }")]              // for initializer
    [InlineData("& { param($p) $p } 1")]                             // parameter
    [InlineData("Get-Item x -ErrorVariable ev; $ev")]                // -ErrorVariable creates it
    [InlineData("Get-Item x -ov out; $out")]                         // alias, too
    [InlineData("New-Variable -Name nv -Value 1; $nv")]              // New-Variable
    [InlineData("Set-Variable sv 1; $sv")]                           // positional name
    [InlineData("[int]::TryParse('1', [ref]$n); $n")]                // [ref] out slot
    [InlineData("1..3 | ForEach-Object { $_ * 2 }; $PSItem; $args; $input")] // automatics
    [InlineData("$script:x = 1; $x")]                                // scoped assignment binds the bare read
    [InlineData("$LASTEXITCODE; $Matches; $foreach; $null; $true")]
    [InlineData("'$notAVariable'")]                                  // single-quoted: no variable at all
    public void DefinedOrAutomatic_IsNotReported(string pipeline)
    {
        Assert.Empty(ScopeAnalyzer.Analyze(pipeline).UnboundReads);
    }

    [Fact]
    public void CompoundAssignment_IsBothAnAssignmentAndARead()
    {
        var a = ScopeAnalyzer.Analyze("$n += 1");
        Assert.Equal(new[] { "n" }, a.LocalAssigned);
        Assert.Equal(new[] { "n" }, a.UnboundReads);
    }

    [Fact]
    public void MemberAndIndexAssignment_ReadTheVariable()
    {
        var a = ScopeAnalyzer.Analyze("$h.Key = 1; $arr[0] = 2");
        Assert.Empty(a.LocalAssigned);
        Assert.Equal(new[] { "h", "arr" }, a.UnboundReads);
    }

    [Fact]
    public void ScopedReads_AreNeverReported()
    {
        var a = ScopeAnalyzer.Analyze("$script:cache; $global:g; $env:PATH; $using:x");
        Assert.Empty(a.UnboundReads);
    }

    [Fact]
    public void AssignmentInsideAString_IsText()
    {
        var a = ScopeAnalyzer.Analyze("\"OutputEncoding=$($OutputEncoding.WebName)\"");
        Assert.Empty(a.LocalAssigned);
        Assert.Equal(new[] { "OutputEncoding" }, a.UnboundReads);
    }

    [Fact]
    public void ParseError_YieldsNothing()
    {
        var a = ScopeAnalyzer.Analyze("if ($x { ");
        Assert.Empty(a.LocalAssigned);
        Assert.Empty(a.UnboundReads);
    }

    [Fact]
    public void FormatWarning_NamesTheNames_AndTheFix()
    {
        var text = ScopeAnalyzer.FormatWarning(["r1"], ["foo", "bar"]);
        Assert.NotNull(text);
        Assert.Contains("⚠️ SCOPE: $r1 was assigned without a scope prefix in an earlier execute_command call", text);
        Assert.Contains("$script:r1 = …", text);
        Assert.Contains("⚠️ UNDEFINED: $foo, $bar are not defined in this console", text);
        Assert.Null(ScopeAnalyzer.FormatWarning([], null));
    }
}
