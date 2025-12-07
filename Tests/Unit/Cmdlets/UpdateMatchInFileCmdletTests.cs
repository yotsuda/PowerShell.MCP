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
        cmdlet.Path = ["test.txt"];
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
        // Arrange: Create ASCII file
        var testFile = Path.GetTempFileName();
        File.WriteAllText(testFile, "Line 1\nLine 2\nLine 3", Encoding.ASCII);

        try
        {
            // Act: Detect encoding
            var encoding = EncodingHelper.DetectEncoding(testFile);

            // Assert: Detected as ASCII
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
        // Arrange: ASCII metadata and array containing non-ASCII characters
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };

        string[] contentWithNonAscii = ["こんにちは", "世界"];

        // Act: Check if upgrade is needed
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata,
            contentWithNonAscii,
            false, // Encoding not explicitly specified
            out string? upgradeMessage
        );

        // Assert: Upgraded to UTF-8
        Assert.True(upgraded);
        Assert.NotNull(upgradeMessage);
        Assert.Equal(65001, metadata.Encoding.CodePage); // UTF-8
    }

    [Fact]
    public void EncodingHelper_DoesNotUpgrade_WhenEncodingExplicitlySpecified()
    {
        // Arrange: ASCII metadata and non-ASCII characters
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };

        string[] contentWithNonAscii = ["こんにちは"];

        // Act: When encoding is explicitly specified
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata,
            contentWithNonAscii,
            true, // Encoding explicitly specified
            out string? upgradeMessage
        );

        // Assert: Not upgraded
        Assert.False(upgraded);
        Assert.Null(upgradeMessage);
        Assert.Equal(20127, metadata.Encoding.CodePage); // Still ASCII
    }

    [Fact]
    public void EncodingHelper_DoesNotUpgrade_WhenContentIsAsciiOnly()
    {
        // Arrange: ASCII metadata and array with ASCII characters only
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding.ASCII,
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };

        string[] asciiContent = ["Hello", "World"];

        // Act: Upgrade check
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata,
            asciiContent,
            false,
            out string? upgradeMessage
        );

        // Assert: Not upgraded
        Assert.False(upgraded);
        Assert.Null(upgradeMessage);
        Assert.Equal(20127, metadata.Encoding.CodePage); // Still ASCII
    }

    [Fact]
    public void EncodingHelper_DoesNotUpgrade_WhenAlreadyUtf8()
    {
        // Arrange: UTF-8 metadata
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = new UTF8Encoding(false),
            NewlineSequence = "\r\n",
            HasTrailingNewline = true
        };

        string[] contentWithNonAscii = ["こんにちは"];

        // Act: Upgrade check
        bool upgraded = EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata,
            contentWithNonAscii,
            false,
            out string? upgradeMessage
        );

        // Assert: Not upgraded since already UTF-8
        Assert.False(upgraded);
        Assert.Null(upgradeMessage);
        Assert.Equal(65001, metadata.Encoding.CodePage); // Remains UTF-8
    }
}
