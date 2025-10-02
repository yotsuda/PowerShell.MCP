using System;
using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Validates that line numbers in an array are 1 or greater (1-based indexing)
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ValidateLineRangeAttribute : ValidateArgumentsAttribute
{
    protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics)
    {
        if (arguments is int[] range)
        {
            foreach (var value in range)
            {
                if (value < 1)
                {
                    throw new ValidationMetadataException(
                        $"Line numbers must be 1 or greater (1-based indexing). Invalid value: {value}");
                }
            }
        }
    }
}
