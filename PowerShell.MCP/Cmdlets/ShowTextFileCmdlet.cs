using System.Management.Automation;
using System.Text;
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
    public SwitchParameter Recurse { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    private bool _lastFileHadOutput = false;
    private Regex? _compiledRegex = null;


    protected override void BeginProcessing()
    {
        // Contains and Pattern can be combined (OR condition) for Show-TextFile

        // -Recurse requires -Pattern or -Contains
        if (Recurse && string.IsNullOrEmpty(Pattern) && string.IsNullOrEmpty(Contains))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("-Recurse requires -Pattern or -Contains parameter."),
                "RecurseRequiresPattern",
                ErrorCategory.InvalidArgument,
                null));
        }

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

        // Pre-compile regex for performance (used across all files)
        // Combine Contains and Pattern with OR if both specified
        if (!string.IsNullOrEmpty(Pattern) || !string.IsNullOrEmpty(Contains))
        {
            string combinedPattern;
            if (!string.IsNullOrEmpty(Contains) && !string.IsNullOrEmpty(Pattern))
            {
                // Both specified: OR condition (escaped Contains | Pattern)
                combinedPattern = Regex.Escape(Contains) + "|" + Pattern;
            }
            else if (!string.IsNullOrEmpty(Pattern))
            {
                combinedPattern = Pattern;
            }
            else
            {
                combinedPattern = Regex.Escape(Contains!);
            }
            _compiledRegex = new Regex(combinedPattern, RegexOptions.Compiled);
        }
    }

    protected override void ProcessRecord()
    {
        // LineRange validation
        ValidateLineRange(LineRange);

        // Get files to process
        IEnumerable<ResolvedFileInfo> files;
        if (Recurse)
        {
            files = EnumerateFilesRecursively(Path, LiteralPath);
        }
        else
        {
            files = ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true);
        }

        foreach (var fileInfo in files)
        {
            try
            {
                // Blank line between files (only if last file had output)
                if (_lastFileHadOutput)
                {
                    WriteObject("");
                    _lastFileHadOutput = false;
                }

                // Get encoding: if specified use it, otherwise null (auto-detect BOM in StreamReader)
                System.Text.Encoding? encoding = string.IsNullOrEmpty(Encoding) 
                    ? null 
                    : EncodingHelper.GetEncodingForReading(fileInfo.ResolvedPath, Encoding);

                // Handle empty file - skip silently when Pattern/Contains is specified
                var fileInfoObj = new FileInfo(fileInfo.ResolvedPath);
                if (fileInfoObj.Length == 0)
                {
                    if (string.IsNullOrEmpty(Pattern) && string.IsNullOrEmpty(Contains))
                    {
                        var displayPath = GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath);
                        WriteObject(AnsiColors.Header(displayPath));
                        WriteWarning("File is empty");
                        _lastFileHadOutput = true;
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(Pattern) || !string.IsNullOrEmpty(Contains))
                {
                    // Both Pattern and Contains use _compiledRegex (combined with OR if both specified)
                    _lastFileHadOutput = ShowWithPattern(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                }
                else
                {
                    ShowWithLineRange(fileInfo.InputPath, fileInfo.ResolvedPath, encoding);
                    _lastFileHadOutput = true;
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "ShowTextFileFailed", ErrorCategory.ReadError, fileInfo.ResolvedPath));
            }
        }
    }


    private void ShowWithLineRange(string inputPath, string filePath, System.Text.Encoding? encoding)
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

        var lines = (encoding != null ? File.ReadLines(filePath, encoding) : File.ReadLines(filePath))
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

    private void ShowTailLines(string filePath, System.Text.Encoding? encoding, int tailCount)
    {
        // Get last N lines in single pass using RotateBuffer
        var buffer = new RotateBuffer<(string line, int lineNumber)>(tailCount);

        int lineNumber = 1;
        foreach (var line in (encoding != null ? File.ReadLines(filePath, encoding) : File.ReadLines(filePath)))
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

    private bool ShowWithPattern(string inputPath, string filePath, System.Text.Encoding? encoding)
    {
        return ShowWithMatch(inputPath, filePath, encoding, line => _compiledRegex!.IsMatch(line), Pattern ?? "", _compiledRegex);
    }

    private bool ShowWithContains(string inputPath, string filePath, System.Text.Encoding? encoding)
    {
        return ShowWithMatch(inputPath, filePath, encoding, line => line.Contains(Contains!, StringComparison.Ordinal), Contains!, null);
    }

    /// <summary>
    /// Searches lines based on match conditions and displays with context (true single-pass implementation using rotate buffer)
    /// Returns true if any matches were found
    /// </summary>
    private bool ShowWithMatch(string inputPath, string filePath,
        System.Text.Encoding? encoding,
        Func<string, bool> matchPredicate,
        string matchValue,
        Regex? regex)
    {
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
        var displayPath = GetDisplayPath(inputPath, filePath);
        var outputBuffer = new List<string>();
        bool anyMatch = false;

        // Use encoding if specified, otherwise default (UTF-8 with BOM auto-detection)
        var lines = encoding != null 
            ? File.ReadLines(filePath, encoding) 
            : File.ReadLines(filePath);
        
        using (var enumerator = lines.GetEnumerator())
        {
            if (!enumerator.MoveNext())
            {
                // Empty file
                return false;
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
                Match? currentMatch = null;
                bool matched = false;
                if (lineNumber >= startLine && lineNumber <= endLine)
                {
                    if (regex != null)
                    {
                        currentMatch = regex.Match(currentLine);
                        matched = currentMatch.Success;
                    }
                    else
                    {
                        matched = matchPredicate(currentLine);
                    }
                }

                if (matched)
                {
                    anyMatch = true;

                    // Output header (first time only)
                    if (outputBuffer.Count == 0)
                    {
                        outputBuffer.Add(AnsiColors.Header(displayPath));
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
                            string gapDisplay = ApplyHighlighting(gapInfo.line, regex, regex == null ? matchValue : null);
                            outputBuffer.Add($"{gapInfo.lineNumber,3}- {gapDisplay}");
                            lastOutputLine = gapInfo.lineNumber;
                        }
                        else if (gapInfo.lineNumber >= preContextStart)
                        {
                            // gapLine is included in leading context -> will be output as leading context, do nothing
                        }
                        else
                        {
                            // Gap is 2+ lines -> separate with empty line
                            outputBuffer.Add("");
                        }
                        gapLineInfo = null;
                        needsGapSeparator = false;
                    }
                    else if (needsGapSeparator)
                    {
                        // Empty line output when no gapLine
                        outputBuffer.Add("");
                        needsGapSeparator = false;
                    }

                    // Output previous 2 lines (from rotate buffer)
                    foreach (var ctx in preContextBuffer)
                    {
                        if (ctx.lineNumber > lastOutputLine)
                        {
                            string ctxDisplay = ApplyHighlighting(ctx.line, regex, regex == null ? matchValue : null);
                            outputBuffer.Add($"{ctx.lineNumber,3}- {ctxDisplay}");
                        }
                    }

                    // Output match line (highlighted) - use cached match if available
                    string displayLine = currentMatch != null 
                        ? ApplyHighlightingWithMatch(currentLine, currentMatch)
                        : ApplyHighlighting(currentLine, regex, matchValue);
                    outputBuffer.Add($"{lineNumber,3}: {displayLine}");

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
                        string display = ApplyHighlighting(currentLine, regex, regex == null ? matchValue : null);
                        outputBuffer.Add($"{lineNumber,3}- {display}");
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

        // Batch output for better performance
        if (outputBuffer.Count > 0)
        {
            WriteObject(outputBuffer, enumerateCollection: true);
        }

        return anyMatch;
    }
    /// <summary>
    /// Applies yellow highlighting using index-based approach (faster than Regex.Replace)
    /// </summary>
    private string ApplyHighlighting(string line, Regex? regex, string? containsValue)
    {
        if (regex != null)
        {
            var match = regex.Match(line);
            if (!match.Success) return line;

            var sb = new StringBuilder(line.Length + 32);
            int lastEnd = 0;
            while (match.Success)
            {
                sb.Append(line, lastEnd, match.Index - lastEnd);
                sb.Append(AnsiColors.Yellow);
                sb.Append(match.Value);
                sb.Append(AnsiColors.Reset);
                lastEnd = match.Index + match.Length;
                match = match.NextMatch();
            }
            sb.Append(line, lastEnd, line.Length - lastEnd);
            return sb.ToString();
        }
        else if (containsValue != null)
        {
            int index = line.IndexOf(containsValue, StringComparison.Ordinal);
            if (index < 0) return line;

            var sb = new StringBuilder(line.Length + 32);
            int lastEnd = 0;
            while (index >= 0)
            {
                sb.Append(line, lastEnd, index - lastEnd);
                sb.Append(AnsiColors.Yellow);
                sb.Append(containsValue);
                sb.Append(AnsiColors.Reset);
                lastEnd = index + containsValue.Length;
                index = line.IndexOf(containsValue, lastEnd, StringComparison.Ordinal);
            }
            sb.Append(line, lastEnd, line.Length - lastEnd);
            return sb.ToString();
        }
        return line;
    }

    /// <summary>
    /// Applies yellow highlighting using pre-computed Match object (avoids re-matching)
    /// </summary>
    private string ApplyHighlightingWithMatch(string line, Match match)
    {
        if (!match.Success) return line;

        var sb = new StringBuilder(line.Length + 32);
        int lastEnd = 0;
        while (match.Success)
        {
            sb.Append(line, lastEnd, match.Index - lastEnd);
            sb.Append(AnsiColors.Yellow);
            sb.Append(match.Value);
            sb.Append(AnsiColors.Reset);
            lastEnd = match.Index + match.Length;
            match = match.NextMatch();
        }
        sb.Append(line, lastEnd, line.Length - lastEnd);
        return sb.ToString();
    }

    /// <summary>
    /// Enumerates files recursively from directories or returns files directly
    /// </summary>
    private IEnumerable<ResolvedFileInfo> EnumerateFilesRecursively(string[]? path, string[]? literalPath)
    {
        string[] inputPaths = path ?? literalPath ?? [];

        foreach (var inputPath in inputPaths)
        {
            string resolvedPath;
            try
            {
                resolvedPath = GetUnresolvedProviderPathFromPSPath(inputPath);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "PathResolutionFailed", ErrorCategory.InvalidArgument, inputPath));
                continue;
            }

            if (Directory.Exists(resolvedPath))
            {
                // Directory: enumerate files recursively
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(resolvedPath, "*", SearchOption.AllDirectories);
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "DirectoryEnumerationFailed", ErrorCategory.ReadError, resolvedPath));
                    continue;
                }

                foreach (var filePath in files)
                {
                    yield return new ResolvedFileInfo
                    {
                        InputPath = inputPath,
                        ResolvedPath = filePath,
                        IsNewFile = false
                    };
                }
            }
            else if (File.Exists(resolvedPath))
            {
                // File: return directly
                yield return new ResolvedFileInfo
                {
                    InputPath = inputPath,
                    ResolvedPath = resolvedPath,
                    IsNewFile = false
                };
            }
            else
            {
                WriteError(new ErrorRecord(
                    new FileNotFoundException($"Path not found: {inputPath}"),
                    "PathNotFound",
                    ErrorCategory.ObjectNotFound,
                    inputPath));
            }
        }
    }
}
