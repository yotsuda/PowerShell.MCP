using System;
using System.Linq;
using System.Management.Automation.Language;
using Xunit;

namespace PowerShell.MCP.Tests;

/// <summary>
/// Guards the transcription contract of the console's command echo, on the
/// copy of MCPPollingEngine.ps1 that actually ships (the embedded resource,
/// not the file on disk).
///
/// Start-Transcript taps the host UI. An echo written with [Console]::Write
/// bypasses it, so a transcript kept every command's output but never the
/// command — a log of results with no record of what produced them. And
/// transcription writes one record per call, so the echo must arrive as a
/// SINGLE call: a per-token echo lands in the transcript shredded one line
/// per token. Both properties are structural, so they are checked here
/// rather than left to the behavioural Pester cover
/// (Tests/Integration/Scenarios/CommandEcho.Transcript.Tests.ps1).
/// </summary>
public class CommandEchoTranscriptTests
{
    private const string EchoFunctionName = "Write-ColoredCommand";

    private static ScriptBlockAst EngineAst()
    {
        var script = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
        var ast = Parser.ParseInput(script, out _, out var errors);
        Assert.Empty(errors);
        return ast;
    }

    private static FunctionDefinitionAst EchoFunction()
    {
        var fn = EngineAst()
            .FindAll(a => a is FunctionDefinitionAst f &&
                          string.Equals(f.Name, EchoFunctionName, StringComparison.OrdinalIgnoreCase), true)
            .Cast<FunctionDefinitionAst>()
            .SingleOrDefault();

        Assert.NotNull(fn);
        return fn!;
    }

    private static Ast[] WriteHostCalls(Ast scope) =>
        scope.FindAll(a => a is CommandAst c &&
                           string.Equals(c.GetCommandName(), "Write-Host", StringComparison.OrdinalIgnoreCase), true)
             .ToArray();

    [Fact]
    public void Echo_NeverWritesStraightToTheConsole()
    {
        // [Console]::Write / ::WriteLine is the path that skipped the host UI
        // and kept the AI's commands out of the user's transcript.
        var consoleWrites = EchoFunction()
            .FindAll(a => a is InvokeMemberExpressionAst m &&
                          m.Expression is TypeExpressionAst t &&
                          t.TypeName.Name.EndsWith("Console", StringComparison.OrdinalIgnoreCase) &&
                          m.Member is StringConstantExpressionAst s &&
                          s.Value.StartsWith("Write", StringComparison.OrdinalIgnoreCase), true)
            .ToArray();

        Assert.Empty(consoleWrites);
    }

    [Fact]
    public void Echo_EmitsThroughTheHost_OncePerPath()
    {
        // One on the coloured path, one in the catch fallback. A third would
        // mean the echo had been split again.
        Assert.Equal(2, WriteHostCalls(EchoFunction()).Length);
    }

    [Fact]
    public void Echo_DoesNotEmitInsideTheTokenLoop()
    {
        // The token loop must accumulate, not emit: transcription records one
        // line per call, so emitting per token shreds the command in the log.
        var loops = EchoFunction()
            .FindAll(a => a is ForEachStatementAst, true)
            .ToArray();

        Assert.NotEmpty(loops);
        foreach (var loop in loops)
        {
            Assert.Empty(WriteHostCalls(loop));
        }
    }

    [Fact]
    public void Echo_RunsBeforeTheStreamCaptureIsInstalled()
    {
        // Invoke-CommandWithAllStreams swaps $Host.UI for a tee while the
        // command runs. The echo has to happen before that, or it would be
        // captured and handed back to the AI as if the command had printed it.
        // The engine calls the capture from more than one place (the AI
        // command path and the silent path), so this pins the statement block
        // the echo actually lives in rather than the script as a whole.
        var echoCall = EngineAst()
            .FindAll(a => a is CommandAst c &&
                          string.Equals(c.GetCommandName(), EchoFunctionName, StringComparison.OrdinalIgnoreCase), true)
            .Cast<CommandAst>()
            .Single();

        Ast? block = echoCall;
        while (block is not null and not StatementBlockAst)
        {
            block = block.Parent;
        }

        Assert.NotNull(block);

        var captureCalls = block!
            .FindAll(a => a is CommandAst c &&
                          string.Equals(c.GetCommandName(), "Invoke-CommandWithAllStreams", StringComparison.OrdinalIgnoreCase), true)
            .Cast<CommandAst>()
            .ToArray();

        Assert.NotEmpty(captureCalls);
        Assert.All(captureCalls, c => Assert.True(
            echoCall.Extent.StartOffset < c.Extent.StartOffset,
            $"{EchoFunctionName} must be called before Invoke-CommandWithAllStreams installs its $Host.UI tee."));
    }
}
