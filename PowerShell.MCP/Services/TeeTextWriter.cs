using System.IO;
using System.Text;

namespace PowerShell.MCP.Services;

/// <summary>
/// TextWriter that forwards every write to a primary writer (typically the
/// original Console.Out / Console.Error) AND captures the same bytes into a
/// StringBuilder for later retrieval. Used by the MCP polling engine to
/// observe output that bypasses PowerShell's stream system entirely:
/// <c>[Console]::WriteLine</c>, <c>[Console]::Error.WriteLine</c>, and
/// .NET libraries that write directly to the standard streams without
/// going through Write-Output / Write-Error. Pre-fix those bytes were
/// invisible to the AI side because <c>2&gt;&amp;1 | Tee-Object</c> only
/// taps the PowerShell pipeline; .NET-level direct writes never enter
/// it. The polling engine swaps Console.Out and Console.Error to
/// instances of this class for the duration of one user command, then
/// restores the originals — the visible terminal sees the writes in
/// real time (forwarded via the primary), and the AI gets them from
/// the StringBuilder.
/// </summary>
/// <remarks>
/// <para>
/// Only Write(char), Write(string), and WriteLine(string) are
/// overridden. The base TextWriter routes every other overload through
/// these eventually, so capture coverage is complete without
/// re-implementing the dozen-plus convenience overloads.
/// </para>
/// <para>
/// Encoding is delegated to the primary so the wrapped writer reports
/// the same encoding as what callers were already targeting (UTF-8 in
/// pwsh.exe under default configuration). Misreporting it would
/// confuse code that inspects <c>[Console]::Out.Encoding</c> for byte-
/// level work.
/// </para>
/// <para>
/// Flush forwards to the primary; the StringBuilder doesn't need
/// flushing. Not thread-safe — the polling engine runs one command at a
/// time and the swap/restore happens around a single
/// <c>Invoke-Captured | Tee-Object | Out-Host</c> pipeline, so
/// concurrent writers are not a concern in practice.
/// </para>
/// </remarks>
public sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _primary;
    private readonly StringBuilder _capture;

    public TeeTextWriter(TextWriter primary, StringBuilder capture)
    {
        _primary = primary;
        _capture = capture;
    }

    public override Encoding Encoding => _primary.Encoding;

    public override void Write(char value)
    {
        _primary.Write(value);
        _capture.Append(value);
    }

    public override void Write(string? value)
    {
        if (value == null) return;
        _primary.Write(value);
        _capture.Append(value);
    }

    public override void WriteLine(string? value)
    {
        _primary.WriteLine(value);
        if (value != null) _capture.Append(value);
        _capture.Append(_primary.NewLine ?? "\n");
    }

    public override void Flush() => _primary.Flush();
}
