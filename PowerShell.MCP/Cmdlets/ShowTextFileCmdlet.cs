using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Linq;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイルの内容を行番号付きで表示
/// LLM最適化：行番号は常に4桁、パターンマッチは*でマーク、常にカレントディレクトリからの相対パスを表示
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
    }

    protected override void ProcessRecord()
    {
        // LineRangeバリデーション
        ValidateLineRange(LineRange);

        // ResolveAndValidateFiles は IEnumerable を返すので、遅延評価のまま処理
        var files = ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true);

        foreach (var fileInfo in files)
        {
            try
            {
                // ファイル間の空行（最初のファイル以外）
                if (_totalFilesProcessed > 0)
                {
                    WriteObject("");
                }
                
                // 表示用パスを決定（PS Drive パスを保持）
                var displayPath = GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath);
                WriteObject($"==> {displayPath} <==");
                
                _totalFilesProcessed++;

                var encoding = TextFileUtility.GetEncoding(fileInfo.ResolvedPath, Encoding);

                // 空ファイルの処理
                var fileInfoObj = new FileInfo(fileInfo.ResolvedPath);
                if (fileInfoObj.Length == 0)
                {
                    WriteWarning("File is empty");
                    continue;
                }

                if (!string.IsNullOrEmpty(Pattern))
                {
                    ShowWithPattern(fileInfo.ResolvedPath, encoding);
                }
                else if (!string.IsNullOrEmpty(Contains))
                {
                    ShowWithContains(fileInfo.ResolvedPath, encoding);
                }
                else
                {
                    ShowWithLineRange(fileInfo.ResolvedPath, encoding);
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "ShowTextFileFailed", ErrorCategory.ReadError, fileInfo.ResolvedPath));
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

    private void ShowWithContains(string filePath, System.Text.Encoding encoding)
    {
        // 行範囲を取得
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // Skip/Take で範囲を絞り込んでから文字列検索
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
            // Contains: リテラル文字列として部分一致検索（正規表現ではない）
            if (line.Contains(Contains, StringComparison.Ordinal))
            {
                // LLM向け：行番号フォーマットを統一（4桁）、マッチ行に*マーク
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
            WriteWarning($"No lines contain: {Contains}");
        }
    }
}