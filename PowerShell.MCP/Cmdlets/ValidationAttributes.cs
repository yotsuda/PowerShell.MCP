using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Validates LineRange parameter values
/// - Positive values: 1-based line numbers from start
/// - Negative values: offset from end (-1 = last line, -10 = 10th from end)
/// - Zero/negative second value: indicates end of file
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
            
            // First value: 0 is not allowed
            if (range[0] == 0)
            {
                throw new ValidationMetadataException(
                    "Start line cannot be 0. Use positive numbers (1-based) or negative numbers (from end).");
            }
            
            // If only one element and negative, it means "last N lines"
            if (range.Length == 1 && range[0] < 0)
            {
                return; // Valid: -10 means last 10 lines
            }
            
            // If two elements, validate the combination
            if (range.Length > 1)
            {
                // Both negative: -10,-1 means "10th from end to last"
                // Both positive: 10,20 means "line 10 to 20"
                // Mixed: not allowed for now
                if ((range[0] > 0 && range[1] < 0 && range[1] != -1 && range[1] != 0) ||
                    (range[0] < 0 && range[1] > 0))
                {
                    // Allow range[1] == -1 or 0 with positive range[0] (means to end of file)
                    if (!(range[0] > 0 && (range[1] == -1 || range[1] == 0)))
                    {
                        throw new ValidationMetadataException(
                            "Cannot mix positive and negative line numbers in range (except -1 or 0 for end of file).");
                    }
                }
            }
        }
    }
}
