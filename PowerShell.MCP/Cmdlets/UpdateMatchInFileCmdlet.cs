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
    /// 文字列リテラル置換または正規表現置換を処理（真の2パス実装）
    /// 1st pass: マッチ行番号収集（軽量、HashSet<int>のみ保持）
    /// 2nd pass: 置換実行 + リアルタイムコンテキスト出力（ストリーミング）
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

            // ===== 1st pass: マッチ行番号収集 =====
            var matchedLines = new HashSet<int>();
            
            using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
            {
                bool hasLines = enumerator.MoveNext();
                if (!hasLines)
                {
                    WriteObject($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made");
                    return;
                }
                
                int lineNumber = 1;
                string currentLine = enumerator.Current;
                bool hasNext = enumerator.MoveNext();
                
                while (true)
                {
                    if (lineNumber >= startLine && lineNumber <= endLine)
                    {
                        bool matched = false;
                        
                        if (isLiteral)
                        {
                            if (currentLine.Contains(Contains!))
                            {
                                matched = true;
                            }
                        }
                        else
                        {
                            if (regex!.IsMatch(currentLine))
                            {
                                matched = true;
                            }
                        }
                        
                        if (matched)
                        {
                            matchedLines.Add(lineNumber);
                        }
                    }
                    
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

            if (matchedLines.Count == 0)
            {
                WriteObject($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made");
                return;
            }

            // ===== 2nd pass: 置換実行 + コンテキスト表示 =====
            var tempFile = System.IO.Path.GetTempFileName();
            int replacementCount = 0;

            try
            {
                replacementCount = ReplaceAndOutputContext(
                    resolvedPath, 
                    tempFile, 
                    originalPath,
                    metadata, 
                    matchedLines, 
                    isLiteral, 
                    regex,
                    startLine,
                    endLine);

                // アトミックに置換
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                // 空行でコンテキストとサマリを分離
                WriteObject("");
                
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
    /// 置換実行 + リアルタイムコンテキスト出力（2nd pass、ストリーミング）
    /// </summary>
    private int ReplaceAndOutputContext(
        string resolvedPath, 
        string tempFile,
        string originalPath, 
        TextFileUtility.FileMetadata metadata,
        HashSet<int> matchedLines,
        bool isLiteral,
        Regex? regex,
        int startLine,
        int endLine)
    {
        int replacementCount = 0;
        var outputLines = new HashSet<int>();  // 出力済み行を追跡
        int lastOutputLine = 0;  // 前回出力した行番号（ギャップ検出用）
        
        string reverseOn = $"{(char)27}[7m";
        string reverseOff = $"{(char)27}[0m";
        
        // ヘッダー出力
        var displayPath = GetDisplayPath(originalPath, resolvedPath);
        WriteObject($"==> {displayPath} <==");
        
        using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(tempFile, false, metadata.Encoding))
        {
            writer.NewLine = metadata.NewlineSequence;
            
            bool hasLines = enumerator.MoveNext();
            if (!hasLines) return 0;
            
            int lineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            
            // Rotate buffer: 前2行を保持（行内容と行番号のペア）
            var preContextBuffer = new RotateBuffer<(string line, int lineNumber)>(2);
            
            // 後続コンテキストカウンタ: マッチ後の2行を収集
            int afterMatchCounter = 0;
            
            while (true)
            {
                bool isMatched = matchedLines.Contains(lineNumber);
                string outputLine = currentLine;
                
                if (isMatched)
                {
                    // ギャップ検出: 前回出力から2行以上離れている場合、空行を挿入
                    if (lastOutputLine > 0 && lineNumber - 2 > lastOutputLine + 1)
                    {
                        WriteObject("");
                    }
                    
                    // 前2行を出力（rotate bufferから、未出力の場合のみ）
                    foreach (var ctx in preContextBuffer)
                    {
                        if (!outputLines.Contains(ctx.lineNumber))
                        {
                            var ctxDisplayLine = BuildContextDisplayLine(ctx.line, isLiteral, regex, reverseOn, reverseOff);
                            WriteObject($"{ctx.lineNumber,3}- {ctxDisplayLine}");
                            outputLines.Add(ctx.lineNumber);
                            lastOutputLine = ctx.lineNumber;
                        }
                    }
                    
                    // 置換実行
                    if (isLiteral)
                    {
                        int count = (currentLine.Length - currentLine.Replace(Contains!, "").Length) / 
                                    Math.Max(1, Contains!.Length);
                        replacementCount += count;
                        outputLine = currentLine.Replace(Contains, Replacement);
                    }
                    else
                    {
                        var matches = regex!.Matches(currentLine);
                        replacementCount += matches.Count;
                        outputLine = regex.Replace(currentLine, Replacement!);
                    }
                    
                    // マッチ行を反転表示して出力
                    string displayLine;
                    if (isLiteral)
                    {
                        if (!string.IsNullOrEmpty(Replacement))
                        {
                            displayLine = outputLine.Replace(Replacement, $"{reverseOn}{Replacement}{reverseOff}");
                        }
                        else
                        {
                            displayLine = outputLine;
                        }
                    }
                    else
                    {
                        displayLine = BuildRegexDisplayLine(currentLine, regex!, Replacement!, reverseOn, reverseOff);
                    }
                    
                    WriteObject($"{lineNumber,3}: {displayLine}");
                    outputLines.Add(lineNumber);
                    lastOutputLine = lineNumber;
                    afterMatchCounter = 2;
                }
                else
                {
                    // 後続コンテキストの出力
                    if (afterMatchCounter > 0 && !outputLines.Contains(lineNumber))
                    {
                        var displayContextLine = BuildContextDisplayLine(currentLine, isLiteral, regex, reverseOn, reverseOff);
                        WriteObject($"{lineNumber,3}- {displayContextLine}");
                        outputLines.Add(lineNumber);
                        lastOutputLine = lineNumber;
                        afterMatchCounter--;
                    }
                }
                
                // ファイルに書き込み
                writer.Write(outputLine);
                
                // Rotate buffer更新（行番号とともに保存）
                preContextBuffer.Add((currentLine, lineNumber));
                
                if (hasNext)
                {
                    writer.Write(metadata.NewlineSequence);
                    lineNumber++;
                    currentLine = enumerator.Current;
                    hasNext = enumerator.MoveNext();
                }
                else
                {
                    break;
                }
            }
            
            if (metadata.HasTrailingNewline)
            {
                writer.Write(metadata.NewlineSequence);
            }
        }
        
        
        return replacementCount;
    }

    /// <summary>
    /// 正規表現置換の反転表示行を構築
    /// </summary>
    private string BuildRegexDisplayLine(string originalLine, Regex regex, string replacement, string reverseOn, string reverseOff)
    {
        // 各マッチの置換結果に反転表示を適用（キャプチャグループ対応）
        var result = regex.Replace(originalLine, match => 
        {
            // match.Result() で $1, $2 などを展開した結果を取得
            var replacedText = match.Result(replacement);
            
            // 空文字列置換（削除）の場合は反転表示しない
            if (string.IsNullOrEmpty(replacedText))
            {
                return replacedText;
            }
            
            // 置換結果全体に反転表示を適用
            return $"{reverseOn}{replacedText}{reverseOff}";
        });
        
        return result;
    }
    /// <summary>
    /// コンテキスト行の表示を構築（マッチがあれば元の文字列を反転表示）
    /// </summary>
    private string BuildContextDisplayLine(string line, bool isLiteral, Regex? regex, string reverseOn, string reverseOff)
    {
        bool hasMatch = false;
        
        if (isLiteral)
        {
            hasMatch = line.Contains(Contains!);
            if (hasMatch)
            {
                return line.Replace(Contains!, $"{reverseOn}{Contains}{reverseOff}");
            }
        }
        else
        {
            hasMatch = regex!.IsMatch(line);
            if (hasMatch)
            {
                // 正規表現のマッチ部分を反転表示（元の文字列のまま）
                return regex.Replace(line, match => $"{reverseOn}{match.Value}{reverseOff}");
            }
        }
        
        return line;
    }




}