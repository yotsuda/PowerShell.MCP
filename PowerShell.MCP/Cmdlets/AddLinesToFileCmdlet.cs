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
            // 新規ファイル作成時のバリデーション
            if (LineNumber > 1)
            {
                WriteError(new ErrorRecord(
                    new ArgumentException($"For new file creation, -LineNumber must be 1 or omitted. Specified: {LineNumber}"),
                    "InvalidLineNumberForNewFile",
                    ErrorCategory.InvalidArgument,
                    resolvedPath));
                return;
            }
            
            // 新規ファイル: LineNumber 未指定なら末尾追加
            insertAt = LineNumber > 0 ? LineNumber : int.MaxValue;
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
            
            // コンテキスト表示用のバッファ（1 pass実装）
            Dictionary<int, string>? contextBuffer = null;
            List<int>? insertedLines = null;
            int totalLines = 0;
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
                    // 既存の非空ファイル：挿入処理 + コンテキスト収集（1 pass）
                    contextBuffer = new Dictionary<int, string>();
                    insertedLines = new List<int>();
                    
                    (totalLines, actualInsertAt) = InsertLinesWithContext(
                        resolvedPath, 
                        tempFile, 
                        contentLines, 
                        metadata, 
                        insertAt,
                        contextBuffer,
                        insertedLines);
                }

                // アトミックに置換（または新規作成）
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                // コンテキスト表示（バッファから表示、ファイル再読込なし）
                if (contextBuffer != null && insertedLines != null && insertedLines.Count > 0)
                {
                    OutputAddContextFromBuffer(
                        resolvedPath, 
                        contextBuffer, 
                        insertedLines, 
                        totalLines);
                }

                string message = isNewFile 
                    ? $"Created {GetDisplayPath(originalPath, resolvedPath)}: Created {contentLines.Length} line(s)"
                    : $"Added {contentLines.Length} line(s) to {GetDisplayPath(originalPath, resolvedPath)} {(effectiveAtEnd ? "at end" : $"at line {insertAt}")}";
                
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
    /// 通常のファイルへの行挿入処理（コンテキストバッファ付き、1 pass）
    /// </summary>
    private static (int totalLines, int actualInsertAt) InsertLinesWithContext(
        string inputPath, 
        string outputPath, 
        string[] contentLines, 
        TextFileUtility.FileMetadata metadata, 
        int insertAt,
        Dictionary<int, string> contextBuffer,
        List<int> insertedLines)
    {
        // コンテキスト範囲を計算（暫定、insertAtが末尾の場合は後で調整）
        int contextStart = Math.Max(1, insertAt == int.MaxValue ? 1 : insertAt - 2);
        
        using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            writer.NewLine = metadata.NewlineSequence;
            
            bool hasLines = enumerator.MoveNext();
            if (!hasLines)
            {
                // これは起こらないはず（空ファイルは別処理）
                WriteContentLines(writer, contentLines, metadata.NewlineSequence, false);
                return (contentLines.Length, 1);
            }

            int inputLineNumber = 1;
            int outputLineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            bool inserted = false;
            int actualInsertAt = insertAt;

            while (true)
            {
                // 挿入位置に到達したら、新しい内容を先に書き込む
                if (!inserted && inputLineNumber == insertAt)
                {
                    actualInsertAt = outputLineNumber;
                    
                    // 挿入行を書き込み＆バッファに保存
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        writer.Write(contentLines[i]);
                        
                        // コンテキストバッファに保存
                        if (outputLineNumber >= contextStart)
                        {
                            contextBuffer[outputLineNumber] = contentLines[i];
                            insertedLines.Add(outputLineNumber);
                        }
                        
                        if (i < contentLines.Length - 1)
                        {
                            writer.Write(metadata.NewlineSequence);
                            outputLineNumber++;
                        }
                    }
                    writer.Write(metadata.NewlineSequence);
                    outputLineNumber++;
                    inserted = true;
                }

                // 現在の行を書き込む
                writer.Write(currentLine);
                
                // コンテキストバッファに保存（範囲内のみ）
                int contextEnd = actualInsertAt + contentLines.Length + 1;
                if (outputLineNumber >= contextStart && outputLineNumber <= contextEnd)
                {
                    contextBuffer[outputLineNumber] = currentLine;
                }

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
                        
                        // 末尾追加の場合、コンテキスト範囲を再計算
                        contextStart = Math.Max(1, actualInsertAt - 2);
                        
                        for (int i = 0; i < contentLines.Length; i++)
                        {
                            writer.Write(contentLines[i]);
                            
                            // コンテキストバッファに保存
                            contextBuffer[outputLineNumber] = contentLines[i];
                            insertedLines.Add(outputLineNumber);
                            
                            if (i < contentLines.Length - 1)
                            {
                                writer.Write(metadata.NewlineSequence);
                                outputLineNumber++;
                            }
                        }
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
    /// バッファから追加コンテキストを表示（ファイル再読込なし）
    /// </summary>
    private void OutputAddContextFromBuffer(
        string filePath,
        Dictionary<int, string> contextBuffer,
        List<int> insertedLines,
        int totalLines)
    {
        // 範囲を計算してマージ
        var (ranges, gapLines) = CalculateAndMergeRanges(insertedLines, totalLines, contextLines: 2);
        
        // 表示用パスを決定
        var displayPath = GetDisplayPath(filePath, filePath);
        WriteObject($"==> {displayPath} <==");
        
        // 範囲ごとに出力
        var insertedSet = new HashSet<int>(insertedLines);
        int rangeIndex = 0;
        
        foreach (var (start, end) in ranges)
        {
            for (int lineNumber = start; lineNumber <= end; lineNumber++)
            {
                if (!contextBuffer.ContainsKey(lineNumber))
                    continue;
                    
                var line = contextBuffer[lineNumber];
                
                if (insertedSet.Contains(lineNumber))
                {
                    // 挿入された行を表示
                    if (insertedLines.Count <= 5)
                    {
                        // 1-5行: 全て反転表示
                        WriteObject($"{lineNumber,3}: \x1b[7m{line}\x1b[0m");
                    }
                    else
                    {
                        // 6行以上: 先頭2行と末尾2行のみ表示
                        int firstLine = insertedLines[0];
                        int secondLine = insertedLines[1];
                        int secondLastLine = insertedLines[insertedLines.Count - 2];
                        int lastLine = insertedLines[insertedLines.Count - 1];
                        
                        if (lineNumber == firstLine || lineNumber == secondLine)
                        {
                            // 先頭2行
                            WriteObject($"{lineNumber,3}: \x1b[7m{line}\x1b[0m");
                        }
                        else if (lineNumber == secondLine + 1)
                        {
                            // 省略マーカー（3行目で1回だけ）- 反転表示
                            int omittedCount = insertedLines.Count - 4;
                            WriteObject($"   : \x1b[7m... ({omittedCount} lines omitted) ...\x1b[0m");
                        }
                        else if (lineNumber == secondLastLine || lineNumber == lastLine)
                        {
                            // 末尾2行
                            WriteObject($"{lineNumber,3}: \x1b[7m{line}\x1b[0m");
                        }
                        // 中間行はスキップ
                    }
                }
                else
                {
                    // コンテキスト行を表示
                    WriteObject($"{lineNumber,3}- {line}");
                }
            }
            
            rangeIndex++;
            if (rangeIndex < ranges.Count)
            {
                WriteObject("");
            }
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
