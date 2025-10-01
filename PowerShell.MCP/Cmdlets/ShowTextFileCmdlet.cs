using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイルの内容を行番号付きで表示
/// LLM最適化：行番号は常に4桁、パターンマッチは*でマーク
/// </summary>
[Cmdlet(VerbsCommon.Show, "TextFile", DefaultParameterSetName = "Default")]
public class ShowTextFileCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "Default")]
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "LineRange")]
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = "Pattern")]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "Default")]
    [Parameter(ParameterSetName = "LineRange")]
    [Parameter(ParameterSetName = "Pattern")]
    public int[]? LineRange { get; set; }

    [Parameter(ParameterSetName = "Pattern", Mandatory = true)]
    public string Pattern { get; set; } = null!;


    protected override void ProcessRecord()
    {
        foreach (var path in Path)
        {
            System.Collections.ObjectModel.Collection<string> resolvedPaths;
            
            try
            {
                resolvedPaths = GetResolvedProviderPathFromPSPath(path, out _);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "PathResolutionFailed",
                    ErrorCategory.InvalidArgument,
                    path));
                continue;
            }
            
            foreach (var resolvedPath in resolvedPaths)
            {
                if (!File.Exists(resolvedPath))
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException($"File not found: {resolvedPath}"),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolvedPath));
                    continue;
                }

                try
                {
                    var encoding = TextFileUtility.DetectEncoding(resolvedPath);

                    if (!string.IsNullOrEmpty(Pattern))
                    {
                        ShowWithPattern(resolvedPath, encoding);
                    }
                    else
                    {
                        ShowWithLineRange(resolvedPath, encoding);
                    }
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "ShowTextFileFailed", ErrorCategory.ReadError, resolvedPath));
                }
            }
        }
    }

    private void ShowWithLineRange(string filePath, System.Text.Encoding encoding)
    {
        int startLine = 1;
        int endLine = int.MaxValue;

        if (LineRange != null && LineRange.Length > 0)
        {
            startLine = LineRange[0];
            endLine = LineRange.Length > 1 ? LineRange[1] : startLine;
        }

        // Skip/Take で必要な範囲だけを取得（LINQの遅延評価で効率的）
        int skipCount = startLine - 1;
        int takeCount = endLine - startLine + 1;

        var lines = File.ReadLines(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        int currentLine = startLine;
        foreach (var line in lines)
        {
            WriteObject($"{currentLine,4}: {line}");
            currentLine++;
        }
    }

    private void ShowWithPattern(string filePath, System.Text.Encoding encoding)
    {
        var regex = new Regex(Pattern);

        // 行範囲を取得
        int startLine = 1;
        int endLine = int.MaxValue;
        if (LineRange != null && LineRange.Length > 0)
        {
            startLine = LineRange[0];
            endLine = LineRange.Length > 1 ? LineRange[1] : startLine;
        }

        // Skip/Take で範囲を絞り込んでからパターンマッチ
        int skipCount = startLine - 1;
        int takeCount = endLine - startLine + 1;

        var linesToSearch = File.ReadLines(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        int matchCount = 0;
        int lineNumber = startLine;

        foreach (var line in linesToSearch)
        {
            if (regex.IsMatch(line))
            {
                // LLM向け：行番号フォーマットを統一（4桁）
                WriteObject($"*{lineNumber,3}: {line}");
                matchCount++;
            }
            lineNumber++;
        }

        // マッチが見つからない場合は警告
        if (matchCount == 0)
        {
            WriteWarning($"No lines matched pattern: {Pattern}");
        }
    }
}
