using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Text;

namespace PowerShell.MCP.Services;

/// <summary>
/// PSHostUserInterface decorator that captures host-level
/// <c>Write</c> / <c>WriteLine</c> calls into a StringBuilder while
/// forwarding every method (Read*, Prompt*, the per-stream writers,
/// progress, etc.) to a wrapped inner UI. Used by the MCP polling
/// engine to observe output that bypasses BOTH the PowerShell stream
/// system AND <c>[Console]::Out</c> entirely:
/// <list type="bullet">
///   <item>
///     <c>What if:</c> messages from <c>$PSCmdlet.ShouldProcess</c>
///     route through <c>host.UI.WriteLine(string)</c>. They are
///     not in any of streams 1-6, and pwsh's ConsoleHost caches the
///     <c>Console.Out</c> writer at startup so a later
///     <c>[Console]::SetOut</c> tee doesn't see them either.
///   </item>
///   <item>
///     Direct <c>$Host.UI.WriteLine("...")</c> calls from user
///     scripts. Same bypass shape.
///   </item>
/// </list>
/// The polling engine swaps the host's internal <c>_externalUI</c>
/// field (reflection on <c>InternalHostUserInterface</c>) to a
/// TeePSHostUserInterface for the duration of one user command, then
/// restores the original. Visible-console rendering is preserved
/// because every method forwards to the original inner UI; the AI
/// side gets the captured text from the StringBuilder.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>Write(String)</c>, <c>Write(ConsoleColor, ConsoleColor,
/// String)</c>, and <c>WriteLine(String)</c> are taps that record
/// into the capture buffer. The per-stream writers
/// (<c>WriteWarningLine</c>, <c>WriteVerboseLine</c>,
/// <c>WriteDebugLine</c>, <c>WriteErrorLine</c>,
/// <c>WriteInformation</c>) are NOT captured here because the polling
/// engine already captures those streams independently
/// (<c>-WarningVariable</c>, <c>-InformationVariable</c>, stream
/// redirects <c>2&gt;&amp;1 4&gt;&amp;1 5&gt;&amp;1</c>). Tapping
/// them at the host UI level would double-count.
/// </para>
/// <para>
/// All <c>Read*</c> / <c>Prompt*</c> methods are pure passthrough so
/// interactive flows (Read-Host, credential prompts, ShouldContinue)
/// continue to work against the real terminal. Capturing those would
/// break interaction without giving the AI anything meaningful.
/// </para>
/// </remarks>
public sealed class TeePSHostUserInterface : PSHostUserInterface
{
    private readonly PSHostUserInterface _inner;
    private readonly StringBuilder _capture;

    public TeePSHostUserInterface(PSHostUserInterface inner, StringBuilder capture)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    public override PSHostRawUserInterface RawUI => _inner.RawUI;

    public override string ReadLine() => _inner.ReadLine();
    public override SecureString ReadLineAsSecureString() => _inner.ReadLineAsSecureString();

    public override void Write(string value)
    {
        _inner.Write(value);
        if (value != null) _capture.Append(value);
    }

    public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
    {
        _inner.Write(foregroundColor, backgroundColor, value);
        if (value != null) _capture.Append(value);
    }

    public override void WriteLine(string value)
    {
        _inner.WriteLine(value);
        if (value != null) _capture.Append(value);
        _capture.Append('\n');
    }

    // Per-stream writers: pure passthrough. The polling engine captures
    // these signals via the stream system (-WarningVariable,
    // -InformationVariable, 2>&1 4>&1 5>&1) so a host-level tap here
    // would double-count.
    public override void WriteErrorLine(string value)   => _inner.WriteErrorLine(value);
    public override void WriteDebugLine(string message) => _inner.WriteDebugLine(message);
    public override void WriteVerboseLine(string message) => _inner.WriteVerboseLine(message);
    public override void WriteWarningLine(string message) => _inner.WriteWarningLine(message);
    public override void WriteProgress(long sourceId, ProgressRecord record) => _inner.WriteProgress(sourceId, record);

    // Interactive prompts: pure passthrough. The user is at the
    // visible terminal; the AI side does not need (and should not
    // intercept) credential / Read-Host / ShouldContinue prompts.
    public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions)
        => _inner.Prompt(caption, message, descriptions);

    public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
        => _inner.PromptForCredential(caption, message, userName, targetName);

    public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
        => _inner.PromptForCredential(caption, message, userName, targetName, allowedCredentialTypes, options);

    public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
        => _inner.PromptForChoice(caption, message, choices, defaultChoice);
}
