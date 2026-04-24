using PowerShell.MCP.Proxy.Helpers;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

public class PipelineHelperTests
{
    public PipelineHelperTests()
    {
        // Reset static state before each test
        PipelineHelper.ResetScopeWarningState();
    }

    #region Truncate Tests

    [Fact]
    public void Truncate_NullPipeline_ReturnsEmptyString()
    {
        Assert.Equal("", PipelineHelper.Truncate(null));
    }

    [Fact]
    public void Truncate_EmptyPipeline_ReturnsEmptyString()
    {
        Assert.Equal("", PipelineHelper.Truncate(""));
    }

    [Fact]
    public void Truncate_ShortPipeline_ReturnsUnchanged()
    {
        Assert.Equal("Get-Date", PipelineHelper.Truncate("Get-Date"));
    }

    [Fact]
    public void Truncate_ExactLength_ReturnsUnchanged()
    {
        var pipeline = new string('x', 30);
        Assert.Equal(pipeline, PipelineHelper.Truncate(pipeline, 30));
    }

    [Fact]
    public void Truncate_LongPipeline_TruncatesWithEllipsis()
    {
        var pipeline = "Get-ChildItem -Path C:\\Users\\Documents -Recurse -Filter *.txt";
        var result = PipelineHelper.Truncate(pipeline, 30);
        Assert.Equal(30, result.Length);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void Truncate_NormalizesWhitespace()
    {
        var pipeline = "Get-Date   \t  \n  -Format  yyyy";
        var result = PipelineHelper.Truncate(pipeline, 100);
        Assert.Equal("Get-Date -Format yyyy", result);
    }

    [Fact]
    public void Truncate_CustomMaxLength_Works()
    {
        var result = PipelineHelper.Truncate("Get-ChildItem", 10);
        Assert.Equal(10, result.Length);
        Assert.EndsWith("...", result);
    }

    #endregion

    #region GetPidString Tests

    [Fact]
    public void GetPidString_NullPipeName_ReturnsUnknown()
    {
        Assert.Equal("unknown", PipelineHelper.GetPidString(null));
    }

    [Fact]
    public void GetPidString_ValidPipeName_ReturnsPid()
    {
        var pipeName = "PSMCP.1234.5678";
        var result = PipelineHelper.GetPidString(pipeName);
        Assert.Equal("5678", result);
    }

    [Fact]
    public void GetPidString_InvalidFormat_ReturnsUnknown()
    {
        var pipeName = "InvalidPipeName";
        var result = PipelineHelper.GetPidString(pipeName);
        Assert.Equal("unknown", result);
    }

    #endregion

    #region CheckLocalVariableAssignments Tests

    [Fact]
    public void CheckLocalVariableAssignments_NoAssignments_ReturnsNull()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("Get-Date");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_ScriptScope_ReturnsNull()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$script:foo = 123");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_GlobalScope_ReturnsNull()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$global:bar = 456");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_EnvVariable_ReturnsNull()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$env:PATH = '/usr/bin'");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_NullTrueFalse_ReturnsNull()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$null = $true = $false");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_AutomaticVariables_ReturnsNull()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$_ = 1; $Matches = @{}");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_LocalVariable_ReturnsWarning()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$foo = 123");
        Assert.NotNull(result);
        Assert.Contains("SCOPE WARNING", result);
        Assert.Contains("$foo", result);
        Assert.Contains("$script:foo", result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_MultipleVariables_ReturnsAllWarnings()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$foo = 1; $bar = 2; $baz = 3");
        Assert.NotNull(result);
        Assert.Contains("$foo", result);
        Assert.Contains("$bar", result);
        Assert.Contains("$baz", result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_DuplicateVariables_ListsOnce()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$foo = 1; $foo = 2");
        Assert.NotNull(result);
        // Count occurrences of "$foo →"
        var count = result.Split("$foo →").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void CheckLocalVariableAssignments_FirstCall_ReturnsDetailedWarning()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("$foo = 123");
        Assert.NotNull(result);
        Assert.Contains("SCOPE WARNING", result);
        Assert.Contains("Consider using", result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_SecondCall_ReturnsCompactWarning()
    {
        // First call — detailed
        PipelineHelper.CheckLocalVariableAssignments("$foo = 1");
        // Second call — compact
        var result = PipelineHelper.CheckLocalVariableAssignments("$bar = 2");
        Assert.NotNull(result);
        Assert.StartsWith("⚠️ SCOPE:", result);
        Assert.Contains("$bar", result);
        Assert.DoesNotContain("Consider using", result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_ForLoopInitializer_ReturnsNull()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("for ($i = 0; $i -lt 10; $i++) { Write-Host $i }");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_ForLoopInitializer_MixedWithOtherVars_WarnsOnlyNonLoop()
    {
        var result = PipelineHelper.CheckLocalVariableAssignments("for ($i = 0; $i -lt 10; $i++) { $result = $i * 2 }");
        Assert.NotNull(result);
        Assert.Contains("$result", result);
        Assert.DoesNotContain("$i →", result);
    }

    // --- per-agent / per-variable dedup behaviour ---

    [Fact]
    public void CheckLocalVariableAssignments_RepeatedSameVariable_SecondCallIsSilent()
    {
        // First pipeline with $foo → detail warning for agent A.
        var first = PipelineHelper.CheckLocalVariableAssignments("$foo = 1", "agent-A");
        Assert.NotNull(first);

        // Same agent re-assigns $foo. The AI already learned the lesson
        // for $foo; repeating the warning is noise, so return null.
        var second = PipelineHelper.CheckLocalVariableAssignments("$foo = 2", "agent-A");
        Assert.Null(second);
    }

    [Fact]
    public void CheckLocalVariableAssignments_NewVariableAfterSeen_ReturnsCompactForNewOnly()
    {
        // Prime: $foo becomes "already-warned" for agent A via detail.
        PipelineHelper.CheckLocalVariableAssignments("$foo = 1", "agent-A");

        // Pipeline introduces $foo (already warned) + $bar (new). Only
        // $bar should surface, and it should use the compact form —
        // the detail was already consumed by the priming call.
        var result = PipelineHelper.CheckLocalVariableAssignments("$foo = 10; $bar = 20", "agent-A");
        Assert.NotNull(result);
        Assert.StartsWith("⚠️ SCOPE:", result);
        Assert.Contains("$bar", result);
        Assert.DoesNotContain("$foo", result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_AllVariablesAlreadySeen_ReturnsNull()
    {
        PipelineHelper.CheckLocalVariableAssignments("$a = 1; $b = 2", "agent-A");
        // Same agent, every name already warned → silent.
        var result = PipelineHelper.CheckLocalVariableAssignments("$a = 3; $b = 4", "agent-A");
        Assert.Null(result);
    }

    [Fact]
    public void CheckLocalVariableAssignments_DifferentAgents_HaveIndependentState()
    {
        // Agent A learns $foo with detail.
        var aFirst = PipelineHelper.CheckLocalVariableAssignments("$foo = 1", "agent-A");
        Assert.NotNull(aFirst);
        Assert.Contains("Consider using", aFirst);

        // Agent B is fresh — the same $foo assignment should also
        // produce a detail warning, not silence.
        var bFirst = PipelineHelper.CheckLocalVariableAssignments("$foo = 1", "agent-B");
        Assert.NotNull(bFirst);
        Assert.Contains("Consider using", bFirst);
    }

    #endregion

    #region FormatBusyStatus Tests

    [Fact]
    public void FormatBusyStatus_WithStatusLine_ReturnsStatusLine()
    {
        var statusLine = "⧗ Custom status line";
        var result = PipelineHelper.FormatBusyStatus(statusLine, 1234, "Get-Date", 5.5);
        Assert.Equal(statusLine, result);
    }

    [Fact]
    public void FormatBusyStatus_NullStatusLine_BuildsFromParameters()
    {
        var result = PipelineHelper.FormatBusyStatus(null, 1234, "Get-Date", 5.5);
        Assert.Contains("PID #1234", result);
        Assert.Contains("Status: Busy", result);
        Assert.Contains("Get-Date", result);
        Assert.Contains("5.50s", result);
    }

    [Fact]
    public void FormatBusyStatus_EmptyStatusLine_BuildsFromParameters()
    {
        var result = PipelineHelper.FormatBusyStatus("", 1234, "Get-Date", 5.5);
        Assert.Contains("PID #1234", result);
    }

    [Fact]
    public void FormatBusyStatus_LongPipeline_Truncates()
    {
        var longPipeline = "Get-ChildItem -Path C:\\Users\\Documents -Recurse -Filter *.txt -Force";
        var result = PipelineHelper.FormatBusyStatus(null, 1234, longPipeline, 5.5);
        Assert.Contains("...", result);
    }

    #endregion

    #region CheckVar1Enforcement Tests

    [Theory]
    [InlineData("Get-Help Update-LinesInFile")]
    [InlineData("Get-Help Add-LinesToFile -Examples")]
    [InlineData("Get-Help Update-MatchInFile -Full")]
    [InlineData("Get-Help Remove-LinesFromFile")]
    [InlineData("Get-Help Set-Content -Detailed")]
    [InlineData("Get-Help Add-Content")]
    [InlineData("Get-Command Update-LinesInFile")]
    [InlineData("Get-Command Add-LinesToFile")]
    public void CheckVar1Enforcement_GetHelpOrGetCommand_ReturnsNull(string pipeline)
    {
        var result = PipelineHelper.CheckVar1Enforcement(pipeline, null, null);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Add-LinesToFile -Path test.txt -Content $var1")]
    [InlineData("Update-LinesInFile -Path test.txt -LineRange 1 -Content $var1")]
    public void CheckVar1Enforcement_WithVar1_ReturnsNull(string pipeline)
    {
        var result = PipelineHelper.CheckVar1Enforcement(pipeline, "some content", null);
        Assert.Null(result);
    }

    [Fact]
    public void CheckVar1Enforcement_UpdateMatchInFile_WithBothVars_ReturnsNull()
    {
        var result = PipelineHelper.CheckVar1Enforcement(
            "Update-MatchInFile -Path test.txt -OldText $var1 -Replacement $var2", "old", "new");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Add-LinesToFile -Path test.txt -Content 'hello'")]
    [InlineData("Update-LinesInFile -Path test.txt -LineRange 1 -Content 'hello'")]
    public void CheckVar1Enforcement_WithoutVar1_ReturnsError(string pipeline)
    {
        var result = PipelineHelper.CheckVar1Enforcement(pipeline, null, null);
        Assert.NotNull(result);
        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void CheckVar1Enforcement_UpdateMatchInFile_WithoutVar2_ReturnsError()
    {
        var result = PipelineHelper.CheckVar1Enforcement(
            "Update-MatchInFile -Path test.txt -OldText $var1 -Replacement 'new'", "old", null);
        Assert.NotNull(result);
        Assert.Contains("var2", result);
    }

    [Theory]
    [InlineData(@"Show-TextFiles ""C:\PlatyPS\en-US\Add-LinesToFile.md""")]
    [InlineData(@"Show-TextFiles ""C:\PlatyPS\en-US\Update-LinesInFile.md"" -LineRange 1,55")]
    [InlineData(@"Show-TextFiles ""C:\PlatyPS\en-US\Update-MatchInFile.md""")]
    [InlineData(@"Show-TextFiles ""C:\PlatyPS\en-US\Remove-LinesFromFile.md""")]
    [InlineData(@"Get-Content ""C:\path\Set-Content.md""")]
    [InlineData(@"Get-Content ""C:\path\Add-Content.md""")]
    public void CheckVar1Enforcement_CmdletNameInPath_ReturnsNull(string pipeline)
    {
        var result = PipelineHelper.CheckVar1Enforcement(pipeline, null, null);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(@"Show-TextFiles C:\path\Add-LinesToFile.md -LineRange 1,55")]
    [InlineData(@"Show-TextFiles C:\path\Update-MatchInFile.md")]
    public void CheckVar1Enforcement_CmdletNameInUnquotedPath_ReturnsNull(string pipeline)
    {
        var result = PipelineHelper.CheckVar1Enforcement(pipeline, null, null);
        Assert.Null(result);
    }

    #endregion

    #region CheckMarkdownFileHint Tests

    [Fact]
    public void CheckMarkdownFileHint_OutputWithMdFile_ReturnsHint()
    {
        PipelineHelper.ResetMarkdownHintState();
        var result = PipelineHelper.CheckMarkdownFileHint("README.md", "default");
        if (OperatingSystem.IsWindows())
            Assert.NotNull(result);
        else
            Assert.Null(result); // Non-Windows returns null after setting flag
    }

    [Fact]
    public void CheckMarkdownFileHint_NoMdFile_ReturnsNull()
    {
        PipelineHelper.ResetMarkdownHintState();
        var result = PipelineHelper.CheckMarkdownFileHint("Get-Date", "default");
        Assert.Null(result);
    }

    [Fact]
    public void CheckMarkdownFileHint_ShownOnlyOncePerAgent()
    {
        PipelineHelper.ResetMarkdownHintState();
        var first = PipelineHelper.CheckMarkdownFileHint("README.md", "default");
        var second = PipelineHelper.CheckMarkdownFileHint("CHANGELOG.md", "default");
        Assert.Null(second);
    }

    [Fact]
    public void CheckMarkdownFileHint_DifferentAgents_EachGetsHint()
    {
        PipelineHelper.ResetMarkdownHintState();
        var agent1 = PipelineHelper.CheckMarkdownFileHint("README.md", "default");
        var agent2 = PipelineHelper.CheckMarkdownFileHint("README.md", "sub-agent-1");
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(agent1);
            Assert.NotNull(agent2);
        }
    }

    [Fact]
    public void CheckMarkdownFileHint_InputMatchFirst_OutputSkipped()
    {
        // Simulates the ?? chain: pipeline match first, output not checked
        PipelineHelper.ResetMarkdownHintState();
        var fromInput = PipelineHelper.CheckMarkdownFileHint("cat README.md", "default");
        var fromOutput = PipelineHelper.CheckMarkdownFileHint("some output with NOTES.md", "default");
        // Second call returns null because hint was already shown
        Assert.Null(fromOutput);
    }

    [Fact]
    public void CheckMarkdownFileHint_InputNoMatch_OutputMatch()
    {
        // Simulates the ?? chain: pipeline has no .md, output does
        PipelineHelper.ResetMarkdownHintState();
        var fromInput = PipelineHelper.CheckMarkdownFileHint("Get-ChildItem", "default");
        Assert.Null(fromInput);
        // Flag not set, so output check works
        var fromOutput = PipelineHelper.CheckMarkdownFileHint("README.md  CHANGELOG.md", "default");
        if (OperatingSystem.IsWindows())
            Assert.NotNull(fromOutput);
    }

    #endregion

    // TODO: Uncomment when JsonDuo is published to PS Gallery
    // #region CheckJsonFileHint Tests
    //
    // [Fact]
    // public void CheckJsonFileHint_OutputWithJsonFile_ReturnsHintOrNullDependingOnInstallation()
    // {
    //     PipelineHelper.ResetJsonHintState();
    //     var result = PipelineHelper.CheckJsonFileHint("config.json", "default");
    //     if (!OperatingSystem.IsWindows())
    //     {
    //         Assert.Null(result);
    //     }
    //     else if (result != null)
    //     {
    //         Assert.Contains("JsonDuo", result);
    //     }
    //     // result == null is valid on Windows when JsonDuo is not installed
    // }
    //
    // [Fact]
    // public void CheckJsonFileHint_NoJsonFile_ReturnsNull()
    // {
    //     PipelineHelper.ResetJsonHintState();
    //     var result = PipelineHelper.CheckJsonFileHint("Get-Date", "default");
    //     Assert.Null(result);
    // }
    //
    // [Fact]
    // public void CheckJsonFileHint_ShownOnlyOncePerAgent()
    // {
    //     PipelineHelper.ResetJsonHintState();
    //     var first = PipelineHelper.CheckJsonFileHint("config.json", "default");
    //     var second = PipelineHelper.CheckJsonFileHint("data.json", "default");
    //     Assert.Null(second);
    // }
    //
    // [Fact]
    // public void CheckJsonFileHint_DifferentAgents_EachGetsHint()
    // {
    //     PipelineHelper.ResetJsonHintState();
    //     var agent1 = PipelineHelper.CheckJsonFileHint("config.json", "default");
    //     var agent2 = PipelineHelper.CheckJsonFileHint("config.json", "sub-agent-1");
    //     // Both should get a result (or both null if not Windows/not installed), but not one null and one not
    //     Assert.Equal(agent1 is not null, agent2 is not null);
    // }
    //
    // [Fact]
    // public void CheckJsonFileHint_InputMatchFirst_OutputSkipped()
    // {
    //     PipelineHelper.ResetJsonHintState();
    //     var fromInput = PipelineHelper.CheckJsonFileHint("cat config.json", "default");
    //     var fromOutput = PipelineHelper.CheckJsonFileHint("some output with data.json", "default");
    //     Assert.Null(fromOutput);
    // }
    //
    // #endregion
}