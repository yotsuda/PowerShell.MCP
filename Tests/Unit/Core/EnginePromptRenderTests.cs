using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation.Language;
using Xunit;

namespace PowerShell.MCP.Tests;

/// <summary>
/// The engine renders the prompt itself (the command echo, the post-command
/// prompt, the disconnect notice). The prompt wrapper must not take those
/// renders for the host's first prompt, which is what lets a proxy launch's
/// startup window close. Every engine render therefore raises
/// $global:McpEngineRenderingPrompt just before it and clears it in a finally,
/// and the wrappers check that flag. Checked on the embedded resource.
/// </summary>
public class EnginePromptRenderTests
{
    private const string Flag = "global:McpEngineRenderingPrompt";

    private static ScriptBlockAst EngineAst()
    {
        var script = EmbeddedResourceLoader.LoadScript("MCPPollingEngine.ps1");
        var ast = Parser.ParseInput(script, out _, out var errors);
        Assert.Empty(errors);
        return ast;
    }

    private static bool AssignsFlag(Ast node, string value) =>
        node is AssignmentStatementAst assignment &&
        assignment.Left is VariableExpressionAst variable &&
        string.Equals(variable.VariablePath.UserPath, Flag, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(assignment.Right.Extent.Text.Trim(), value, StringComparison.OrdinalIgnoreCase);

    private static StatementAst? PreviousStatement(StatementAst statement)
    {
        ReadOnlyCollection<StatementAst>? siblings = statement.Parent switch
        {
            StatementBlockAst block => block.Statements,
            NamedBlockAst named => named.Statements,
            _ => null
        };
        if (siblings == null) return null;

        var index = siblings.IndexOf(statement);
        return index > 0 ? siblings[index - 1] : null;
    }

    [Fact]
    public void EveryEnginePromptRender_IsFlagged_AndTheFlagIsClearedInAFinally()
    {
        var promptCalls = EngineAst()
            .FindAll(a => a is CommandAst c &&
                          string.Equals(c.GetCommandName(), "prompt", StringComparison.OrdinalIgnoreCase), true)
            .ToArray();

        Assert.NotEmpty(promptCalls);

        foreach (var call in promptCalls)
        {
            TryStatementAst? guard = null;
            for (var parent = call.Parent; parent != null && guard == null; parent = parent.Parent)
            {
                if (parent is TryStatementAst attempt && attempt.Finally != null &&
                    attempt.Finally.FindAll(n => AssignsFlag(n, "$false"), true).Any())
                {
                    guard = attempt;
                }
            }

            var line = call.Extent.StartLineNumber;
            Assert.True(guard != null, $"The prompt render at engine line {line} is not inside a try whose finally clears ${Flag}.");

            var previous = PreviousStatement(guard!);
            Assert.True(previous != null && AssignsFlag(previous, "$true"),
                $"The prompt render at engine line {line} is not preceded by raising ${Flag}.");
        }
    }

    [Fact]
    public void PromptWrappers_CheckTheFlag_NotTheBusyStatus()
    {
        // Checking for a busy Status missed the host's first prompt whenever a
        // command had been accepted but not started yet, which is what a stalled
        // startup leaves behind.
        var wrappers = EngineAst()
            .FindAll(a => a is FunctionDefinitionAst f &&
                          (string.Equals(f.Name, "global:prompt", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(f.Name, "prompt", StringComparison.OrdinalIgnoreCase)), true)
            .Cast<FunctionDefinitionAst>()
            .ToArray();

        // The initial wrap, and the reap loop's re-wrap after a user replaces the prompt.
        Assert.Equal(2, wrappers.Length);
        foreach (var wrapper in wrappers)
        {
            var body = wrapper.Body.Extent.Text;
            Assert.Contains("MarkFirstPromptRendered", body);
            Assert.Contains("McpEngineRenderingPrompt", body);
            Assert.DoesNotContain("Status -ne 'busy'", body);
        }
    }
}
