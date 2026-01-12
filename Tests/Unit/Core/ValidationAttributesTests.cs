using System.Management.Automation;
using System.Reflection;
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests;

/// <summary>
/// Unit tests for ValidateLineRangeAttribute
/// Uses reflection to test protected methods
/// </summary>
public class ValidationAttributesTests
{
    private readonly ValidateLineRangeAttribute _attribute;
    private readonly MethodInfo _validateMethod;

    public ValidationAttributesTests()
    {
        _attribute = new ValidateLineRangeAttribute();

        // Get protected method via reflection
        _validateMethod = typeof(ValidateLineRangeAttribute)
            .GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    private void InvokeValidate(object? arguments)
    {
        _validateMethod.Invoke(_attribute, new object?[] { arguments, null });
    }

    #region Valid Cases


    [Fact]
    public void Validate_MultipleValidLines_NoException()
    {
        // Arrange
        var validRange = new[] { 1, 5, 10 };

        // Act & Assert
        var exception = Record.Exception(() => InvokeValidate(validRange));

        Assert.Null(exception);
    }


    [Fact]
    public void Validate_LargeLineNumbers_NoException()
    {
        // Arrange
        var validRange = new[] { 1000, 5000, 10000 };

        // Act & Assert
        var exception = Record.Exception(() => InvokeValidate(validRange));

        Assert.Null(exception);
    }



    #endregion

    #region Invalid Cases

    [Fact]
    public void Validate_ZeroLineNumber_ThrowsValidationException()
    {
        // Arrange
        var invalidRange = new[] { 0 };

        // Act & Assert
        var exception = Assert.Throws<TargetInvocationException>(() =>
            InvokeValidate(invalidRange));

        Assert.IsType<ValidationMetadataException>(exception.InnerException);
        Assert.Contains("Start line cannot be 0", exception.InnerException!.Message);
    }


    [Fact]
    public void Validate_NegativeSingleValue_IsValidForTailLines()
    {
        // Arrange - negative single value means "last N lines"
        var validRange = new[] { -10 };

        // Act & Assert - should not throw
        var exception = Record.Exception(() => InvokeValidate(validRange));
        Assert.Null(exception);
    }




    #endregion

    #region Edge Cases

    [Fact]
    public void Validate_EmptyArray_NoException()
    {
        // Arrange
        var emptyRange = new int[] { };

        // Act & Assert
        var exception = Record.Exception(() => InvokeValidate(emptyRange));

        Assert.Null(exception);
    }





    #endregion

    #region Attribute Usage


    #endregion

    #region Reflection Tests


    #endregion
}
