using System.Text;
using PowerShell.MCP.Cmdlets;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// Encoding detection must prefer valid UTF-8 over a charset guess: a guess of
/// windows-1252 for a UTF-8 file makes an edit cmdlet write the file back in
/// that code page, and the text the edit added is then not UTF-8.
/// </summary>
public class EncodingDetectionTests
{
    [Fact]
    public void ValidUtf8WithSparseNonAscii_IsUtf8()
    {
        var text = new string('a', 17000) + " — 💡 " + new string('b', 100);
        var bytes = new UTF8Encoding(false).GetBytes(text);
        var encoding = FileMetadataHelper.DetectEncodingFromBytes(bytes, bytes.Length);
        Assert.Equal(65001, encoding.CodePage);
        Assert.Empty(encoding.GetPreamble());
    }

    [Fact]
    public void Utf8SampleCutInsideAMultiByteSequence_IsStillUtf8()
    {
        var bytes = new UTF8Encoding(false).GetBytes(new string('a', 100) + "日本語");
        var encoding = FileMetadataHelper.DetectEncodingFromBytes(bytes, bytes.Length - 1);
        Assert.Equal(65001, encoding.CodePage);
    }

    [Fact]
    public void PureAscii_StillGoesToTheGuesser()
    {
        var bytes = Encoding.ASCII.GetBytes(new string('a', 100));
        var encoding = FileMetadataHelper.DetectEncodingFromBytes(bytes, bytes.Length);
        Assert.True(encoding.CodePage is 20127 or 65001, $"unexpected code page {encoding.CodePage}");
    }

    [Fact]
    public void ShiftJis_IsNotMistakenForUtf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(932).GetBytes("日本語のテキストです。" + new string('a', 50));
        var encoding = FileMetadataHelper.DetectEncodingFromBytes(bytes, bytes.Length);
        Assert.NotEqual(65001, encoding.CodePage);
    }
}