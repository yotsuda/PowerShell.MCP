using System.Text;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class UpdateMatchInFileCmdletTests : IDisposable
{
    private readonly string _testFile;

    public UpdateMatchInFileCmdletTests()
    {
        _testFile = Path.GetTempFileName();
        File.WriteAllLines(_testFile, new[] { "Hello World", "Test Line" });
    }

    public void Dispose()
    {
        if (File.Exists(_testFile)) File.Delete(_testFile);
    }

    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        var cmdlet = new UpdateMatchInFileCmdlet();
        Assert.NotNull(cmdlet);
        Assert.IsAssignableFrom<TextFileCmdletBase>(cmdlet);
    }

    [Fact]
    public void Path_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateMatchInFileCmdlet();
        cmdlet.Path = new[] { "test.txt" };
        Assert.NotNull(cmdlet.Path);
    }

    [Fact]
    public void Pattern_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateMatchInFileCmdlet();
        cmdlet.Pattern = @"\d+";
        Assert.Equal(@"\d+", cmdlet.Pattern);
    }

    [Fact]
    public void Replacement_SetValue_StoresCorrectly()
    {
        var cmdlet = new UpdateMatchInFileCmdlet
        {
            Replacement = "replaced"
        };
        Assert.Equal("replaced", cmdlet.Replacement);
    }

    [Fact]
    public void EncodingHelper_DetectsAsciiCorrectly()
    {
        // Arrange: ASCIIファイルを作成
        var testFile = Path.GetTempFileName();
        File.WriteAllText(testFile, "Line 1\nLine 2\nLine 3", Encoding.ASCII);
        
        try
        {
            // Act: エンコーディングを検出
            var encoding = EncodingHelper.DetectEncoding(testFile);
            
            // Assert: ASCIIとして検出される
            Assert.Equal(20127, encoding.CodePage); // US-ASCII
        }
        finally
        {
            if (File.Exists(testFile)) File.Delete(testFile);
        }
    }

    [Fact]
    public void EncodingHelper_UpgradeLogic_WorksWithNonAsciiReplacement()
    {
        // Arrange: ASCIIメタデータと非ASCII文字を含む配列
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };
        
        string[] contentWithNonAscii = new[] { "こんにちは", "世界" };
        
        // Act: アップグレードが必要かチェック
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata, 
            contentWithNonAscii, 
            false, // エンコーディングが明示的に指定されていない
            out string? upgradeMessage
        );
        
        // Assert: UTF-8にアップグレードされる
        Assert.True(upgraded);
        Assert.NotNull(upgradeMessage);
        Assert.Equal(65001, metadata.Encoding.CodePage); // UTF-8
    }

    [Fact]
    public void EncodingHelper_DoesNotUpgrade_WhenEncodingExplicitlySpecified()
    {
        // Arrange: ASCIIメタデータと非ASCII文字
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };
        
        string[] contentWithNonAscii = new[] { "こんにちは" };
        
        // Act: エンコーディングが明示的に指定されている場合
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata, 
            contentWithNonAscii, 
            true, // エンコーディングが明示的に指定されている
            out string? upgradeMessage
        );
        
        // Assert: アップグレードされない
        Assert.False(upgraded);
        Assert.Null(upgradeMessage);
        Assert.Equal(20127, metadata.Encoding.CodePage); // 依然としてASCII
    }

    [Fact]
    public void EncodingHelper_DoesNotUpgrade_WhenContentIsAsciiOnly()
    {
        // Arrange: ASCIIメタデータとASCII文字のみの配列
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };
        
        string[] asciiContent = new[] { "Hello", "World" };
        
        // Act: アップグレードチェック
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata, 
            asciiContent, 
            false,
            out string? upgradeMessage
        );
        
        // Assert: アップグレードされない
        Assert.False(upgraded);
        Assert.Null(upgradeMessage);
        Assert.Equal(20127, metadata.Encoding.CodePage); // 依然としてASCII
    }

    [Fact]
    public void EncodingHelper_DoesNotUpgrade_WhenAlreadyUtf8()
    {
        // Arrange: UTF-8メタデータ
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = new UTF8Encoding(false),
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };
        
        string[] contentWithNonAscii = new[] { "こんにちは" };
        
        // Act: アップグレードチェック
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata, 
            contentWithNonAscii, 
            false,
            out string? upgradeMessage
        );
        
        // Assert: 既にUTF-8なのでアップグレードされない
        Assert.False(upgraded);
        Assert.Null(upgradeMessage);
        Assert.Equal(65001, metadata.Encoding.CodePage); // UTF-8のまま
    }
}