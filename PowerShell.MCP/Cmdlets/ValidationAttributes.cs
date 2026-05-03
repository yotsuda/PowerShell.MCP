using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Validates LineRange parameter values (string format: "10", "10,20", "10-20")
/// - Single value: positive = specific line; negative = tail count
///   (Show-TextFiles / Remove-LinesFromFile — e.g. -LineRange -5 means last 5 lines).
/// - Two values:
///     - Start (first) MUST be positive (1-based line number).
///     - End (second): positive = 1-based line, 0 / negative = end of
///       file. -1 / -10 / -99 etc. all collapse to "to EOF".
///   Zero or negative as the FIRST value with two args is rejected —
///   the "from-end" semantic for two-arg form is achieved via the second
///   value, not the first.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ValidateLineRangeAttribute : ValidateArgumentsAttribute
{
    protected override void Validate(object arguments, EngineIntrinsics engineIntrinsics)
    {
        string[]? arr = arguments as string[];
        if (arr == null || arr.Length == 0)
            return;

        try
        {
            var (startLine, endLine) = TextFileUtility.ParseLineRange(arr);
            // Detect two-arg form: ParseLineRange normalizes endLine != startLine
            // when 2 args were given (positive end stays as-is, non-positive
            // collapses to int.MaxValue). With 1 arg, endLine == startLine.
            bool twoArgs = endLine != startLine;
            // Start line 0 has no meaning in a 1-based numbering system.
            if (startLine == 0)
            {
                throw new ValidationMetadataException(
                    "Start line cannot be 0. Use a positive 1-based line number; pass 0 or a negative number as the SECOND value to mean end-of-file.");
            }
            // Tail range "-10,-1" is allowed: first negative + endLine collapsed
            // to MaxValue (i.e. original second was non-positive too).
            bool isTailRange = twoArgs && startLine < 0 && endLine == int.MaxValue;
            if (twoArgs && startLine < 0 && !isTailRange)
            {
                throw new ValidationMetadataException(
                    $"With two values, start line must be a positive 1-based line number (got {startLine}). " +
                    "Use a positive number as the start; pass 0 or a negative number as the SECOND value to mean end-of-file.");
            }
        }
        catch (ArgumentException ex)
        {
            throw new ValidationMetadataException(ex.Message);
        }
    }
}
