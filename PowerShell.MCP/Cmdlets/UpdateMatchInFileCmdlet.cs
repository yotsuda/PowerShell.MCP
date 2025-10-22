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
    /// 文字列リテラル置換または正規表現置換を処理（2パス）
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

            // 1st pass: 置換実行 + メタデータ収集
            var tempFile = System.IO.Path.GetTempFileName();
            var matchedLines = new List<int>();
            var displayData = new Dictionary<int, string>();
            int replacementCount = 0;
            int totalLines = 0;

            // ANSIエスケープシーケンス（反転表示）
            string reverseOn = $"{(char)27}[7m";
            string reverseOff = $"{(char)27}[0m";

            try
            {
                using (var reader = new StreamReader(resolvedPath, metadata.Encoding))
                using (var writer = new StreamWriter(tempFile, false, metadata.Encoding))
                {
                    // 改行コードを保持
                    writer.NewLine = metadata.NewlineSequence;
                    
                    int lineNumber = 1;
                    string? line;
                    
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (lineNumber >= startLine && lineNumber <= endLine)
                        {
                            if (isLiteral)
                            {
                                if (line.Contains(Contains!))
                                {
                                    matchedLines.Add(lineNumber);
                                    
                                    // 置換回数カウント
                                    int count = (line.Length - line.Replace(Contains!, "").Length) / 
                                                Math.Max(1, Contains!.Length);
                                    replacementCount += count;
                                    
                                    // 置換実行
                                    var newLine = line.Replace(Contains, Replacement);
                                    
                                    // 反転表示データを構築（置換後の文字列に反転表示を適用）
                                    // 空文字列置換（削除）の場合は反転表示する対象がないので、置換後の行をそのまま使用
                                    string displayLine;
                                    if (!string.IsNullOrEmpty(Replacement))
                                    {
                                        displayLine = newLine.Replace(Replacement, $"{reverseOn}{Replacement}{reverseOff}");
                                    }
                                    else
                                    {
                                        displayLine = newLine;
                                    }
                                    displayData[lineNumber] = displayLine;
                                    
                                    writer.WriteLine(newLine);
                                    lineNumber++;
                                    continue;
                                }
                            }
                            else
                            {
                                var matches = regex!.Matches(line);
                                if (matches.Count > 0)
                                {
                                    matchedLines.Add(lineNumber);
                                    replacementCount += matches.Count;
                                    
                                    // 置換実行
                                    var newLine = regex.Replace(line, Replacement!);
                                    
                                    // 反転表示データを構築
                                    // 正規表現の場合、置換後の各マッチ位置を計算して反転表示を適用
                                    var displayLine = BuildRegexDisplayLine(line, regex, Replacement!, reverseOn, reverseOff);
                                    displayData[lineNumber] = displayLine;
                                    
                                    writer.WriteLine(newLine);
                                    lineNumber++;
                                    continue;
                                }
                            }
                        }
                        
                        writer.WriteLine(line);
                        lineNumber++;
                    }
                    
                    totalLines = lineNumber - 1;  // 総行数を記録
                }

                if (matchedLines.Count == 0)
                {
                    WriteObject($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made");
                    File.Delete(tempFile);
                    return;
                }

                // アトミックに置換
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                // 2nd pass: コンテキスト表示
                OutputReplacementContext(resolvedPath, metadata.Encoding, matchedLines, displayData, totalLines);

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
    /// マッチした行とその前後のコンテキストを表示
    /// </summary>
    private void OutputReplacementContext(string filePath, System.Text.Encoding encoding, List<int> matchedLines, Dictionary<int, string> displayData, int totalLines)
    {
        if (matchedLines.Count == 0) return;

        var matchedSet = new HashSet<int>(matchedLines);
        
        
        // 範囲を計算してマージ（前後2行の文脈）
        var (ranges, gapLines) = CalculateAndMergeRanges(matchedLines, totalLines, contextLines: 2);
        
        // 表示用パスを決定
        var displayPath = GetDisplayPath(filePath, filePath);
        WriteObject($"==> {displayPath} <==");
        
        // 範囲ごとに出力
        int rangeIndex = 0;
        int currentLine = 1;
        
        foreach (var line in File.ReadLines(filePath, encoding))
        {
            if (rangeIndex >= ranges.Count)
                break;
                
            var (start, end) = ranges[rangeIndex];
            
            if (currentLine >= start && currentLine <= end)
            {
                string separator = matchedSet.Contains(currentLine) ? ":" : "-";
                
                string displayLine;
                if (displayData.ContainsKey(currentLine))
                {
                    // 置換行は反転表示データを使用
                    displayLine = displayData[currentLine];
                }
                else
                {
                    // コンテキスト行はそのまま
                    displayLine = line;
                }
                
                WriteObject($"{currentLine,3}{separator} {displayLine}");
                
                if (currentLine == end)
                {
                    rangeIndex++;
                    if (rangeIndex < ranges.Count)
                    {
                        // 次の範囲との間に空行
                        WriteObject("");
                    }
                }
            }
            
            currentLine++;
        }
    }

}