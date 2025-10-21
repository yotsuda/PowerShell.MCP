using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Validates LineRange parameter values
/// - First value (start line) must be 1 or greater (1-based indexing)
/// - Second value (end line) can be 0 or negative to indicate end of file
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ValidateLineRangeAttribute : ValidateArgumentsAttribute
{
    protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics)
    {
        if (arguments is int[] range)
        {
            if (range.Length == 0)
                return; // Empty array is valid
            
            // First value (start line) must be >= 1
            if (range[0] < 1)
            {
                throw new ValidationMetadataException(
                    $"Start line must be 1 or greater (1-based indexing). Invalid value: {range[0]}");
            }
            
            // Second value (end line) can be 0 or negative (means end of file)
            // or >= 1 for explicit end line
            if (range.Length > 1 && range[1] != 0 && range[1] < -1)
            {
                // Allow -1, -2, etc. all mean "end of file"
                // This check is intentionally lenient
            }
        }
    }
}
