using System.Management.Automation;
using System.Text.RegularExpressions;
using System.Linq;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイルの内容を行番号付きで表示
/// LLM最適化：行番号は3桁、マッチ行は:でコンテキスト行は-で区別（grep標準）、常にカレントディレクトリからの相対パスを表示
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
            WriteObject($"{currentLine,3}: {line}");
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
        ShowWithMatch(filePath, encoding, line => regex.IsMatch(line), "pattern", Pattern, true);
    }

    private void ShowWithContains(string filePath, System.Text.Encoding encoding)
    {
        ShowWithMatch(filePath, encoding, 
            line => line.Contains(Contains!, StringComparison.Ordinal), 
            "contain", Contains!, false);
    }

    /// <summary>
    /// マッチ条件に基づいて行を検索し、前後の文脈と共に表示
    /// </summary>
    private void ShowWithMatch(
        string filePath, 
        System.Text.Encoding encoding, 
        Func<string, bool> matchPredicate,
        string matchTypeForWarning,
        string matchValue,
        bool isRegex)
    {
        // 行範囲を取得
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // 1st pass: マッチした行番号を収集（LineRange 範囲内のみ効率的に読み込み）
        var matchedLines = new List<int>();

        var linesToSearch = File.ReadLines(filePath, encoding)
            .Skip(startLine - 1)
            .Take(endLine - startLine + 1);

        int lineNumber = startLine;
        bool hasLines = false;

        foreach (var line in linesToSearch)
        {
            hasLines = true;
            if (matchPredicate(line))
            {
                matchedLines.Add(lineNumber);
            }
            lineNumber++;
        }

        // マッチが見つからない場合
        if (matchedLines.Count == 0)
        {
            if (!hasLines && LineRange != null)
            {
                WriteWarning($"Line range {startLine}-{endLine} is beyond file length. No output.");
            }
            else
            {
                string message = matchTypeForWarning == "pattern" 
                    ? $"No lines matched pattern: {matchValue}"
                    : $"No lines contain: {matchValue}";
                WriteWarning(message);
            }
            return;
        }

        // 範囲を計算してマージ（前後2行の文脈）
        var (ranges, gapLines) = CalculateAndMergeRanges(matchedLines, endLine, contextLines: 2);

        // 2nd pass: 範囲ごとに出力（反転表示付き）
        OutputRangesWithContext(filePath, encoding, ranges, gapLines, matchedLines, matchValue, isRegex);
    }

    /// <summary>
    /// 指定された範囲を効率的に出力（ファイルを1回だけ読み込み、マッチ部分を反転表示）
    /// </summary>
    private void OutputRangesWithContext(
        string filePath, 
        System.Text.Encoding encoding, 
        List<(int start, int end)> ranges,
        HashSet<int> gapLines,
        List<int> matchedLines,
        string searchValue,
        bool isRegex)
    {
        var matchedSet = new HashSet<int>(matchedLines);
        int rangeIndex = 0;
        int currentLine = 1;
        bool inRange = false;

        // ANSIエスケープシーケンス（反転表示）
        string reverseOn = $"{(char)27}[7m";
        string reverseOff = $"{(char)27}[0m";

        foreach (var line in File.ReadLines(filePath, encoding))
        {
            // 全ての範囲を処理済みなら終了
            if (rangeIndex >= ranges.Count)
                break;

            var (start, end) = ranges[rangeIndex];

            // 現在の範囲に到達したか？
            if (currentLine >= start && currentLine <= end)
            {
                if (!inRange)
                {
                    // 範囲の最初の行
                    inRange = true;
                }

                string separator = matchedSet.Contains(currentLine) ? ":" : "-";
                
                // マッチ行の場合、マッチ部分を反転表示
                string displayLine = line;
                if (matchedSet.Contains(currentLine))
                {
                    if (isRegex)
                    {
                        // 正規表現の場合
                        var regex = new Regex(searchValue, RegexOptions.Compiled);
                        displayLine = regex.Replace(line, m => $"{reverseOn}{m.Value}{reverseOff}");
                    }
                    else
                    {
                        // Contains（リテラル文字列）の場合
                        displayLine = line.Replace(searchValue, $"{reverseOn}{searchValue}{reverseOff}");
                    }
                }

                WriteObject($"{currentLine,3}{separator} {displayLine}");

                // 範囲の最後の行に到達したら、次の範囲へ
                if (currentLine == end)
                {
                    inRange = false;
                    rangeIndex++;

                    // 次の範囲があれば空行を挿入
                    if (rangeIndex < ranges.Count)
                    {
                        WriteObject("");
                    }
                }
            }

            currentLine++;
        }
    }
}