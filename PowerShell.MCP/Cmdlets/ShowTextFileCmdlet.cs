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
        
        // ヘッダー出力
        var displayPath = GetDisplayPath(filePath, filePath);
        WriteObject($"==> {displayPath} <==");

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
    /// マッチ条件に基づいて行を検索し、前後の文脈と共に表示（真の1パス実装、rotate buffer使用）
    /// </summary>
    private void ShowWithMatch(
        string filePath, 
        System.Text.Encoding encoding, 
        Func<string, bool> matchPredicate,
        string matchTypeForWarning,
        string matchValue,
        bool isRegex)
    {
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
        
        // ANSIエスケープシーケンス（反転表示）
        string reverseOn = $"{(char)27}[7m";
        string reverseOff = $"{(char)27}[0m";
        
        var displayPath = GetDisplayPath(filePath, filePath);
        bool headerPrinted = false;
        bool anyMatch = false;
        
        using (var enumerator = File.ReadLines(filePath, encoding).GetEnumerator())
        {
            if (!enumerator.MoveNext())
            {
                // 空ファイル
                WriteWarning("File is empty. No output.");
                return;
            }
            
            int lineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            
            // Rotate buffer: 前2行 + ギャップ候補1行
            string? prevPrevLine = null;
            string? prevLine = null;
            string? gapLine = null;
            
            // 後続コンテキストカウンタ
            int afterMatchCounter = 0;
            
            // 最後に出力した行番号（ギャップ検出用）
            int lastOutputLine = 0;
            
            // ギャップ検出: 空行出力フラグ（次のマッチが見つかったときに出力）
            bool needsGapSeparator = false;
            
            while (true)
            {
                // マッチ判定は元の LineRange 内のみで行う
                bool matched = (lineNumber >= startLine && lineNumber <= endLine) && matchPredicate(currentLine);
                
                if (matched)
                {
                    anyMatch = true;
                    
                    // ヘッダー出力（初回のみ）
                    if (!headerPrinted)
                    {
                        WriteObject($"==> {displayPath} <==");
                        headerPrinted = true;
                    }
                    
                    // ギャップがあれば空行を出力
                    if (needsGapSeparator)
                    {
                        WriteObject("");
                        needsGapSeparator = false;
                    }
                    
                    // ギャップがあれば gapLine を出力（ギャップが1行だけなので結合）
                    if (gapLine != null)
                    {
                        string gapDisplay = ApplyHighlightingIfMatched(gapLine, matchPredicate, matchValue, isRegex, reverseOn, reverseOff);
                        WriteObject($"{lastOutputLine + 1,3}- {gapDisplay}");
                        gapLine = null;
                    }
                    
                    // 前2行を出力（rotate buffer から）
                    if (prevPrevLine != null && lineNumber >= 3 && lineNumber - 2 > lastOutputLine)
                    {
                        string prevPrevDisplay = ApplyHighlightingIfMatched(prevPrevLine, matchPredicate, matchValue, isRegex, reverseOn, reverseOff);
                        WriteObject($"{lineNumber - 2,3}- {prevPrevDisplay}");
                    }
                    if (prevLine != null && lineNumber >= 2 && lineNumber - 1 > lastOutputLine)
                    {
                        string prevDisplay = ApplyHighlightingIfMatched(prevLine, matchPredicate, matchValue, isRegex, reverseOn, reverseOff);
                        WriteObject($"{lineNumber - 1,3}- {prevDisplay}");
                    }
                    
                    // マッチ行を出力（反転表示）
                    string displayLine;
                    if (isRegex)
                    {
                        var regex = new Regex(matchValue, RegexOptions.Compiled);
                        displayLine = regex.Replace(currentLine, m => $"{reverseOn}{m.Value}{reverseOff}");
                    }
                    else
                    {
                        displayLine = currentLine.Replace(matchValue, $"{reverseOn}{matchValue}{reverseOff}");
                    }
                    WriteObject($"{lineNumber,3}: {displayLine}");
                    
                    afterMatchCounter = 2;
                    lastOutputLine = lineNumber;
                    gapLine = null; // ギャップリセット
                }
                else
                {
                    // 後続コンテキストの出力
                    if (afterMatchCounter > 0)
                    {
                        string display = ApplyHighlightingIfMatched(currentLine, matchPredicate, matchValue, isRegex, reverseOn, reverseOff);
                        WriteObject($"{lineNumber,3}- {display}");
                        afterMatchCounter--;
                        lastOutputLine = lineNumber;
                    }
                    else if (lastOutputLine > 0)
                    {
                        // ギャップ検出モード
                        if (lineNumber == lastOutputLine + 1)
                        {
                            // 最後の出力行の次の1行目 → ギャップ候補として保持
                            gapLine = currentLine;
                        }
                        else if (lineNumber == lastOutputLine + 2)
                        {
                            // 最後の出力行の次の2行目 → ギャップが2行以上 → 空行出力フラグ
                            needsGapSeparator = true;
                            gapLine = null;
                            lastOutputLine = 0; // ギャップ検出終了
                        }
                    }
                }
                
                // Rotate buffer更新（元の行を保存）
                prevPrevLine = prevLine;
                prevLine = currentLine;
                
                // 次へ
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
        
        // マッチが見つからない場合
        if (!anyMatch)
        {
            string message = matchTypeForWarning == "pattern" 
                ? $"No lines matched pattern: {matchValue}"
                : $"No lines contain: {matchValue}";
            WriteWarning(message);
        }
    }

    /// <summary>
    /// 行にマッチが含まれる場合、反転表示を適用する
    /// </summary>
    private string ApplyHighlightingIfMatched(string line, Func<string, bool> matchPredicate, string matchValue, bool isRegex, string reverseOn, string reverseOff)
    {
        if (!matchPredicate(line))
        {
            return line;
        }
        
        if (isRegex)
        {
            var regex = new Regex(matchValue, RegexOptions.Compiled);
            return regex.Replace(line, m => $"{reverseOn}{m.Value}{reverseOff}");
        }
        else
        {
            return line.Replace(matchValue, $"{reverseOn}{matchValue}{reverseOff}");
        }
    }
}