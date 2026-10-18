using System.Collections.ObjectModel;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Security;
using System.Text;
using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// On Linux and macOS the console host renders through Console.Out, so the
/// direct-write tee saw every formatted result a second time and the AI got
/// the whole output again under "CONSOLE.OUT (direct)". Host writes are now
/// made inside TeeTextWriter.Suppress: still shown, not captured.
/// </summary>
public class TeeTextWriterTests
{
    [Fact]
    public void DirectWrites_AreCaptured_AndForwarded()
    {
        var shown = new StringWriter();
        var captured = new StringBuilder();
        var tee = new TeeTextWriter(shown, captured);

        tee.Write("a");
        tee.WriteLine("b");

        Assert.Equal("a" + "b" + shown.NewLine, shown.ToString());
        Assert.Equal("a" + "b" + shown.NewLine, captured.ToString());
    }

    [Fact]
    public void SuppressedWrites_AreForwardedButNotCaptured_AndScopesNest()
    {
        var shown = new StringWriter();
        var captured = new StringBuilder();
        var tee = new TeeTextWriter(shown, captured);

        using (TeeTextWriter.Suppress())
        {
            tee.WriteLine("outer");
            using (TeeTextWriter.Suppress()) tee.Write('x');
            tee.Write("still suppressed");
        }
        tee.Write("direct");

        Assert.Contains("outer", shown.ToString());
        Assert.Contains("still suppressed", shown.ToString());
        Assert.Equal("direct", captured.ToString());
    }

    [Fact]
    public void Suppress_DisposedTwice_DoesNotUnbalance()
    {
        var captured = new StringBuilder();
        var tee = new TeeTextWriter(new StringWriter(), captured);

        var scope = TeeTextWriter.Suppress();
        scope.Dispose();
        scope.Dispose();
        using (TeeTextWriter.Suppress()) tee.Write("hidden");
        tee.Write("seen");

        Assert.Equal("seen", captured.ToString());
    }

    [Fact]
    public void AtLineStart_FollowsTheLastText_SuppressedOrNot()
    {
        // The engine asks this instead of Console.CursorLeft on Linux and
        // macOS, where CursorLeft waits for the terminal's reply on stdin.
        var tee = new TeeTextWriter(new StringWriter(), new StringBuilder());

        tee.Write("no newline yet");
        Assert.False(TeeTextWriter.AtLineStart);
        tee.WriteLine("done");
        Assert.True(TeeTextWriter.AtLineStart);
        using (TeeTextWriter.Suppress()) tee.Write("host output without newline");
        Assert.False(TeeTextWriter.AtLineStart);
        tee.Write('\n');
        Assert.True(TeeTextWriter.AtLineStart);

        // A colour reset after the newline leaves the cursor at the line start.
        tee.Write("coloured line\n\u001b[0m");
        Assert.True(TeeTextWriter.AtLineStart);
        tee.Write("\u001b[32mgreen, no newline\u001b[0m");
        Assert.False(TeeTextWriter.AtLineStart);

        // A new tee starts a command right after its echo, at a line start.
        _ = new TeeTextWriter(new StringWriter(), new StringBuilder());
        Assert.True(TeeTextWriter.AtLineStart);
    }

    [Fact]
    public void HostWrites_ThatGoThroughConsoleOut_AreNotCapturedAsDirect()
    {
        // A host that renders through the console writer, as ConsoleHost
        // does on Linux and macOS.
        var shown = new StringWriter();
        var direct = new StringBuilder();
        var tee = new TeeTextWriter(shown, direct);
        var hostText = new StringBuilder();
        var ui = new TeePSHostUserInterface(new ConsoleWritingUI(tee), hostText);

        ui.WriteLine("formatted result");
        ui.Write(ConsoleColor.Green, ConsoleColor.Black, "write-host text");
        ui.WriteWarningLine("a warning");
        tee.WriteLine("[Console]::WriteLine text");

        Assert.Contains("formatted result", shown.ToString());
        Assert.Contains("a warning", shown.ToString());
        Assert.Equal("[Console]::WriteLine text" + shown.NewLine, direct.ToString());
        Assert.Contains("formatted result", hostText.ToString());
    }

    private sealed class ConsoleWritingUI(TextWriter console) : PSHostUserInterface
    {
        public override PSHostRawUserInterface RawUI => null!;
        public override string ReadLine() => "";
        public override SecureString ReadLineAsSecureString() => new();
        public override void Write(string value) => console.Write(value);
        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) => console.Write(value);
        public override void WriteLine(string value) => console.WriteLine(value);
        public override void WriteErrorLine(string value) => console.WriteLine(value);
        public override void WriteDebugLine(string message) => console.WriteLine(message);
        public override void WriteProgress(long sourceId, ProgressRecord record) { }
        public override void WriteVerboseLine(string message) => console.WriteLine(message);
        public override void WriteWarningLine(string message) => console.WriteLine(message);
        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions) => new();
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName) => null!;
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options) => null!;
        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice) => defaultChoice;
    }
}
