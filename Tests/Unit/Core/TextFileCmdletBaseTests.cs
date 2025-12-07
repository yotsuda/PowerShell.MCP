using System.Management.Automation;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests;

/// <summary>
/// Unit tests for TextFileCmdletBase
/// Tests protected methods using test-derived class
/// </summary>
public class TextFileCmdletBaseTests
{
    /// <summary>
    /// Test-derived class - exposes protected methods as public
    /// </summary>
    private class TestCmdlet : TextFileCmdletBase
    {
        // Expose protected static methods as public
        public static bool PublicIsPSDrivePath(string path)
            => IsPSDrivePath(path);

        public string PublicGetDisplayPath(string originalPath, string resolvedPath)
            => GetDisplayPath(originalPath, resolvedPath);

        public string PublicGetDisplayPathForWildcard(string originalPattern, string resolvedPath)
            => GetDisplayPathForWildcard(originalPattern, resolvedPath);

        public void PublicValidateLineRange(int[]? lineRange)
            => ValidateLineRange(lineRange);

        public void PublicValidateContainsAndPatternMutuallyExclusive(string? contains, string? pattern)
            => ValidateContainsAndPatternMutuallyExclusive(contains, pattern);
    }

    #region IsPSDrivePath Tests

    [Theory]
    [InlineData("Temp:\\\\file.txt", true)]
    [InlineData("Env:\\\\Path", true)]
    [InlineData("HKLM:\\\\Software", true)]
    [InlineData("Variable:\\\\PSVersionTable", true)]
    public void IsPSDrivePath_ValidPSDrivePaths_ReturnsTrue(string path, bool expected)
    {
        // Act
        var result = TestCmdlet.PublicIsPSDrivePath(path);

        // Assert
        Assert.Equal(expected, result);
    }


    [Theory]
    [InlineData("C:\\\\temp\\\\file.txt", false)]
    [InlineData("D:\\\\data", false)]
    [InlineData("\\\\\\\\server\\\\share", false)]
    [InlineData("relative\\\\path", false)]
    [InlineData("file.txt", false)]
    [InlineData("", false)]
    public void IsPSDrivePath_NonPSDrivePaths_ReturnsFalse(string path, bool expected)
    {
        // Act
        var result = TestCmdlet.PublicIsPSDrivePath(path);

        // Assert
        Assert.Equal(expected, result);
    }


    [Fact]
    public void IsPSDrivePath_NullPath_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(TestCmdlet.PublicIsPSDrivePath(null!));
    }


    #endregion

    #region ValidateLineRange Tests

    [Fact]
    public void ValidateLineRange_ValidSingleValue_NoException()
    {
        // Arrange
        var cmdlet = new TestCmdlet();
        var validRange = new[] { 5 };

        // Act & Assert
        var exception = Record.Exception(() => cmdlet.PublicValidateLineRange(validRange));
        Assert.Null(exception);
    }


    [Fact]
    public void ValidateLineRange_ValidTwoValues_NoException()
    {
        // Arrange
        var cmdlet = new TestCmdlet();
        var validRange = new[] { 5, 10 };

        // Act & Assert
        var exception = Record.Exception(() => cmdlet.PublicValidateLineRange(validRange));
        Assert.Null(exception);
    }



    [Fact]
    public void ValidateLineRange_StartGreaterThanEnd_ThrowsException()
    {
        // Arrange
        var cmdlet = new TestCmdlet();
        var invalidRange = new[] { 10, 5 };

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            cmdlet.PublicValidateLineRange(invalidRange));
        
        Assert.Contains("must be less than or equal to end", exception.Message);
    }





    #endregion

    #region ValidateContainsAndPatternMutuallyExclusive Tests




    [Fact]
    public void ValidateContainsAndPattern_BothSpecified_ThrowsException()
    {
        // Arrange
        var cmdlet = new TestCmdlet();

        // Act & Assert
        var exception = Assert.Throws<PSArgumentException>(() => 
            cmdlet.PublicValidateContainsAndPatternMutuallyExclusive("search", "pattern"));
        
        Assert.Contains("Cannot specify both -Contains and -Pattern", exception.Message);
    }




    #endregion

    #region GetDisplayPathForWildcard Tests

    [Fact]
    public void GetDisplayPathForWildcard_SimplePattern_ReturnsFileName()
    {
        // Arrange
        var cmdlet = new TestCmdlet();
        var pattern = "*.txt";
        var resolved = "C:\\\\temp\\\\file.txt";

        // Act
        var result = cmdlet.PublicGetDisplayPathForWildcard(pattern, resolved);

        // Assert
        Assert.Equal("file.txt", result);
    }




    #endregion
}
