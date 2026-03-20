using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Validates LineRange parameter values (string format: "10", "10,20", "10-20")
/// - Positive values: 1-based line numbers from start
/// - Negative values: offset from end (-1 = last line, -10 = 10th from end)
/// - Zero/negative second value: indicates end of file
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ValidateLineRangeAttribute : ValidateArgumentsAttribute
{
    protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics)
    {
        if (arguments is not string s || string.IsNullOrWhiteSpace(s))
            return;

        try
        {
            var (startLine, _) = TextFileUtility.ParseLineRange(s);
            if (startLine == 0)
            {
                throw new ValidationMetadataException(
                    "Start line cannot be 0. Use positive numbers (1-based) or negative numbers (from end).");
            }
        }
        catch (ArgumentException ex)
        {
            throw new ValidationMetadataException(ex.Message);
        }
    }
}
