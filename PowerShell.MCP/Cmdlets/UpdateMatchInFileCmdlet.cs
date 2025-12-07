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

        // Contains に改行が含まれている場合はエラー（行単位で処理するため）
        if (hasLiteral && (Contains!.Contains('\n') || Contains.Contains('\r')))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Contains cannot contain newline characters. Update-MatchInFile processes files line by line."),
                "InvalidContains",
                ErrorCategory.InvalidArgument,
                Contains));
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

        // Pattern に改行が含まれている場合はエラー（行単位で処理するため）
        if (hasRegex && (Pattern!.Contains('\n') || Pattern.Contains('\r')))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Pattern cannot contain newline characters. Update-MatchInFile processes files line by line."),
                "InvalidPattern",
                ErrorCategory.InvalidArgument,
                Pattern));
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
    /// ファイルを1回読みながら、マッチ判定・置換・コンテキスト表示を同時に行う
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

        // -WhatIf が明示的に指定されているかチェック
        bool isWhatIf = MyInvocation.BoundParameters.ContainsKey("WhatIf") && 
                        (SwitchParameter)MyInvocation.BoundParameters["WhatIf"];
        
        // ShouldProcess で確認（-Confirm や -WhatIf の処理）
        if (!ShouldProcess(resolvedPath, actionDescription))
        {
            // -Confirm で No を選んだ場合、または -WhatIf の場合
            if (!isWhatIf)
            {
                // -Confirm で No: 何も表示せず終了
                return;
            }
            // -WhatIf の場合は差分プレビューを表示するため続行
        }
        
        bool dryRun = isWhatIf;

        // バックアップ（dryRun でない場合のみ）
        if (!dryRun && Backup)
        {
            var backupPath = TextFileUtility.CreateBackup(resolvedPath);
            WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
        }

        // ===== 1パス処理: マッチ判定・置換・コンテキスト表示を同時に実行 =====
        string? tempFile = dryRun ? null : System.IO.Path.GetTempFileName();
        int replacementCount = 0;
        bool headerPrinted = false;
        
        // コンテキスト表示用
        var preContextBuffer = new RotateBuffer<(string line, int lineNum)>(2);
        int afterMatchCounter = 0;
        int lastOutputLine = 0;
        

        try
        {
            // 空ファイルのチェック
            var fileInfoObj = new FileInfo(resolvedPath);
            if (fileInfoObj.Length == 0)
            {
                if (dryRun)
                {
                    WriteWarning("File is empty. Nothing to replace.");
                }
                else
                {
                    WriteObject(AnsiColors.Info($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made"));
                }
                if (tempFile != null) File.Delete(tempFile);
                return;
            }

            using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
            {
                StreamWriter? writer = null;
                try
                {
                    if (!dryRun && tempFile != null)
                    {
                        writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536);
                        writer.NewLine = metadata.NewlineSequence;
                    }

                    if (!enumerator.MoveNext())
                    {
                        throw new InvalidOperationException("Unexpected empty file");
                    }

                    int lineNumber = 1;
                    string currentLine = enumerator.Current;
                    bool hasNext = enumerator.MoveNext();
                    bool isFirstOutputLine = true;

                    while (true)
                    {
                        // マッチ判定
                        bool isMatched = false;
                        if (lineNumber >= startLine && lineNumber <= endLine)
                        {
                            if (isLiteral)
                            {
                                isMatched = currentLine.Contains(Contains!);
                            }
                            else
                            {
                                isMatched = regex!.IsMatch(currentLine);
                            }
                        }

                        string outputLine = currentLine;

                        if (isMatched)
                        {
                            // ヘッダー出力（最初のマッチ時のみ）
                            if (!headerPrinted)
                            {
                                var displayPath = GetDisplayPath(originalPath, resolvedPath);
                                WriteObject(AnsiColors.Header(displayPath));
                                headerPrinted = true;
                            }

                            // ギャップ検出: 前回出力から2行以上離れている場合
                            if (lastOutputLine > 0 && lineNumber - 2 > lastOutputLine + 1)
                            {
                                WriteObject("");
                            }

                            // 前2行をコンテキストとして出力
                            foreach (var ctx in preContextBuffer)
                            {
                                if (ctx.lineNum > lastOutputLine)
                                {
                                    var ctxDisplayLine = BuildContextDisplayLine(ctx.line, isLiteral, regex);
                                    WriteObject($"{ctx.lineNum,3}- {ctxDisplayLine}");
                                    lastOutputLine = ctx.lineNum;
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

                            // マッチ行を表示（WhatIf: 赤＋緑、通常: 緑のみ）
                            string displayLine;
                            if (dryRun)
                            {
                                // WhatIf: 置換前（赤）と置換後（緑）を両方表示
                                if (isLiteral)
                                {
                                    displayLine = currentLine.Replace(Contains!, 
                                        $"{AnsiColors.Red}{Contains}{AnsiColors.Reset}{AnsiColors.Green}{Replacement}{AnsiColors.Reset}");
                                }
                                else
                                {
                                    displayLine = BuildRegexDisplayLine(currentLine, regex!, Replacement!);
                                }
                            }
                            else
                            {
                                // 通常実行: 置換後の結果のみ表示（緑でハイライト）
                                if (isLiteral)
                                {
                                    displayLine = currentLine.Replace(Contains!, 
                                        $"{AnsiColors.Green}{Replacement}{AnsiColors.Reset}");
                                }
                                else
                                {
                                    displayLine = regex!.Replace(currentLine, 
                                        match => $"{AnsiColors.Green}{Replacement}{AnsiColors.Reset}");
                                }
                            }

                            WriteObject($"{lineNumber,3}: {displayLine}");
                            lastOutputLine = lineNumber;
                            afterMatchCounter = 2;
                        }
                        else
                        {
                            // 後続コンテキストの出力
                            if (afterMatchCounter > 0)
                            {
                                var displayContextLine = BuildContextDisplayLine(currentLine, isLiteral, regex);
                                WriteObject($"{lineNumber,3}- {displayContextLine}");
                                lastOutputLine = lineNumber;
                                afterMatchCounter--;
                            }

                            // Rotate buffer更新
                            preContextBuffer.Add((currentLine, lineNumber));
                        }

                        // ファイルに書き込み（dryRun でない場合のみ）
                        if (writer != null)
                        {
                            if (!isFirstOutputLine)
                            {
                                writer.Write(metadata.NewlineSequence);
                            }
                            writer.Write(outputLine);
                            isFirstOutputLine = false;
                        }

                        if (hasNext)
                        {
                            lineNumber++;
                            currentLine = enumerator.Current;
                            hasNext = enumerator.MoveNext();
                        }
                        else
                        {
                            // 最終行の処理
                            if (writer != null && metadata.HasTrailingNewline)
                            {
                                writer.Write(metadata.NewlineSequence);
                            }
                            break;
                        }
                    }
                }
                finally
                {
                    writer?.Dispose();
                }
            }

            if (replacementCount == 0)
            {
                if (dryRun)
                {
                    WriteWarning("No lines matched. File not modified.");
                }
                else
                {
                    WriteObject(AnsiColors.Info($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made"));
                }
                if (tempFile != null) File.Delete(tempFile);
                return;
            }

            // 空行でコンテキストとサマリを分離
            WriteObject("");

            if (dryRun)
            {
                // WhatIf: ファイルは変更しない
                WriteObject(AnsiColors.WhatIf($"What if: Would update {GetDisplayPath(originalPath, resolvedPath)}: {replacementCount} replacement(s)"));
            }
            else
            {
                // アトミックに置換
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile!);
                WriteObject(AnsiColors.Success($"Updated {GetDisplayPath(originalPath, resolvedPath)}: {replacementCount} replacement(s) made"));
            }
        }
        catch
        {
            if (tempFile != null && File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
            throw;
        }
    }


    /// <summary>
    /// 正規表現置換の差分表示行を構築（削除部分を赤、追加部分を緑で表示）
    /// </summary>
    private static string BuildRegexDisplayLine(string originalLine, Regex regex, string replacement)
    {
        // 各マッチの削除→追加を連続表示（キャプチャグループ対応）
        var result = regex.Replace(originalLine, match => 
        {
            // match.Result() で $1, $2 などを展開した結果を取得
            var replacedText = match.Result(replacement);
            
            // 削除部分（元のマッチ）を赤+取り消し線、追加部分（置換結果）を緑で表示
            return $"{AnsiColors.Red}{match.Value}{AnsiColors.Reset}{AnsiColors.Green}{replacedText}{AnsiColors.Reset}";
        });
        
        return result;
    }

    /// <summary>
    /// コンテキスト行の表示を構築（マッチがあれば元の文字列を黄色でハイライト）
    /// </summary>
    private string BuildContextDisplayLine(string line, bool isLiteral, Regex? regex)
    {
        if (isLiteral)
        {
            if (line.Contains(Contains!))
            {
                return line.Replace(Contains!, $"{AnsiColors.Yellow}{Contains}{AnsiColors.Reset}");
            }
        }
        else
        {
            if (regex!.IsMatch(line))
            {
                // 正規表現のマッチ部分を黄色でハイライト（元の文字列のまま）
                return regex.Replace(line, match => $"{AnsiColors.Yellow}{match.Value}{AnsiColors.Reset}");
            }
        }
        
        return line;
    }
}