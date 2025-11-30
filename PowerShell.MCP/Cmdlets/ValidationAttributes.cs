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
                // range[1] <= 0 means "to end of file" - always valid with positive start
                if (range[0] > 0 && range[1] <= 0)
                {
                    return; // Valid: 5,-1 or 5,0 or 5,-99 all mean "line 5 to end"
                }
                
                // Both negative: -10,-1 means "10th from end to last"
                if (range[0] < 0 && range[1] < 0)
                {
                    return; // Valid
                }
                
                // Both positive: 10,20 means "line 10 to 20"
                if (range[0] > 0 && range[1] > 0)
                {
                    return; // Valid
                }
                
                // Mixed (negative start, positive end): not allowed
                throw new ValidationMetadataException(
                    "Cannot mix negative start with positive end in line range.");
            }
        }
    }
}
