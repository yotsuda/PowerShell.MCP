using System.Management.Automation.Language;

namespace PowerShell.MCP.Services;

/// <summary>
/// Static analysis of an AI pipeline's variable use, for the scope warning.
///
/// An AI command runs in a child scope (see Invoke-Captured in
/// MCPPollingEngine.ps1), so a plain <c>$x = …</c> is discarded when the call
/// ends, and only <c>$script:x</c> / <c>$global:x</c> survive to the next
/// call. The proxy used to warn at every new local assignment, by regex; that
/// fired on the overwhelmingly common harmless case (a temporary used within
/// one call, a loop variable, <c>$x=</c> inside a string) and said nothing
/// at the moment the mistake actually bit — the later call that read the
/// variable and got <c>$null</c>.
///
/// This analyzer works on the real AST and reports only what the engine
/// needs to warn precisely: which names the pipeline assigns locally (the
/// engine remembers them per console), which it assigns with a scope prefix
/// (the engine forgets those), and which names it READS without defining
/// them anywhere in the same pipeline. The engine then checks each such read
/// against the live session (<c>Get-Variable</c>) and warns only for a name
/// that does not exist — "assigned without $script: in an earlier call" when
/// it remembers the name, "not defined in this console" otherwise.
/// </summary>
public static class ScopeAnalyzer
{
    public sealed record Analysis(
        IReadOnlyList<string> LocalAssigned,
        IReadOnlyList<string> ScopeAssigned,
        IReadOnlyList<string> UnboundReads)
    {
        public static readonly Analysis Empty = new([], [], []);
    }

    // Names that are valid reads without ever existing as a session variable
    // at the moment the engine checks: pipeline / loop / event / scriptblock
    // automatics, and the constants.
    private static readonly HashSet<string> AutomaticNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_", "PSItem", "this", "input", "args", "foreach", "switch", "Matches",
        "LASTEXITCODE", "null", "true", "false", "?", "^", "$",
        "Error", "Event", "EventArgs", "EventSubscriber", "Sender", "SourceEventArgs", "SourceArgs",
        "PSCmdlet", "PSBoundParameters", "MyInvocation", "PSScriptRoot", "PSCommandPath",
        "StackTrace", "PSSenderInfo", "PSDebugContext",
        // Preference variables that do not exist until someone sets them;
        // reading one is how they are consulted.
        "OFS", "PSModuleAutoLoadingPreference",
    };

    // Common parameters whose argument NAMES a variable the command creates.
    private static readonly HashSet<string> VariableCreatingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "ov", "OutVariable", "ev", "ErrorVariable", "wv", "WarningVariable",
        "iv", "InformationVariable", "pv", "PipelineVariable",
        // Cmdlet-specific ones: Tee-Object -Variable, Import-LocalizedData
        // -BindingVariable, Invoke-WebRequest / Invoke-RestMethod
        // -SessionVariable (-SV).
        "Variable", "BindingVariable", "SessionVariable", "sv",
    };

    private static readonly HashSet<string> VariableCreatingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "New-Variable", "nv", "Set-Variable", "sv",
    };

    public static Analysis Analyze(string pipeline)
    {
        if (string.IsNullOrWhiteSpace(pipeline)) return Analysis.Empty;

        ScriptBlockAst ast;
        try
        {
            ast = Parser.ParseInput(pipeline, out _, out var errors);
            if (errors is { Length: > 0 }) return Analysis.Empty;
        }
        catch
        {
            return Analysis.Empty;
        }

        var localAssigned = new List<string>();
        var scopeAssigned = new List<string>();
        // Names the pipeline itself gives a value, anywhere in it: a read of one
        // of these is never stale, whatever the statement order.
        var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Variable nodes that are assignment targets, not reads.
        var targets = new HashSet<Ast>();

        void AddDistinct(List<string> list, string name)
        {
            if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
        }

        foreach (var node in ast.FindAll(a => a is AssignmentStatementAst, true))
        {
            var assignment = (AssignmentStatementAst)node;
            var compound = assignment.Operator != TokenKind.Equals;
            foreach (var variable in AssignmentTargets(assignment.Left))
            {
                targets.Add(variable);
                var path = variable.VariablePath;
                if (path.IsUnqualified)
                {
                    AddDistinct(localAssigned, path.UserPath);
                    // `$x += 1` writes $x but reads it first: it is a local
                    // assignment to remember, and a read to check.
                    if (!compound) bound.Add(path.UserPath);
                    else targets.Remove(variable);
                }
                else if (path.IsScript || path.IsGlobal)
                {
                    // UserPath is "script:x" — keep the bare name.
                    var bare = path.UserPath[(path.UserPath.IndexOf(':') + 1)..];
                    AddDistinct(scopeAssigned, bare);
                    bound.Add(bare);
                }
            }
        }

        foreach (var node in ast.FindAll(a => a is ParameterAst, true))
            bound.Add(((ParameterAst)node).Name.VariablePath.UserPath);

        foreach (var node in ast.FindAll(a => a is ForEachStatementAst, true))
        {
            var variable = ((ForEachStatementAst)node).Variable;
            targets.Add(variable);
            bound.Add(variable.VariablePath.UserPath);
        }

        // [ref]$x is NOT a definition: PowerShell refuses "[ref] cannot be
        // applied to a variable that does not exist", so an unbound $x there
        // is exactly a read worth reporting (the usual idiom assigns $x = $null
        // first, which binds it).

        // A dot-sourced file or an Invoke-Expression string can define any
        // name, so what the pipeline then reads cannot be judged from its text.
        var definesUnknownNames = false;

        foreach (var node in ast.FindAll(a => a is CommandAst, true))
        {
            var command = (CommandAst)node;
            var elements = command.CommandElements;
            if (command.InvocationOperator == TokenKind.Dot) definesUnknownNames = true;
            for (var i = 0; i < elements.Count; i++)
            {
                if (elements[i] is not CommandParameterAst parameter) continue;
                if (!VariableCreatingParameters.Contains(parameter.ParameterName)) continue;
                var argument = parameter.Argument ?? (i + 1 < elements.Count ? elements[i + 1] : null);
                if (argument is StringConstantExpressionAst constant)
                    bound.Add(constant.Value.TrimStart('+'));
            }

            var name = command.GetCommandName();
            if (name == null) continue;
            name = name[(name.LastIndexOf('\\') + 1)..];
            if (name.Equals("Invoke-Expression", StringComparison.OrdinalIgnoreCase) || name.Equals("iex", StringComparison.OrdinalIgnoreCase))
                definesUnknownNames = true;
            if (!VariableCreatingCommands.Contains(name)) continue;
            // -Name x, -Name:x, or the first positional argument.
            string? created = null;
            for (var i = 1; i < elements.Count && created == null; i++)
            {
                if (elements[i] is CommandParameterAst p)
                {
                    // -Name and its unambiguous prefixes (-N, -Na, -Nam).
                    if ("Name".StartsWith(p.ParameterName, StringComparison.OrdinalIgnoreCase))
                    {
                        var argument = p.Argument ?? (i + 1 < elements.Count ? elements[i + 1] : null);
                        if (argument is StringConstantExpressionAst constant) created = constant.Value;
                    }
                    else if (p.Argument == null) i++; // skip this parameter's value
                }
                else if (elements[i] is StringConstantExpressionAst positional)
                {
                    created = positional.Value;
                }
            }
            if (created != null) bound.Add(created);
        }

        var unboundReads = new List<string>();
        if (definesUnknownNames) return new Analysis(localAssigned, scopeAssigned, unboundReads);
        foreach (var node in ast.FindAll(a => a is VariableExpressionAst, true))
        {
            var variable = (VariableExpressionAst)node;
            if (targets.Contains(variable)) continue;
            // $using:x — the parser wraps the bare variable in a
            // UsingExpressionAst; it is read in another runspace, not here.
            if (variable.Parent is UsingExpressionAst) continue;
            var path = variable.VariablePath;
            if (!path.IsUnqualified) continue;
            var name = path.UserPath;
            if (AutomaticNames.Contains(name) || bound.Contains(name)) continue;
            AddDistinct(unboundReads, name);
        }

        return new Analysis(localAssigned, scopeAssigned, unboundReads);
    }

    private static IEnumerable<VariableExpressionAst> AssignmentTargets(ExpressionAst left)
    {
        switch (left)
        {
            case VariableExpressionAst variable:
                yield return variable;
                break;
            case AttributedExpressionAst attributed: // [int]$x = … / [ValidateRange()]$x = …
                foreach (var v in AssignmentTargets(attributed.Child)) yield return v;
                break;
            case ArrayLiteralAst array: // $a, $b = 1, 2
                foreach (var element in array.Elements)
                    foreach (var v in AssignmentTargets(element)) yield return v;
                break;
            case ParenExpressionAst paren when paren.Pipeline is PipelineAst p
                                               && p.PipelineElements.Count == 1
                                               && p.PipelineElements[0] is CommandExpressionAst ce:
                foreach (var v in AssignmentTargets(ce.Expression)) yield return v;
                break;
            // $x.Prop = …, $a[0] = … : $x / $a must already exist — those are reads.
        }
    }

    /// <summary>
    /// The warning text for the engine to append to a command's result, or
    /// null when there is nothing to say. <paramref name="stale"/> are reads
    /// of names an earlier call assigned locally in this console;
    /// <paramref name="undefined"/> are reads of names this console has never
    /// seen defined. Both lists hold only names that did not exist at the time
    /// the command ran.
    /// </summary>
    public static string? FormatWarning(IReadOnlyList<string>? stale, IReadOnlyList<string>? undefined)
    {
        var lines = new List<string>();

        if (stale is { Count: > 0 })
        {
            var names = string.Join(", ", stale.Select(n => "$" + n));
            var plural = stale.Count > 1;
            var example = stale[0];
            lines.Add($"⚠️ SCOPE: {names} {(plural ? "were" : "was")} assigned without a scope prefix in an earlier execute_command call. " +
                      "A local variable is discarded when the call that assigned it ends, so " +
                      $"{(plural ? "they read" : "it reads")} as $null here. " +
                      $"Assign with the $script: prefix to keep a value across calls (e.g. $script:{example} = …); reading it as ${example} then works.");
        }

        if (undefined is { Count: > 0 })
        {
            var names = string.Join(", ", undefined.Select(n => "$" + n));
            var plural = undefined.Count > 1;
            lines.Add($"⚠️ UNDEFINED: {names} {(plural ? "are" : "is")} not defined in this console and read{(plural ? "" : "s")} as $null. " +
                      "A value set in another console, or before a new server session, is not available here.");
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
