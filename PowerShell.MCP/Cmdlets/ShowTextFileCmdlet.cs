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
                    var displayPath = GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath);
                    WriteObject($"==> {displayPath} <==");
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
        WriteObject($"==> {displayPath} <==");

        // LineRange が null または空の場合はデフォルト (1, int.MaxValue)
        int requestedStart = 1;
        int requestedEnd = int.MaxValue;
        
        if (LineRange != null && LineRange.Length > 0)
        {
            requestedStart = LineRange[0];
            requestedEnd = LineRange.Length > 1 ? LineRange[1] : requestedStart;
            
            // 末尾からの指定 (-10 または -10,-1)
            if (requestedStart < 0)
            {
                ShowTailLines(filePath, encoding, -requestedStart);
                return;
            }
            
            // endLine が 0 または負数の場合は末尾まで
            if (requestedEnd <= 0)
            {
                requestedEnd = int.MaxValue;
            }
        }

        // 通常の範囲指定
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
        // RotateBuffer を使って末尾N行を1passで取得
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
    /// マッチ条件に基づいて行を検索し、前後の文脈と共に表示（真の1パス実装、rotate buffer使用）
    /// </summary>
    private void ShowWithMatch(string inputPath, string filePath, 
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
        
        var displayPath = GetDisplayPath(inputPath, filePath);
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
            
            // Rotate buffer: 前2行を保持（行内容と行番号のペア）
            var preContextBuffer = new RotateBuffer<(string line, int lineNumber)>(2);
            
            // ギャップ候補（後続コンテキスト直後の1行）
            (string line, int lineNumber)? gapLineInfo = null;
            
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
                    
                    // ギャップ処理: gapLineInfo があれば判定して出力
                    if (gapLineInfo.HasValue)
                    {
                        // 前置コンテキストの開始行を計算
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
                        // gapLine の次の行が前置コンテキストの開始なら、gapLine を出力（連続）
                        if (gapInfo.lineNumber + 1 == preContextStart)
                        {
                            string gapDisplay = ApplyHighlightingIfMatched(gapInfo.line, matchPredicate, matchValue, isRegex, reverseOn, reverseOff);
                            WriteObject($"{gapInfo.lineNumber,3}- {gapDisplay}");
                            lastOutputLine = gapInfo.lineNumber;
                        }
                        else if (gapInfo.lineNumber >= preContextStart)
                        {
                            // gapLine が前置コンテキストに含まれる → 前置として出力されるので何もしない
                        }
                        else
                        {
                            // ギャップが2行以上 → 空行で分離
                            WriteObject("");
                        }
                        gapLineInfo = null;
                        needsGapSeparator = false;
                    }
                    else if (needsGapSeparator)
                    {
                        // gapLine がない場合の空行出力
                        WriteObject("");
                        needsGapSeparator = false;
                    }
                    
                    // 前2行を出力（rotate buffer から）
                    foreach (var ctx in preContextBuffer)
                    {
                        if (ctx.lineNumber > lastOutputLine)
                        {
                            string ctxDisplay = ApplyHighlightingIfMatched(ctx.line, matchPredicate, matchValue, isRegex, reverseOn, reverseOff);
                            WriteObject($"{ctx.lineNumber,3}- {ctxDisplay}");
                        }
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
                    gapLineInfo = null; // ギャップリセット
                    needsGapSeparator = false; // フラグもリセット
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
                            gapLineInfo = (currentLine, lineNumber);
                        }
                        else if (lineNumber == lastOutputLine + 2)
                        {
                            // 最後の出力行の次の2行目 → ギャップが2行以上 → 空行出力フラグ
                            needsGapSeparator = true;
                            // gapLineInfo は保持（後で判定に使用）
                        }
                    }
                }
                
                // Rotate buffer更新（行番号とともに保存）
                preContextBuffer.Add((currentLine, lineNumber));
                
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