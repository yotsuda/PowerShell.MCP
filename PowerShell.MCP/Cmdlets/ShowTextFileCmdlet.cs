using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Linq;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Displays text file content with line numbers
/// LLM-optimized: 3-digit line numbers, : for matches, - for context (grep standard), shows relative path
/// </summary>
[Cmdlet(VerbsCommon.Show, "TextFile")]
public class ShowTextFileCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Pattern { get; set; }
    
    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Contains { get; set; }


    [Parameter]
    public string? Encoding { get; set; }

    private int _totalFilesProcessed = 0;


    protected override void BeginProcessing()
    {
        ValidateContainsAndPatternMutuallyExclusive(Contains, Pattern);
        
        // Error if pattern contains newlines (never matches in line-by-line processing)
        if (!string.IsNullOrEmpty(Pattern) && (Pattern.Contains('\n') || Pattern.Contains('\r')))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Pattern cannot contain newline characters. Show-TextFile processes files line by line."),
                "InvalidPattern",
                ErrorCategory.InvalidArgument,
                Pattern));
        }
        
        if (!string.IsNullOrEmpty(Contains) && (Contains.Contains('\n') || Contains.Contains('\r')))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Contains cannot contain newline characters. Show-TextFile processes files line by line."),
                "InvalidContains",
                ErrorCategory.InvalidArgument,
                Contains));
        }
    }

    protected override void ProcessRecord()
    {
        // LineRange validation
        ValidateLineRange(LineRange);

        // ResolveAndValidateFiles returns IEnumerable, process with lazy evaluation
        var files = ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true);

        foreach (var fileInfo in files)
        {
            try
            {
                // Blank line between files (except first)
                if (_totalFilesProcessed > 0)
                {
                    WriteObject("");
                }
                
                
                _totalFilesProcessed++;

                var encoding = TextFileUtility.GetEncoding(fileInfo.ResolvedPath, Encoding);

                // Handle empty file
                var fileInfoObj = new FileInfo(fileInfo.ResolvedPath);
                if (fileInfoObj.Length == 0)
                {
                    var displayPath = GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath);
                    WriteObject(AnsiColors.Header(displayPath));
                    WriteWarning("File is empty");
                    continue;
                }

                if (!string.IsNullOrEmpty(Pattern))
                {
                    ShowWithPattern(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                }
                else if (!string.IsNullOrEmpty(Contains))
                {
                    ShowWithContains(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                }
                else
                {
                    ShowWithLineRange(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "ShowTextFileFailed", ErrorCategory.ReadError, fileInfo.ResolvedPath));
            }
        }
    }


    private void ShowWithLineRange(string inputPath, string filePath, System.Text.Encoding encoding)
    {
        var displayPath = GetDisplayPath(inputPath, filePath);
        WriteObject(AnsiColors.Header(displayPath));

        // Default to (1, int.MaxValue) if LineRange is null or empty
        int requestedStart = 1;
        int requestedEnd = int.MaxValue;
        
        if (LineRange != null && LineRange.Length > 0)
        {
            requestedStart = LineRange[0];
            requestedEnd = LineRange.Length > 1 ? LineRange[1] : requestedStart;
            
            // Tail specification (-10 or -10,-1)
            if (requestedStart < 0)
            {
                ShowTailLines(filePath, encoding, -requestedStart);
                return;
            }
            
            // If endLine is 0 or negative, continue to end
            if (requestedEnd <= 0)
            {
                requestedEnd = int.MaxValue;
            }
        }

            // Normal range specification
        int skipCount = requestedStart - 1;
        int takeCount = requestedEnd == int.MaxValue ? int.MaxValue : requestedEnd - requestedStart + 1;

        var lines = File.ReadLines(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        int currentLine = requestedStart;
        bool hasOutput = false;

        foreach (var line in lines)
        {
            WriteObject($"{currentLine,3}: {line}");
            currentLine++;
            hasOutput = true;
        }

        if (!hasOutput && LineRange != null)
        {
            WriteWarning($"Line range {requestedStart}-{requestedEnd} is beyond file length. No output.");
        }
    }

    private void ShowTailLines(string filePath, System.Text.Encoding encoding, int tailCount)
    {
            // Get last N lines in single pass using RotateBuffer
        var buffer = new RotateBuffer<(string line, int lineNumber)>(tailCount);
        
        int lineNumber = 1;
        foreach (var line in File.ReadLines(filePath, encoding))
        {
            buffer.Add((line, lineNumber));
            lineNumber++;
        }

        if (buffer.Count == 0)
        {
            WriteWarning("File is empty. No output.");
            return;
        }

        foreach (var item in buffer)
        {
            WriteObject($"{item.lineNumber,3}: {item.line}");
        }
    }

    private void ShowWithPattern(string inputPath, string filePath, System.Text.Encoding encoding)
    {
        var regex = new Regex(Pattern, RegexOptions.Compiled);
        ShowWithMatch(inputPath, filePath, encoding, line => regex.IsMatch(line), "pattern", Pattern, true);
    }

    private void ShowWithContains(string inputPath, string filePath, System.Text.Encoding encoding)
    {
        ShowWithMatch(inputPath, filePath, encoding, line => line.Contains(Contains!, StringComparison.Ordinal), "contain", Contains!, false);
    }

    /// <summary>
    /// Searches lines based on match conditions and displays with context (true single-pass implementation using rotate buffer)
    /// </summary>
    private void ShowWithMatch(string inputPath, string filePath, 
        System.Text.Encoding encoding, 
        Func<string, bool> matchPredicate,
        string matchTypeForWarning,
        string matchValue,
        bool isRegex)
    {
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
        var displayPath = GetDisplayPath(inputPath, filePath);
        bool headerPrinted = false;
        bool anyMatch = false;
        
        using (var enumerator = File.ReadLines(filePath, encoding).GetEnumerator())
        {
            if (!enumerator.MoveNext())
            {
        // Empty file
                WriteWarning("File is empty. No output.");
                return;
            }
            
            int lineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            
        // Rotate buffer: holds previous 2 lines (line content and line number pairs)
            var preContextBuffer = new RotateBuffer<(string line, int lineNumber)>(2);
            
        // Gap candidate (1 line after trailing context)
            (string line, int lineNumber)? gapLineInfo = null;
            
        // Trailing context counter
            int afterMatchCounter = 0;
            
        // Last output line number (for gap detection)
            int lastOutputLine = 0;
            
        // Gap detection: empty line output flag (output when next match is found)
            bool needsGapSeparator = false;
            
            while (true)
            {
        // Match check only within original LineRange
                bool matched = (lineNumber >= startLine && lineNumber <= endLine) && matchPredicate(currentLine);
                
                if (matched)
                {
                    anyMatch = true;
                    
        // Output header (first time only)
                    if (!headerPrinted)
                    {
                        WriteObject(AnsiColors.Header(displayPath));
                        headerPrinted = true;
                    }
                    
        // Gap processing: if gapLineInfo exists, check and output
                    if (gapLineInfo.HasValue)
                    {
        // Calculate start line of leading context
                        int preContextStart = lineNumber;
                        for (int i = preContextBuffer.Count - 1; i >= 0; i--)
                        {
                            var ctx = preContextBuffer[i];
                            if (ctx.lineNumber > lastOutputLine)
                            {
                                preContextStart = ctx.lineNumber;
                            }
                        }
                        
                        var gapInfo = gapLineInfo.Value;
        // If gapLine next line is start of leading context, output gapLine (continuous)
                        if (gapInfo.lineNumber + 1 == preContextStart)
                        {
                            string gapDisplay = ApplyHighlightingIfMatched(gapInfo.line, matchPredicate, matchValue, isRegex);
                            WriteObject($"{gapInfo.lineNumber,3}- {gapDisplay}");
                            lastOutputLine = gapInfo.lineNumber;
                        }
                        else if (gapInfo.lineNumber >= preContextStart)
                        {
        // gapLine is included in leading context -> will be output as leading context, do nothing
                        }
                        else
                        {
        // Gap is 2+ lines -> separate with empty line
                            WriteObject("");
                        }
                        gapLineInfo = null;
                        needsGapSeparator = false;
                    }
                    else if (needsGapSeparator)
                    {
        // Empty line output when no gapLine
                        WriteObject("");
                        needsGapSeparator = false;
                    }
                    
        // Output previous 2 lines (from rotate buffer)
                    foreach (var ctx in preContextBuffer)
                    {
                        if (ctx.lineNumber > lastOutputLine)
                        {
                            string ctxDisplay = ApplyHighlightingIfMatched(ctx.line, matchPredicate, matchValue, isRegex);
                            WriteObject($"{ctx.lineNumber,3}- {ctxDisplay}");
                        }
                    }
                    
        // Output match line (highlighted)
                    string displayLine;
                    if (isRegex)
                    {
                        var regex = new Regex(matchValue, RegexOptions.Compiled);
                        displayLine = regex.Replace(currentLine, m => $"{AnsiColors.Yellow}{m.Value}{AnsiColors.Reset}");
                    }
                    else
                    {
                        displayLine = currentLine.Replace(matchValue, $"{AnsiColors.Yellow}{matchValue}{AnsiColors.Reset}");
                    }
                    WriteObject($"{lineNumber,3}: {displayLine}");
                    
                    afterMatchCounter = 2;
                    lastOutputLine = lineNumber;
        gapLineInfo = null; // Reset gap
        needsGapSeparator = false; // Reset flag
                }
                else
                {
        // Output trailing context
                    if (afterMatchCounter > 0)
                    {
                        string display = ApplyHighlightingIfMatched(currentLine, matchPredicate, matchValue, isRegex);
                        WriteObject($"{lineNumber,3}- {display}");
                        afterMatchCounter--;
                        lastOutputLine = lineNumber;
                    }
                    else if (lastOutputLine > 0)
                    {
        // Gap detection mode
                        if (lineNumber == lastOutputLine + 1)
                        {
        // First line after last output -> store as gap candidate
                            gapLineInfo = (currentLine, lineNumber);
                        }
                        else if (lineNumber == lastOutputLine + 2)
                        {
        // Second line after last output -> gap is 2+ lines -> set empty line flag
                            needsGapSeparator = true;
        // Keep gapLineInfo (used later for check)
                        }
                    }
                }
                
        // Update rotate buffer (save with line number)
                preContextBuffer.Add((currentLine, lineNumber));
                
        // Continue to next
                if (hasNext)
                {
                    lineNumber++;
                    currentLine = enumerator.Current;
                    hasNext = enumerator.MoveNext();
                }
                else
                {
                    break;
                }
            }
        }
        
        // No matches found
        if (!anyMatch)
        {
            string message = matchTypeForWarning == "pattern" 
                ? $"No lines matched pattern: {matchValue}"
                : $"No lines contain: {matchValue}";
            WriteWarning(message);
        }
    }

    /// <summary>
    /// Applies yellow highlighting if line contains match
    /// </summary>
    private string ApplyHighlightingIfMatched(string line, Func<string, bool> matchPredicate, string matchValue, bool isRegex)
    {
        if (!matchPredicate(line))
        {
            return line;
        }
        
        if (isRegex)
        {
            var regex = new Regex(matchValue, RegexOptions.Compiled);
            return regex.Replace(line, m => $"{AnsiColors.Yellow}{m.Value}{AnsiColors.Reset}");
        }
        else
        {
            return line.Replace(matchValue, $"{AnsiColors.Yellow}{matchValue}{AnsiColors.Reset}");
        }
    }
}