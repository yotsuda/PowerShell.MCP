using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルに行を追加（新規作成も可能）
/// LLM最適化:新規ファイル・既存ファイルともにパラメータ省略可(デフォルトで末尾追加、-LineNumber で挿入位置指定)
/// </summary>
[Cmdlet(VerbsCommon.Add, "LinesToFile", SupportsShouldProcess = true)]
public class AddLinesToFileCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter(Mandatory = true, Position = 1)]
    public object[] Content { get; set; } = null!;

    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int LineNumber { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void ProcessRecord()
    {
        // -Path または -LiteralPath から処理対象を取得
        string[] inputPaths = Path ?? LiteralPath;
        bool isLiteralPath = (LiteralPath != null);
        
        foreach (var inputPath in inputPaths)
        {
            bool isNewFile = false;
            string? resolvedPath = null;
            
            // パス解決の試行
            try
            {
                if (isLiteralPath)
                {
                    // -LiteralPath: ワイルドカード展開なし
                    resolvedPath = GetUnresolvedProviderPathFromPSPath(inputPath);
                    isNewFile = !File.Exists(resolvedPath);
                }
                else
                {
                    // -Path: ワイルドカード展開あり
                    try
                    {
                        var resolved = GetResolvedProviderPathFromPSPath(inputPath, out _);
                        
                        // 解決されたパスを処理
                        foreach (var rPath in resolved)
                        {
                            try
                            {
                                AddToFile(rPath, inputPath, false);
                            }
                            catch (Exception ex)
                            {
                                WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, rPath));
                            }
                        }
                        continue; // 正常に処理完了したので次へ
                    }
                    catch (ItemNotFoundException)
                    {
                        // ファイルが存在しない場合、ワイルドカードチェック
                        if (WildcardPattern.ContainsWildcardCharacters(inputPath))
                        {
                            WriteError(new ErrorRecord(
                                new InvalidOperationException($"Cannot create new file with wildcard pattern: {inputPath}"),
                                "WildcardNotSupportedForNewFile",
                                ErrorCategory.InvalidArgument,
                                inputPath));
                            continue;
                        }
                        
                        // 新規ファイル作成の準備
                        resolvedPath = GetUnresolvedProviderPathFromPSPath(inputPath);
                        isNewFile = true;
                    }
                }
                
                // resolvedPath が設定されていれば処理
                if (resolvedPath != null)
                {
                    try
                    {
                        AddToFile(resolvedPath, inputPath, isNewFile);
                    }
                    catch (Exception ex)
                    {
                        WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, resolvedPath));
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "PathResolutionFailed", ErrorCategory.InvalidArgument, inputPath));
            }
        }
    }


    /// <summary>
    /// 新しい内容をファイルに書き込む（新規ファイルと空ファイルで共通）
    /// </summary>
    private static void WriteNewContent(string outputPath, string[] contentLines, TextFileUtility.FileMetadata metadata)
    {
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            for (int i = 0; i < contentLines.Length; i++)
            {
                writer.Write(contentLines[i]);
                
                if (i < contentLines.Length - 1)
                {
                    writer.Write(metadata.NewlineSequence);
                }
            }
        }
    }

    /// <summary>
    /// ファイルに行を追加（新規・既存ファイル共通）
    /// </summary>
    private void AddToFile(string resolvedPath, string originalPath, bool isNewFile)
    {

        // メタデータの取得または作成
        TextFileUtility.FileMetadata metadata;
        if (isNewFile)
        {
            // 新規ファイル：デフォルトのメタデータを使用
            metadata = new TextFileUtility.FileMetadata
            {
                Encoding = string.IsNullOrEmpty(Encoding) ? new System.Text.UTF8Encoding(false) : TextFileUtility.GetEncoding(resolvedPath, Encoding),
                NewlineSequence = Environment.NewLine,
                HasTrailingNewline = false
            };
        }
        else
        {
            // 既存ファイル：ファイルからメタデータを検出
            metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);
        }

        string[] contentLines = TextFileUtility.ConvertToStringArray(Content);

        // Content に非 ASCII 文字が含まれている場合、エンコーディングを UTF-8 にアップグレード
        if (TextFileUtility.TryUpgradeEncodingIfNeeded(metadata, contentLines, Encoding != null, out var upgradeMessage))
        {
            WriteInformation(upgradeMessage, ["EncodingUpgrade"]);
        }
        
        // LineNumber 指定がなければ末尾追加（新規・既存共通）
        int insertAt;
        bool effectiveAtEnd;
        
        if (isNewFile)
        {
            // 新規ファイル作成時: LineNumber > 1 の場合は警告を出す
            if (LineNumber > 1)
            {
                WriteWarning($"File does not exist. Creating new file. LineNumber {LineNumber} will be treated as line 1.");
            }
            
            // 新規ファイル: LineNumber 未指定なら末尾追加、LineNumber > 1 なら 1 として扱う
            insertAt = (LineNumber > 1) ? 1 : (LineNumber > 0 ? LineNumber : int.MaxValue);
            effectiveAtEnd = LineNumber == 0;
        }
        else
        {
            // 既存ファイル: LineNumber 未指定ならデフォルトで末尾追加
            insertAt = LineNumber > 0 ? LineNumber : int.MaxValue;
            effectiveAtEnd = LineNumber == 0;
        }
        
        string actionDescription = effectiveAtEnd
            ? $"Add {contentLines.Length} line(s) at end" 
            : $"Add {contentLines.Length} line(s) at line {insertAt}";

        if (ShouldProcess(resolvedPath, actionDescription))
        {
            if (Backup)
            {
                if (isNewFile)
                {
                    WriteWarning($"-Backup parameter is ignored for new file creation: {GetDisplayPath(originalPath, resolvedPath)}");
                }
                else
                {
                    var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                    WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
                }
            }

            var tempFile = System.IO.Path.GetTempFileName();
            int actualInsertAt = insertAt;

            try
            {
                if (isNewFile || new FileInfo(resolvedPath).Length == 0)
                {
                    // 新規ファイルまたは空ファイル：新しい内容のみを書き込む
                    WriteNewContent(tempFile, contentLines, metadata);
                }
                else
                {
                    // 既存の非空ファイル：挿入処理（コンテキストはリアルタイム出力）
                    int totalLines;
                    (totalLines, actualInsertAt) = InsertLinesWithContext(originalPath, resolvedPath, 
                        tempFile, 
                        contentLines, 
                        metadata, 
                        insertAt);
                }

                // アトミックに置換（または新規作成）
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                string message = isNewFile 
                    ? $"Created {GetDisplayPath(originalPath, resolvedPath)}: {contentLines.Length} line(s) (net: +{contentLines.Length})"
                    : $"Added {contentLines.Length} line(s) to {GetDisplayPath(originalPath, resolvedPath)} {(effectiveAtEnd ? "at end" : $"at line {insertAt}")} (net: +{contentLines.Length})";
                
                WriteObject(message);

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
    /// 通常のファイルへの行挿入処理（コンテキストをリアルタイム出力、1 pass）
    /// </summary>
    private (int totalLines, int actualInsertAt) InsertLinesWithContext(string originalPath, string inputPath, 
        string outputPath, 
        string[] contentLines, 
        TextFileUtility.FileMetadata metadata, 
        int insertAt)
    {
        var displayPath = GetDisplayPath(originalPath, inputPath);
        bool contextHeaderPrinted = false;
        
        using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            writer.NewLine = metadata.NewlineSequence;
            
            bool hasLines = enumerator.MoveNext();
            if (!hasLines)
            {
                // これは起こらないはず（空ファイルは別処理）
                for (int i = 0; i < contentLines.Length; i++)
                {
                    writer.Write(contentLines[i]);
                    if (i < contentLines.Length - 1)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                }
                return (contentLines.Length, 1);
            }

            int inputLineNumber = 1;
            int outputLineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            bool inserted = false;
            int actualInsertAt = insertAt;
            int afterContextCounter = 0;
            
            // Rotate buffer: 末尾追加時のコンテキスト表示用
            string? prevPrevLine = null;
            string? prevLine = null;
            
            while (true)
            {
                // 挿入位置に到達したら、新しい内容を先に書き込む
                if (!inserted && inputLineNumber == insertAt)
                {
                    actualInsertAt = outputLineNumber;
                    
                    // コンテキストヘッダー出力
                    if (!contextHeaderPrinted)
                    {
                        WriteObject($"==> {displayPath} <==");
                        contextHeaderPrinted = true;
                    }
                    
                    // 前2行を出力（rotate buffer から）
                    if (prevPrevLine != null && outputLineNumber >= 3)
                    {
                        WriteObject($"{outputLineNumber-2,3}- {prevPrevLine}");
                    }
                    if (prevLine != null && outputLineNumber >= 2)
                    {
                        WriteObject($"{outputLineNumber-1,3}- {prevLine}");
                    }
                    
                    // 挿入する行を出力
                    if (contentLines.Length <= 5)
                    {
                        // 1-5行: 全て表示
                        for (int i = 0; i < contentLines.Length; i++)
                        {
                            writer.Write(contentLines[i]);
                            WriteObject($"{outputLineNumber,3}: \x1b[7m{contentLines[i]}\x1b[0m");
                            
                            if (i < contentLines.Length - 1)
                            {
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }
                        }
                    }
                    else
                    {
                        // 6行以上: 先頭2行と末尾2行のみ表示
                        // 先頭2行
                        for (int i = 0; i < 2; i++)
                        {
                            writer.Write(contentLines[i]);
                            WriteObject($"{outputLineNumber,3}: \x1b[7m{contentLines[i]}\x1b[0m");
                            writer.Write(metadata.NewlineSequence);
                            outputLineNumber++;
                        }
                        
                        // 省略マーカー出力
                        int omittedCount = contentLines.Length - 4;
                        WriteObject($"   : \x1b[7m... ({omittedCount} lines omitted) ...\x1b[0m");
                        
                        // 中間行を書き込み（出力なし）
                        for (int i = 2; i < contentLines.Length - 2; i++)
                        {
                            writer.Write(contentLines[i]);
                            writer.Write(metadata.NewlineSequence);
                            outputLineNumber++;
                        }
                        
                        // 末尾2行
                        for (int i = contentLines.Length - 2; i < contentLines.Length; i++)
                        {
                            writer.Write(contentLines[i]);
                            WriteObject($"{outputLineNumber,3}: \x1b[7m{contentLines[i]}\x1b[0m");
                            
                            if (i < contentLines.Length - 1)
                            {
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }
                        }
                    }
                    
                    writer.Write(metadata.NewlineSequence);
                    outputLineNumber++;
                    inserted = true;
                    afterContextCounter = 2; // 後2行を出力
                }

                // 現在の行を書き込む
                writer.Write(currentLine);
                
                // 後コンテキストの出力
                if (afterContextCounter > 0)
                {
                    WriteObject($"{outputLineNumber,3}- {currentLine}");
                    afterContextCounter--;
                    if (afterContextCounter == 0)
                    {
                        // コンテキスト出力完了、空行追加
                        WriteObject("");
                    }
                }
                
                // Rotate buffer 更新: 末尾追加時のコンテキスト表示用に最後の2行を保持
                prevPrevLine = prevLine;
                prevLine = currentLine;
                
                if (hasNext)
                {
                    writer.Write(metadata.NewlineSequence);
                    inputLineNumber++;
                    outputLineNumber++;
                    currentLine = enumerator.Current;
                    hasNext = enumerator.MoveNext();
                }
                else
                {
                    // 最終行の処理
                    if (!inserted)
                    {
                        // 末尾追加（デフォルト）
                        writer.Write(metadata.NewlineSequence);
                        outputLineNumber++;
                        actualInsertAt = outputLineNumber;
                        
                        // コンテキストヘッダー出力
                        if (!contextHeaderPrinted)
                        {
                            WriteObject($"==> {displayPath} <==");
                            contextHeaderPrinted = true;
                        }
                        
                        // 前2行を出力（rotate buffer から）
                        int prevLineNum = outputLineNumber - 1;
                        if (prevPrevLine != null && prevLineNum >= 2)
                        {
                            WriteObject($"{prevLineNum-1,3}- {prevPrevLine}");
                        }
                        if (prevLine != null)
                        {
                            WriteObject($"{prevLineNum,3}- {prevLine}");
                        }
                        
                        // 挿入する行を出力
                        if (contentLines.Length <= 5)
                        {
                            // 1-5行: 全て表示
                            for (int i = 0; i < contentLines.Length; i++)
                            {
                                writer.Write(contentLines[i]);
                                WriteObject($"{outputLineNumber,3}: \x1b[7m{contentLines[i]}\x1b[0m");
                                
                                if (i < contentLines.Length - 1)
                                {
                                    writer.Write(metadata.NewlineSequence);
                                    outputLineNumber++;
                                }
                            }
                        }
                        else
                        {
                            // 6行以上: 先頭2行と末尾2行のみ表示
                            // 先頭2行
                            for (int i = 0; i < 2; i++)
                            {
                                writer.Write(contentLines[i]);
                                WriteObject($"{outputLineNumber,3}: \x1b[7m{contentLines[i]}\x1b[0m");
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }
                            
                            // 省略マーカー出力
                            int omittedCount = contentLines.Length - 4;
                            WriteObject($"   : \x1b[7m... ({omittedCount} lines omitted) ...\x1b[0m");
                            
                            // 中間行を書き込み（出力なし）
                            for (int i = 2; i < contentLines.Length - 2; i++)
                            {
                                writer.Write(contentLines[i]);
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }
                            
                            // 末尾2行
                            for (int i = contentLines.Length - 2; i < contentLines.Length; i++)
                            {
                                writer.Write(contentLines[i]);
                                WriteObject($"{outputLineNumber,3}: \x1b[7m{contentLines[i]}\x1b[0m");
                                
                                if (i < contentLines.Length - 1)
                                {
                                    writer.Write(metadata.NewlineSequence);
                                    outputLineNumber++;
                                }
                            }
                        }
                        
                        // 末尾追加時は後コンテキストなし、空行のみ
                        WriteObject("");
                        
                        inserted = true;
                    }
                    else
                    {
                        // 元のファイルの末尾改行を保持
                        if (metadata.HasTrailingNewline)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                    }
                    break;
                }
            }
            
            return (outputLineNumber, actualInsertAt);
        }
    }



    /// <summary>
    /// 複数行の内容を書き込む
    /// </summary>
    private static void WriteContentLines(
        StreamWriter writer, 
        string[] contentLines, 
        string newlineSequence, 
        bool addTrailingNewline)
    {
        for (int i = 0; i < contentLines.Length; i++)
        {
            writer.Write(contentLines[i]);

            // 各行の後に改行を追加（最後の行は条件による）
            if (i < contentLines.Length - 1 || addTrailingNewline)
            {
                writer.Write(newlineSequence);
            }
        }
    }

}