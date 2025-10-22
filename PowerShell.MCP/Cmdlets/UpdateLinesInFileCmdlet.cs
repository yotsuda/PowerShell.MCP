using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルの行を更新
/// LLM最適化：行範囲指定または全体置換、Content省略で削除
/// LineRange未指定時：ファイル全体の置き換え（既存）または新規作成（新規）
/// </summary>
[Cmdlet(VerbsData.Update, "LinesInFile", SupportsShouldProcess = true)]
public class UpdateLinesInFileCmdlet : TextFileCmdletBase
{
    private const string ErrorMessageLineRangeWithoutFile = 
        "File not found: {0}. Cannot use -LineRange with non-existent file.";

    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter(Position = 1)]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter]
    public object[]? Content { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void ProcessRecord()
    {
        // LineRangeバリデーション
        ValidateLineRange(LineRange);

        // LineRange指定時は既存ファイルが必要
        bool allowNewFiles = (LineRange == null);
        
        if (!allowNewFiles)
        {
            // LineRange指定時は既存ファイル必須なので、存在しない場合はカスタムエラー
            foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
            {
                ProcessFile(fileInfo.InputPath, fileInfo.ResolvedPath);
            }
        }
        else
        {
            // LineRange未指定時は新規ファイル作成可能
            foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: true, requireExisting: false))
            {
                ProcessFile(fileInfo.InputPath, fileInfo.ResolvedPath);
            }
        }
    }

    private void ProcessFile(string originalPath, string resolvedPath)
    {
        bool fileExists = File.Exists(resolvedPath);

        try
        {
            // メタデータの取得または生成
            TextFileUtility.FileMetadata metadata = fileExists
                ? TextFileUtility.DetectFileMetadata(resolvedPath, Encoding)
                : CreateNewFileMetadata(resolvedPath);

            string[] contentLines = TextFileUtility.ConvertToStringArray(Content);
            
            // Content に非 ASCII 文字が含まれている場合、エンコーディングを UTF-8 にアップグレード
            if (TextFileUtility.TryUpgradeEncodingIfNeeded(metadata, contentLines, Encoding != null, out var upgradeMessage))
            {
                WriteInformation(upgradeMessage, ["EncodingUpgrade"]);
            }
            
            var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
            bool isFullFileReplace = LineRange == null;


            string actionDescription = GetActionDescription(fileExists, isFullFileReplace, startLine, endLine);

            if (ShouldProcess(resolvedPath, actionDescription))
            {
                if (Backup && fileExists)
                {
                    var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                    WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
                }

                ExecuteFileOperation(
                    resolvedPath, 
                    metadata, 
                    contentLines, 
                    isFullFileReplace, 
                    startLine, 
                    endLine, 
                    fileExists, 
                    originalPath);
            }
        }
        catch (Exception ex)
        {
            string errorId = fileExists ? "SetFailed" : "CreateFailed";
            WriteError(new ErrorRecord(ex, errorId, ErrorCategory.WriteError, resolvedPath));
        }
    }

    private TextFileUtility.FileMetadata CreateNewFileMetadata(string resolvedPath)
    {
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = Encoding != null 
                ? TextFileUtility.GetEncoding(resolvedPath, Encoding) 
                : new System.Text.UTF8Encoding(false), // UTF8 without BOM
            NewlineSequence = Environment.NewLine,
            HasTrailingNewline = true  // デフォルトで末尾改行あり
        };

        // ディレクトリが存在しない場合は作成
        var directory = System.IO.Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            WriteInformation($"Created directory: {directory}", new string[] { "DirectoryCreated" });
        }
        return metadata;
    }

    private static string GetActionDescription(bool fileExists, bool isFullFileReplace, int startLine, int endLine)
    {
        if (!fileExists)
        {
            return "Create new file";
        }

        if (isFullFileReplace)
        {
            return "Set entire file content";
        }

        return $"Set content of lines {startLine}-{endLine}";
    }

    private void ExecuteFileOperation(
        string resolvedPath,
        TextFileUtility.FileMetadata metadata,
        string[] contentLines,
        bool isFullFileReplace,
        int startLine,
        int endLine,
        bool fileExists,
        string originalPath)
    {
        var tempFile = System.IO.Path.GetTempFileName();
        int linesRemoved;
        int linesInserted;
        
        // コンテキスト表示用のバッファ（1 pass実装）
        Dictionary<int, string>? contextBuffer = null;
        HashSet<int>? updatedLinesSet = null;
        Dictionary<int, string>? deletedLines = null;
        int totalLines = 0;

        try
        {
            if (isFullFileReplace)
            {
                // ファイル全体を置換（新規も既存も同じメソッド）
                (linesRemoved, linesInserted) = TextFileUtility.ReplaceEntireFile(
                    resolvedPath,
                    tempFile,
                    metadata,
                    contentLines);
            }
            else
            {
                // 行範囲を置換 + コンテキスト収集（1 pass）
                if (fileExists && LineRange != null)
                {
                    // コンテキスト表示のためバッファを常に有効化（削除時も）
                    contextBuffer = new Dictionary<int, string>();
                    updatedLinesSet = new HashSet<int>();
                }
                
                string? warningMessage;
                (linesRemoved, linesInserted, totalLines, warningMessage, deletedLines) = ReplaceLineRangeWithContext(
                    resolvedPath,
                    tempFile,
                    metadata,
                    startLine,
                    endLine,
                    contentLines,
                    contextBuffer,
                    updatedLinesSet);
                
                // 警告があれば出力
                if (!string.IsNullOrEmpty(warningMessage))
                {
                    WriteWarning(warningMessage);
                }
            }

            // アトミックに置換
            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

            // コンテキスト表示（バッファから表示、ファイル再読込なし）
            if (contextBuffer != null && updatedLinesSet != null && updatedLinesSet.Count > 0)
            {
                OutputUpdateContextFromBuffer(
                    resolvedPath, 
                    contextBuffer, 
                    updatedLinesSet.ToList(), 
                    totalLines);
            }
            else if (contextBuffer != null && deletedLines != null && deletedLines.Count > 0)
            {
                // 削除時のコンテキスト表示（バッファから表示、ファイル再読込なし）
                OutputDeleteContextFromBuffer(
                    resolvedPath, 
                    contextBuffer, 
                    deletedLines,
                    LineRange![0]);
            }

            // 結果メッセージ
            string message = GenerateResultMessage(fileExists, linesRemoved, linesInserted);
            string prefix = fileExists ? "Updated" : "Created";
            WriteObject($"{prefix} {GetDisplayPath(originalPath, resolvedPath)}: {message}");
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

    private static string GenerateResultMessage(bool fileExists, int linesRemoved, int linesInserted)
    {
        if (!fileExists)
        {
            return $"Created {linesInserted} line(s)";
        }

        if (linesInserted == 0)
        {
            return $"Removed {linesRemoved} line(s)";
        }

        if (linesInserted == linesRemoved)
        {
            return $"Replaced {linesRemoved} line(s)";
        }

        // 行数が変わる置換
        int netChange = linesInserted - linesRemoved;
        string netStr = netChange > 0 ? $"+{netChange}" : netChange.ToString();
        return $"Replaced {linesRemoved} line(s) with {linesInserted} line(s) (net: {netStr})";
    }
    /// <summary>
    /// 行範囲を置換しながらコンテキストバッファを構築（1 pass）
    /// </summary>
    private (int linesRemoved, int linesInserted, int totalLines, string? warningMessage, Dictionary<int, string>? deletedLines) ReplaceLineRangeWithContext(
        string inputPath,
        string outputPath,
        TextFileUtility.FileMetadata metadata,
        int startLine,
        int endLine,
        string[] contentLines,
        Dictionary<int, string>? contextBuffer,
        HashSet<int>? updatedLinesSet)
    {
        // コンテキスト範囲を計算（前後2行）
        int contextStart = Math.Max(1, startLine - 2);
        int contextEnd = endLine + 2; // 暫定値、totalLinesで後で調整

        int currentLine = 1;
        int outputLine = 1;
        int linesRemoved = endLine - startLine + 1;
        int linesInserted = contentLines.Length;
        string? warningMessage = null;
        bool insertedContent = false;
        Dictionary<int, string>? deletedLines = contentLines.Length == 0 && contextBuffer != null ? new Dictionary<int, string>() : null;

        using (var reader = new StreamReader(inputPath, metadata.Encoding))
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            writer.NewLine = metadata.NewlineSequence;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                // 置換範囲の開始
                if (currentLine == startLine && !insertedContent)
                {
                    // 新しい内容を挿入
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        writer.Write(contentLines[i]);
                        
                        // コンテキストバッファに保存
                        if (contextBuffer != null && outputLine >= contextStart)
                        {
                            contextBuffer[outputLine] = contentLines[i];
                            updatedLinesSet?.Add(outputLine);
                        }
                        
                        if (i < contentLines.Length - 1 || currentLine < endLine || reader.Peek() != -1)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                        outputLine++;
                    }
                    insertedContent = true;
                }

                // 置換範囲内の行を処理
                if (currentLine >= startLine && currentLine <= endLine)
                {
                    // 削除時：削除される行を保存
                    if (deletedLines != null)
                    {
                        deletedLines[currentLine] = line;
                        
                        // 削除時のみ：コンテキスト範囲内の前後行も保存（削除前のコンテキスト）
                        if (contextBuffer != null && currentLine >= contextStart && currentLine <= endLine)
                        {
                            contextBuffer[currentLine] = line;
                        }
                    }
                    
                    currentLine++;
                    continue;
                }

                // 置換範囲外の行をコピー
                writer.Write(line);
                
                // コンテキストバッファに保存
                if (contextBuffer != null)
                {
                    if (deletedLines != null)
                    {
                        // 削除時：元のファイルの行番号で、削除範囲の前後2行を保存
                        if ((currentLine >= contextStart && currentLine < startLine) || 
                            (currentLine > endLine && currentLine <= endLine + 2))
                        {
                            contextBuffer[currentLine] = line;
                        }
                    }
                    else
                    {
                        // 更新時：出力ファイルの行番号で保存
                        if (outputLine >= contextStart && outputLine <= contextEnd + linesInserted - linesRemoved)
                        {
                            contextBuffer[outputLine] = line;
                        }
                    }
                }
                
                if (reader.Peek() != -1 || (currentLine == endLine && !insertedContent))
                {
                    writer.Write(metadata.NewlineSequence);
                }

                currentLine++;
                outputLine++;
            }

            // ファイル末尾で置換範囲に達しなかった場合
            if (currentLine <= startLine && !insertedContent)
            {
                int actualLineCount = currentLine - 1;
                
                // startLine が範囲外の場合は例外をスロー
                if (startLine > actualLineCount)
                {
                    throw new ArgumentException(
                        $"Line range {startLine}-{endLine} is out of bounds. File has only {actualLineCount} line(s).",
                        nameof(startLine));
                }
                
                // endLine が範囲外の場合は警告
                if (endLine > actualLineCount)
                {
                    warningMessage = $"End line {endLine} exceeds file length ({actualLineCount} lines). Will process up to line {actualLineCount}.";
                }

                // 新しい内容を末尾に追加
                for (int i = 0; i < contentLines.Length; i++)
                {
                    writer.Write(contentLines[i]);
                    
                    if (contextBuffer != null)
                    {
                        contextBuffer[outputLine] = contentLines[i];
                        updatedLinesSet?.Add(outputLine);
                    }
                    
                    if (i < contentLines.Length - 1)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    outputLine++;
                }
            }
        }

        int totalLines = outputLine - 1;
        return (linesRemoved, linesInserted, totalLines, warningMessage, deletedLines);
    }

    /// <summary>
    /// バッファから更新コンテキストを表示（ファイル再読込なし）
    /// </summary>
    private void OutputUpdateContextFromBuffer(
        string filePath,
        Dictionary<int, string> contextBuffer,
        List<int> updatedLines,
        int totalLines)
    {
        // 範囲を計算してマージ
        var (ranges, gapLines) = CalculateAndMergeRanges(updatedLines, totalLines, contextLines: 2);
        
        // 表示用パスを決定
        var displayPath = GetDisplayPath(filePath, filePath);
        WriteObject($"==> {displayPath} <==");
        
        // 範囲ごとに出力
        var updatedSet = new HashSet<int>(updatedLines);
        int rangeIndex = 0;
        
        foreach (var (start, end) in ranges)
        {
            for (int lineNumber = start; lineNumber <= end; lineNumber++)
            {
                if (!contextBuffer.ContainsKey(lineNumber))
                    continue;
                    
                var line = contextBuffer[lineNumber];
                
                if (updatedSet.Contains(lineNumber))
                {
                    // 更新された行を表示
                    if (updatedLines.Count <= 5)
                    {
                        // 1-5行: 全て反転表示
                        WriteObject($"{lineNumber,3}: \x1b[7m{line}\x1b[0m");
                    }
                    else
                    {
                        // 6行以上: 先頭2行と末尾2行のみ表示
                        int firstLine = updatedLines[0];
                        int secondLine = updatedLines[1];
                        int secondLastLine = updatedLines[updatedLines.Count - 2];
                        int lastLine = updatedLines[updatedLines.Count - 1];
                        
                        if (lineNumber == firstLine || lineNumber == secondLine)
                        {
                            // 先頭2行
                            WriteObject($"{lineNumber,3}: \x1b[7m{line}\x1b[0m");
                        }
                        else if (lineNumber == secondLine + 1)
                        {
                            // 省略マーカー（3行目で1回だけ）- 反転表示
                            int omittedCount = updatedLines.Count - 4;
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
    /// バッファから削除コンテキストを表示（ファイル再読込なし、1 pass）
    /// </summary>
    private void OutputDeleteContextFromBuffer(
        string filePath,
        Dictionary<int, string> contextBuffer,
        Dictionary<int, string> deletedLines,
        int startLine)
    {
        // 表示用パスを決定
        var displayPath = GetDisplayPath(filePath, filePath);
        WriteObject($"==> {displayPath} <==");
        
        // 削除範囲の終了行を計算
        int endLine = startLine + deletedLines.Count - 1;
        
        // 前のコンテキスト行を表示（startLine-2 ～ startLine-1）
        for (int lineNumber = Math.Max(1, startLine - 2); lineNumber < startLine; lineNumber++)
        {
            if (contextBuffer.ContainsKey(lineNumber))
            {
                WriteObject($"{lineNumber,3}- {contextBuffer[lineNumber]}");
            }
        }
        
        // 削除された行を表示（反転表示）
        if (deletedLines.Count <= 5)
        {
            // 1-5行: すべて表示
            foreach (var kvp in deletedLines.OrderBy(kv => kv.Key))
            {
                WriteObject($"{kvp.Key,3}: \x1b[7m{kvp.Value}\x1b[0m");
            }
        }
        else
        {
            // 6行以上: 先頭2行と末尾2行のみ表示
            var orderedLines = deletedLines.OrderBy(kv => kv.Key).ToList();
            
            // 先頭2行
            WriteObject($"{orderedLines[0].Key,3}: \x1b[7m{orderedLines[0].Value}\x1b[0m");
            WriteObject($"{orderedLines[1].Key,3}: \x1b[7m{orderedLines[1].Value}\x1b[0m");
            
            // 省略マーカー
            int omittedCount = deletedLines.Count - 4;
            WriteObject($"   : \x1b[7m... ({omittedCount} lines omitted) ...\x1b[0m");
            
            // 末尾2行
            WriteObject($"{orderedLines[orderedLines.Count - 2].Key,3}: \x1b[7m{orderedLines[orderedLines.Count - 2].Value}\x1b[0m");
            WriteObject($"{orderedLines[orderedLines.Count - 1].Key,3}: \x1b[7m{orderedLines[orderedLines.Count - 1].Value}\x1b[0m");
        }
        
        // 後のコンテキスト行を表示（endLine+1 ～ endLine+2）
        for (int lineNumber = endLine + 1; lineNumber <= endLine + 2; lineNumber++)
        {
            if (contextBuffer.ContainsKey(lineNumber))
            {
                WriteObject($"{lineNumber,3}- {contextBuffer[lineNumber]}");
            }
        }
    }
}