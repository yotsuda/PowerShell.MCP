using System.Management.Automation;
using System.Text.RegularExpressions;
using System.IO;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイル内のパターンマッチを更新
/// LLM最適化：文字列リテラル置換と正規表現置換の2つのモード
/// </summary>
[Cmdlet(VerbsData.Update, "MatchInFile", SupportsShouldProcess = true)]
public class UpdateMatchInFileCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter]
    public string? Contains { get; set; }

    [Parameter]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Replacement { get; set; }

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        bool hasLiteral = !string.IsNullOrEmpty(Contains);
        bool hasRegex = !string.IsNullOrEmpty(Pattern);
        
        // どちらも指定されていない
        if (!hasLiteral && !hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Either -Contains/-Replacement or -Pattern/-Replacement must be specified."),
                "ParameterRequired",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // 両方指定されている
        if (hasLiteral && hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Cannot specify both -Contains/-Replacement and -Pattern/-Replacement."),
                "ConflictingParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // Literalモードで片方だけ指定されている
        if (hasLiteral && Replacement == null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -Contains and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // Regexモードで片方だけ指定されている
        if (hasRegex && Replacement == null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -Pattern and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
    }

    protected override void ProcessRecord()
    {
        // LineRangeバリデーション
        ValidateLineRange(LineRange);

        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
        {
            try
            {
                ProcessStringReplacement(fileInfo.InputPath, fileInfo.ResolvedPath);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "UpdateFailed", ErrorCategory.WriteError, fileInfo.ResolvedPath));
            }
        }
    }

    /// <summary>
    /// 文字列リテラル置換または正規表現置換を処理（1パス実装）
    /// </summary>
    private void ProcessStringReplacement(string originalPath, string resolvedPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);
        
        // Replacement に非 ASCII 文字が含まれている場合、エンコーディングを UTF-8 にアップグレード
        if (!string.IsNullOrEmpty(Replacement) && 
            EncodingHelper.TryUpgradeEncodingIfNeeded(metadata, [Replacement], Encoding != null, out var upgradeMessage))
        {
            WriteInformation(upgradeMessage, ["EncodingUpgrade"]);
        }
        
        var isLiteral = !string.IsNullOrEmpty(Contains);
        var regex = isLiteral ? null : new Regex(Pattern!, RegexOptions.Compiled);
        
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // より具体的なアクション説明
        string actionDescription;
        if (isLiteral)
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace '{Contains}' with '{Replacement}'{rangeInfo}";
        }
        else
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace pattern '{Pattern}' with '{Replacement}'{rangeInfo}";
        }

        if (ShouldProcess(resolvedPath, actionDescription))
        {
            // バックアップ
            if (Backup)
            {
                var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
            }

            // 1 pass: 置換実行 + コンテキスト収集
            var tempFile = System.IO.Path.GetTempFileName();
            var matchedLines = new List<int>();
            var contextBuffer = new Dictionary<int, string>();
            int replacementCount = 0;

            // ANSIエスケープシーケンス（反転表示）
            string reverseOn = $"{(char)27}[7m";
            string reverseOff = $"{(char)27}[0m";

            try
            {
                using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
                using (var writer = new StreamWriter(tempFile, false, metadata.Encoding))
                {
                    // 改行コードを保持
                    writer.NewLine = metadata.NewlineSequence;
                    
                    bool hasLines = enumerator.MoveNext();
                    if (!hasLines)
                    {
                        // 空ファイル（通常は起こらない）
                        File.Delete(tempFile);
                        WriteObject($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made");
                        return;
                    }
                    
                    int lineNumber = 1;
                    string currentLine = enumerator.Current;
                    bool hasNext = enumerator.MoveNext();
                    
                    // Rotate buffer: 前2行を保持（コンテキスト表示用）
                    string? prevPrevLine = null;
                    string? prevLine = null;
                    
                    // 後続コンテキストカウンタ: マッチ後の2行を収集
                    int afterMatchCounter = 0;
                    
                    while (true)
                    {
                        bool matched = false;
                        string? newLine = null;
                        
                        if (lineNumber >= startLine && lineNumber <= endLine)
                        {
                            if (isLiteral)
                            {
                                if (currentLine.Contains(Contains!))
                                {
                                    matched = true;
                                    
                                    // 置換回数カウント
                                    int count = (currentLine.Length - currentLine.Replace(Contains!, "").Length) / 
                                                Math.Max(1, Contains!.Length);
                                    replacementCount += count;
                                    
                                    // 置換実行
                                    newLine = currentLine.Replace(Contains, Replacement);
                                }
                            }
                            else
                            {
                                var matches = regex!.Matches(currentLine);
                                if (matches.Count > 0)
                                {
                                    matched = true;
                                    replacementCount += matches.Count;
                                    
                                    // 置換実行
                                    newLine = regex.Replace(currentLine, Replacement!);
                                }
                            }
                        }
                        
                        if (matched)
                        {
                            // 前2行をコンテキストバッファに追加（rotate bufferから）
                            if (prevPrevLine != null)
                            {
                                contextBuffer[lineNumber - 2] = prevPrevLine;
                            }
                            if (prevLine != null)
                            {
                                contextBuffer[lineNumber - 1] = prevLine;
                            }
                            
                            // 反転表示データを構築
                            string displayLine;
                            if (isLiteral)
                            {
                                // 空文字列置換（削除）の場合は反転表示する対象がないので、置換後の行をそのまま使用
                                if (!string.IsNullOrEmpty(Replacement))
                                {
                                    displayLine = newLine!.Replace(Replacement, $"{reverseOn}{Replacement}{reverseOff}");
                                }
                                else
                                {
                                    displayLine = newLine!;
                                }
                            }
                            else
                            {
                                displayLine = BuildRegexDisplayLine(currentLine, regex!, Replacement!, reverseOn, reverseOff);
                            }
                            
                            // マッチ行をコンテキストバッファに追加
                            contextBuffer[lineNumber] = displayLine;
                            matchedLines.Add(lineNumber);
                            
                            // 後続2行のカウンタをセット
                            afterMatchCounter = 2;
                            
                            // 置換後の行を書き込み
                            writer.Write(newLine);
                        }
                        else
                        {
                            // 後続コンテキストの収集
                            if (afterMatchCounter > 0)
                            {
                                contextBuffer[lineNumber] = currentLine;
                                afterMatchCounter--;
                            }
                            
                            // 元の行を書き込み
                            writer.Write(currentLine);
                        }
                        
                        // Rotate buffer更新（置換前の元の行を保存）
                        prevPrevLine = prevLine;
                        prevLine = currentLine;
                        
                        // 次の行があるかチェック
                        if (hasNext)
                        {
                            writer.Write(metadata.NewlineSequence);
                            lineNumber++;
                            currentLine = enumerator.Current;
                            hasNext = enumerator.MoveNext();
                        }
                        else
                        {
                            // 最終行に到達
                            break;
                        }
                    }
                    
                    // 元のファイルに末尾改行があれば保持
                    if (metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                }

                if (matchedLines.Count == 0)
                {
                    WriteObject($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made");
                    File.Delete(tempFile);
                    return;
                }

                // アトミックに置換
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                // コンテキスト表示（バッファから、ファイル再読込なし）
                OutputReplacementContextFromBuffer(originalPath, resolvedPath, contextBuffer, matchedLines);

                WriteObject($"Updated {GetDisplayPath(originalPath, resolvedPath)}: {replacementCount} replacement(s) made");
            }
            catch
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
                throw;
            }
        }
    }

    /// <summary>
    /// 正規表現置換の反転表示行を構築
    /// </summary>
    private string BuildRegexDisplayLine(string originalLine, Regex regex, string replacement, string reverseOn, string reverseOff)
    {
        var matches = regex.Matches(originalLine);
        var result = regex.Replace(originalLine, replacement);
        
        // 各マッチの置換結果に反転表示を適用
        // 簡易実装: Replacementが単純な文字列の場合のみ反転表示
        // キャプチャグループを含む場合は複雑なので、置換結果全体を反転表示しない
        // 空文字列置換（削除）の場合は反転表示する対象がないので、そのまま返す
        if (!string.IsNullOrEmpty(replacement) && !replacement.Contains("$"))
        {
            result = result.Replace(replacement, $"{reverseOn}{replacement}{reverseOff}");
        }
        
        return result;
    }

    /// <summary>
    /// マッチした行とその前後のコンテキストを表示（バッファから、ファイル再読込なし）
    /// </summary>
    private void OutputReplacementContextFromBuffer(string originalPath, string resolvedPath, Dictionary<int, string> contextBuffer, List<int> matchedLines)
    {
        if (matchedLines.Count == 0) return;

        var matchedSet = new HashSet<int>(matchedLines);
        
        // contextBufferのキーをソート
        var sortedLineNumbers = contextBuffer.Keys.OrderBy(x => x).ToList();
        
        // 範囲を計算してマージ（前後2行の文脈）
        var ranges = CalculateAndMergeRangesFromBuffer(sortedLineNumbers, contextLines: 2);
        
        // 表示用パスを決定
        var displayPath = GetDisplayPath(originalPath, resolvedPath);
        WriteObject($"==> {displayPath} <==");
        
        // 範囲ごとに出力
        for (int rangeIndex = 0; rangeIndex < ranges.Count; rangeIndex++)
        {
            var (start, end) = ranges[rangeIndex];
            
            for (int lineNumber = start; lineNumber <= end; lineNumber++)
            {
                if (contextBuffer.ContainsKey(lineNumber))
                {
                    string separator = matchedSet.Contains(lineNumber) ? ":" : "-";
                    WriteObject($"{lineNumber,3}{separator} {contextBuffer[lineNumber]}");
                }
            }
            
            // 次の範囲との間に空行
            if (rangeIndex < ranges.Count - 1)
            {
                WriteObject("");
            }
        }
    }

    /// <summary>
    /// バッファから範囲を計算してマージ
    /// </summary>
    private List<(int start, int end)> CalculateAndMergeRangesFromBuffer(List<int> lineNumbers, int contextLines)
    {
        var ranges = new List<(int start, int end)>();
        if (lineNumbers.Count == 0) return ranges;

        int currentStart = lineNumbers[0];
        int currentEnd = lineNumbers[0];

        for (int i = 1; i < lineNumbers.Count; i++)
        {
            int lineNumber = lineNumbers[i];
            
            // 現在の範囲と隣接または重複している場合、範囲を拡張
            if (lineNumber <= currentEnd + 1)
            {
                currentEnd = lineNumber;
            }
            else
            {
                // 範囲を確定
                ranges.Add((currentStart, currentEnd));
                currentStart = lineNumber;
                currentEnd = lineNumber;
            }
        }
        
        // 最後の範囲を追加
        ranges.Add((currentStart, currentEnd));

        return ranges;
    }


}