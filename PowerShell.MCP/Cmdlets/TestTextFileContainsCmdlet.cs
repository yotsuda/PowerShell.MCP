using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Tests whether a text file contains specific text or pattern.
/// LLM optimized: Returns boolean immediately on first match for maximum performance.
/// Supports both literal string search (-Contains) and regex pattern matching (-Pattern).
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "TextFileContains")]
[OutputType(typeof(bool))]
public class TestTextFileContainsCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Contains { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    protected override void ProcessRecord()
    {
        // LineRange validation (highest priority)
        ValidateLineRange(LineRange);

        // Get paths from -Path or -LiteralPath
        string[] inputPaths = Path ?? LiteralPath;
        bool isLiteralPath = (LiteralPath != null);

        // For multiple files, return true if ANY file matches (OR operation)
        bool anyMatch = false;

        foreach (var inputPath in inputPaths)
        {
            System.Collections.ObjectModel.Collection<string> resolvedPaths;
            
            try
            {
                if (isLiteralPath)
                {
                    // -LiteralPath: no wildcard expansion
                    var resolved = GetUnresolvedProviderPathFromPSPath(inputPath);
                    resolvedPaths = new System.Collections.ObjectModel.Collection<string> { resolved };
                }
                else
                {
                    // -Path: wildcard expansion
                    resolvedPaths = GetResolvedProviderPathFromPSPath(inputPath, out _);
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "PathResolutionFailed",
                    ErrorCategory.InvalidArgument,
                    inputPath));
                continue;
            }
            
            foreach (var resolvedPath in resolvedPaths)
            {
                if (!File.Exists(resolvedPath))
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException($"File not found: {inputPath}"),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolvedPath));
                    continue;
                }

                try
                {
                    var encoding = TextFileUtility.GetEncoding(resolvedPath, Encoding);

                    // Check if file is empty
                    var fileInfo = new FileInfo(resolvedPath);
                    if (fileInfo.Length == 0)
                    {
                        continue; // Empty file has no matches
                    }

                    // Perform the test based on parameters
                    bool fileMatches;
                    if (!string.IsNullOrEmpty(Pattern))
                    {
                        fileMatches = TestWithPattern(resolvedPath, encoding);
                    }
                    else if (!string.IsNullOrEmpty(Contains))
                    {
                        fileMatches = TestWithContains(resolvedPath, encoding);
                    }
                    else
                    {
                        // No search criteria specified - return false
                        fileMatches = false;
                    }

                    if (fileMatches)
                    {
                        anyMatch = true;
                        // Early exit optimization: if we found a match and processing multiple files,
                        // we can return true immediately
                        if (resolvedPaths.Count > 1 || inputPaths.Length > 1)
                        {
                            WriteObject(true);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "TestTextFileFailed", ErrorCategory.ReadError, resolvedPath));
                }
            }
        }

        // Write the final result
        WriteObject(anyMatch);
    }

    /// <summary>
    /// Test file with regex pattern. Returns true on first match (1-pass, early exit).
    /// </summary>
    private bool TestWithPattern(string filePath, System.Text.Encoding encoding)
    {
        var regex = new Regex(Pattern, RegexOptions.Compiled);
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        int skipCount = startLine - 1;
        int takeCount = endLine - startLine + 1;

        var linesToSearch = File.ReadLines(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        // 1-pass: return true immediately on first match
        foreach (var line in linesToSearch)
        {
            if (regex.IsMatch(line))
            {
                return true; // Early exit on first match
            }
        }

        return false; // No matches found
    }

    /// <summary>
    /// Test file with literal string (Contains). Returns true on first match (1-pass, early exit).
    /// </summary>
    private bool TestWithContains(string filePath, System.Text.Encoding encoding)
    {
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        int skipCount = startLine - 1;
        int takeCount = endLine - startLine + 1;

        var linesToSearch = File.ReadLines(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        // 1-pass: return true immediately on first match
        foreach (var line in linesToSearch)
        {
            if (line.Contains(Contains, StringComparison.Ordinal))
            {
                return true; // Early exit on first match
            }
        }

        return false; // No matches found
    }
}
