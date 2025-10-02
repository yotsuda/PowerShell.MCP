using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイルの内容を行番号付きで表示
/// LLM最適化：行番号は常に4桁、パターンマッチは*でマーク、常にカレントディレクトリからの相対パスを表示
/// </summary>
[Cmdlet(VerbsCommon.Show, "TextFile")]
public class ShowTextFileCmdlet : TextFileCmdletBase
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    private int _totalFilesProcessed = 0;

    protected override void ProcessRecord()
    {
        // LineRangeバリデーション（最優先）
        ValidateLineRange(LineRange);

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
                        new FileNotFoundException($"File not found: {path}"),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        resolvedPath));
                    continue;
                }

                try
                {
                    // ファイル間の空行（最初のファイル以外）
                    if (_totalFilesProcessed > 0)
                    {
                        WriteObject("");
                    }
                    
                    // 表示用パスを決定（PS Drive パスを保持）
                    var displayPath = GetDisplayPath(path, resolvedPath);
                    
                    // ヘッダーとして表示
                    WriteObject($"==> {displayPath} <==");
                    
                    _totalFilesProcessed++;

                    var encoding = TextFileUtility.GetEncoding(resolvedPath, Encoding);

                    // 空ファイルの処理
                    var fileInfo = new FileInfo(resolvedPath);
                    if (fileInfo.Length == 0)
                    {
                        WriteWarning("File is empty");
                        continue;
                    }

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
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // Skip/Take で必要な範囲だけを取得（LINQの遅延評価で効率的）
        int skipCount = startLine - 1;
        int takeCount = endLine - startLine + 1;

        var lines = File.ReadLines(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        int currentLine = startLine;
        bool hasOutput = false;

        foreach (var line in lines)
        {
            WriteObject($"{currentLine,4}: {line}");
            currentLine++;
            hasOutput = true;
        }

        // 1行も出力されなかった場合のみメッセージ
        if (!hasOutput && LineRange != null)
        {
            WriteWarning($"Line range {startLine}-{endLine} is beyond file length. No output.");
        }
    }

    private void ShowWithPattern(string filePath, System.Text.Encoding encoding)
    {
        var regex = new Regex(Pattern, RegexOptions.Compiled);

        // 行範囲を取得
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // Skip/Take で範囲を絞り込んでからパターンマッチ
        int skipCount = startLine - 1;
        int takeCount = endLine - startLine + 1;

        var linesToSearch = File.ReadLines(filePath, encoding)
            .Skip(skipCount)
            .Take(takeCount);

        int matchCount = 0;
        int lineNumber = startLine;
        bool hasLines = false;

        foreach (var line in linesToSearch)
        {
            hasLines = true;
            if (regex.IsMatch(line))
            {
                // LLM向け：行番号フォーマットを統一（4桁）
                WriteObject($"*{lineNumber,3}: {line}");
                matchCount++;
            }
            lineNumber++;
        }

        // 指定範囲が存在しない場合
        if (!hasLines && LineRange != null)
        {
            WriteWarning($"Line range {startLine}-{endLine} is beyond file length. No output.");
        }
        // マッチが見つからない場合
        else if (matchCount == 0)
        {
            WriteWarning($"No lines matched pattern: {Pattern}");
        }
    }
}