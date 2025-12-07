using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルから行を削除
/// LLM最適化：行範囲指定、文字列包含、または正規表現マッチで削除（組み合わせも可能）
/// </summary>
[Cmdlet(VerbsCommon.Remove, "LinesFromFile", SupportsShouldProcess = true)]
public class RemoveLinesFromFileCmdlet : TextFileCmdletBase
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
    public string? Contains { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        // -Contains と -Pattern の同時指定チェック
        ValidateContainsAndPatternMutuallyExclusive(Contains, Pattern);

        // LineRange、Contains、Pattern の少なくとも一方が指定されているかチェック
        if (LineRange == null && string.IsNullOrEmpty(Contains) && string.IsNullOrEmpty(Pattern))
        {
            throw new PSArgumentException("At least one of -LineRange, -Contains, or -Pattern must be specified.");
        }

        // Contains に改行が含まれている場合はエラー（行単位で処理するため）
        if (!string.IsNullOrEmpty(Contains) && (Contains.Contains('\n') || Contains.Contains('\r')))
        {
            throw new PSArgumentException("Contains cannot contain newline characters. Remove-LinesFromFile processes files line by line.");
        }

        // Pattern に改行が含まれている場合はエラー（行単位で処理するため）
        if (!string.IsNullOrEmpty(Pattern) && (Pattern.Contains('\n') || Pattern.Contains('\r')))
        {
            throw new PSArgumentException("Pattern cannot contain newline characters. Remove-LinesFromFile processes files line by line.");
        }

        // LineRange バリデーション
        if (LineRange != null)
        {
            ValidateLineRange(LineRange);
        }
    }

    protected override void ProcessRecord()
    {
        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
        {
            try
            {
                var metadata = TextFileUtility.DetectFileMetadata(fileInfo.ResolvedPath, Encoding);

                int startLine = int.MaxValue;
                int endLine = int.MaxValue;
                Regex? regex = null;

                // 削除条件の準備
                bool useLineRange = LineRange != null;
                bool useContains = !string.IsNullOrEmpty(Contains);
                bool usePattern = !string.IsNullOrEmpty(Pattern);

                string actionDescription;
                if (useLineRange && useContains)
                {
                    (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                    actionDescription = $"Remove lines {startLine}-{endLine} containing: {Contains}";
                }
                else if (useLineRange && usePattern)
                {
                    (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                    regex = new Regex(Pattern!, RegexOptions.Compiled);
                    actionDescription = $"Remove lines {startLine}-{endLine} matching pattern: {Pattern}";
                }
                else if (useLineRange)
                {
                    (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                    actionDescription = $"Remove lines {startLine}-{endLine}";
                }
                else if (useContains)
                {
                    actionDescription = $"Remove lines containing: {Contains}";
                }
                else // usePattern only
                {
                    regex = new Regex(Pattern!, RegexOptions.Compiled);
                    actionDescription = $"Remove lines matching pattern: {Pattern}";
                }

                // -WhatIf が明示的に指定されているかチェック
                bool isWhatIf = MyInvocation.BoundParameters.ContainsKey("WhatIf") && 
                                (SwitchParameter)MyInvocation.BoundParameters["WhatIf"];
                
                // ShouldProcess で確認（-Confirm や -WhatIf の処理）
                if (!ShouldProcess(fileInfo.ResolvedPath, actionDescription))
                {
                    if (!isWhatIf)
                    {
                        // -Confirm で No: 何も表示せず終了
                        continue;
                    }
                    // -WhatIf の場合は差分プレビューを表示するため続行
                }
                
                bool dryRun = isWhatIf;

                if (Backup && !dryRun)
                {
                    var backupPath = TextFileUtility.CreateBackup(fileInfo.ResolvedPath);
                    WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
                }

                string? tempFile = dryRun ? null : System.IO.Path.GetTempFileName();
                int linesRemoved = 0;

                // コンテキスト表示用（rotate buffer）
                // dryRun: 削除前の行番号、通常: 削除後の行番号
                var preContextBuffer = new RotateBuffer<(string line, int lineNum)>(2);
                int afterRemovalCounter = 0;
                int lastOutputLine = 0;
                bool headerPrinted = false;
                try
                {
                    // 空ファイルのチェック
                    var fileInfoObj = new FileInfo(fileInfo.ResolvedPath);
                    if (fileInfoObj.Length == 0)
                    {
                        WriteWarning("File is empty. Nothing to remove.");
                        if (tempFile != null) File.Delete(tempFile);
                        continue;
                    }

                    using (var enumerator = File.ReadLines(fileInfo.ResolvedPath, metadata.Encoding).GetEnumerator())
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

                            int lineNumber = 1;        // 削除前の行番号
                            int outputLineNumber = 1;  // 削除後の行番号
                            string currentLine = enumerator.Current;
                            bool hasNext = enumerator.MoveNext();
                            bool isFirstOutputLine = true;
                            bool wasRemoving = false;

                            while (true)
                            {
                                bool shouldRemove = false;

                                // 条件判定
                                if (useLineRange && useContains)
                                {
                                    shouldRemove = (lineNumber >= startLine && lineNumber <= endLine) &&
                                                  currentLine.Contains(Contains!);
                                }
                                else if (useLineRange && usePattern)
                                {
                                    shouldRemove = (lineNumber >= startLine && lineNumber <= endLine) &&
                                                  regex!.IsMatch(currentLine);
                                }
                                else if (useLineRange)
                                {
                                    shouldRemove = lineNumber >= startLine && lineNumber <= endLine;
                                }
                                else if (useContains)
                                {
                                    shouldRemove = currentLine.Contains(Contains!);
                                }
                                else // usePattern only
                                {
                                    shouldRemove = regex!.IsMatch(currentLine);
                                }

                                // 削除範囲の開始検出
                                if (!wasRemoving && shouldRemove)
                                {
                                    if (!headerPrinted)
                                    {
                                        var displayPath = GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath);
                                        WriteObject(AnsiColors.Header(displayPath));
                                        headerPrinted = true;
                                    }

                                    // 表示用の行番号を決定
                                    int contextLineNum = dryRun ? lineNumber : outputLineNumber;

                                    // ギャップ検出: 前回出力から2行以上離れている場合
                                    if (lastOutputLine > 0 && contextLineNum - 2 > lastOutputLine + 1)
                                    {
                                        WriteObject("");
                                    }

                                    // 前2行をコンテキストとして出力
                                    foreach (var ctx in preContextBuffer)
                                    {
                                        int ctxDisplayNum = dryRun ? ctx.lineNum : ctx.lineNum - linesRemoved;
                                        if (ctxDisplayNum > lastOutputLine)
                                        {
                                            WriteObject($"{ctxDisplayNum,3}- {ctx.line}");
                                            lastOutputLine = ctxDisplayNum;
                                        }
                                    }
                                }

                                // 削除範囲の終了検出
                                if (wasRemoving && !shouldRemove)
                                {
                                    afterRemovalCounter = 2;
                                }

                                if (shouldRemove)
                                {
                                    if (dryRun)
                                    {
                                        // -WhatIf: 削除行を赤色で表示（マッチ部分を黄色背景でハイライト）
                                        string displayLine;
                                        if (useContains)
                                        {
                                            // Contains: マッチ部分を黄色背景でハイライト
                                            displayLine = currentLine.Replace(Contains!, 
                                                $"{AnsiColors.RedOnYellow}{Contains}{AnsiColors.RedOnDefault}");
                                        }
                                        else if (usePattern)
                                        {
                                            // Pattern: 正規表現マッチ部分を黄色背景でハイライト
                                            displayLine = regex!.Replace(currentLine, 
                                                match => $"{AnsiColors.RedOnYellow}{match.Value}{AnsiColors.RedOnDefault}");
                                        }
                                        else
                                        {
                                            // LineRange のみ: 行全体を赤で表示
                                            displayLine = currentLine;
                                        }
                                        WriteObject($"{lineNumber,3}: {AnsiColors.Red}{displayLine}{AnsiColors.Reset}");
                                        lastOutputLine = lineNumber;
                                    }
                                    else
                                    {
                                        // 通常実行: 連続削除の最初だけ位置マーカーを表示（行番号なし）
                                        if (!wasRemoving)
                                        {
                                            WriteObject("   :");
                                        }
                                    }
                                    linesRemoved++;
                                }
                                else
                                {
                                    // 削除しない行：書き込む（dryRun でない場合のみ）
                                    if (writer != null)
                                    {
                                        if (!isFirstOutputLine)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                        writer.Write(currentLine);
                                        isFirstOutputLine = false;
                                    }

                                    // 削除後の2行をコンテキストとして出力
                                    if (afterRemovalCounter > 0)
                                    {
                                        int displayNum = dryRun ? lineNumber : outputLineNumber;
                                        WriteObject($"{displayNum,3}- {currentLine}");
                                        lastOutputLine = displayNum;
                                        afterRemovalCounter--;
                                    }

                                    // Rotate buffer更新
                                    preContextBuffer.Add((currentLine, lineNumber));

                                    outputLineNumber++;
                                }

                                wasRemoving = shouldRemove;

                                if (hasNext)
                                {
                                    lineNumber++;
                                    currentLine = enumerator.Current;
                                    hasNext = enumerator.MoveNext();
                                }
                                else
                                {
                                    // 最終行の処理
                                    if (writer != null && !shouldRemove && metadata.HasTrailingNewline)
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

                    if (linesRemoved == 0)
                    {
                        WriteWarning("No lines matched. File not modified.");
                        if (tempFile != null) File.Delete(tempFile);
                        continue;
                    }

                    // 空行でコンテキストとサマリを分離
                    WriteObject("");

                    if (dryRun)
                    {
                        // WhatIf: ファイルは変更しない
                        WriteObject(AnsiColors.WhatIf($"What if: Would remove {linesRemoved} line(s) from {GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath)}"));
                    }
                    else
                    {
                        // アトミックに置換
                        TextFileUtility.ReplaceFileAtomic(fileInfo.ResolvedPath, tempFile!);
                        WriteObject(AnsiColors.Success($"Removed {linesRemoved} line(s) from {GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath)} (net: -{linesRemoved})"));
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
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "RemoveLineFailed", ErrorCategory.WriteError, fileInfo.ResolvedPath));
            }
        }
    }
}