using PowerShell.MCP.Proxy.Helpers;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

public class PipelineHelperTests
{
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
        var pipeName = "PowerShell.MCP.Communication.1234.5678";
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
        Assert.Contains("pwsh PID: 1234", result);
        Assert.Contains("Status: Busy", result);
        Assert.Contains("Get-Date", result);
        Assert.Contains("5.50s", result);
    }

    [Fact]
    public void FormatBusyStatus_EmptyStatusLine_BuildsFromParameters()
    {
        var result = PipelineHelper.FormatBusyStatus("", 1234, "Get-Date", 5.5);
        Assert.Contains("pwsh PID: 1234", result);
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

    #endregion
}