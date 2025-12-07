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
    /// <summary>
    /// rotate buffer で収集したコンテキスト情報
    /// </summary>
    private class ContextData
    {
        // 前2行コンテキスト
        public string? ContextBefore2 { get; set; }
        public string? ContextBefore1 { get; set; }
        public int ContextBefore2Line { get; set; }
        public int ContextBefore1Line { get; set; }

        // 削除時のみ使用
        public string? DeletedFirst { get; set; }
        public string? DeletedSecond { get; set; }
        public string? DeletedThirdLast { get; set; }  // リングバッファ（末尾3行）
        public string? DeletedSecondLast { get; set; }  // リングバッファ（末尾2行）
        public string? DeletedLast { get; set; }
        public int DeletedCount { get; set; }
        public int DeletedStartLine { get; set; }

        // 後2行コンテキスト
        public string? ContextAfter1 { get; set; }
        public string? ContextAfter2 { get; set; }
        public int ContextAfter1Line { get; set; }
        public int ContextAfter2Line { get; set; }
    }

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

    [Parameter(ValueFromPipeline = true)]
    public object[]? Content { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    private List<object>? _contentBuffer;
    private bool _accumulateContent;

    protected override void BeginProcessing()
    {
        // Path が引数で指定され、Content が引数で指定されていない場合のみパイプ入力を蓄積
        bool pathFromArgument = MyInvocation.BoundParameters.ContainsKey("Path") ||
                                MyInvocation.BoundParameters.ContainsKey("LiteralPath");
        bool contentFromArgument = MyInvocation.BoundParameters.ContainsKey("Content");

        _accumulateContent = pathFromArgument && !contentFromArgument;
    }

    protected override void ProcessRecord()
    {
        // パイプから Content が来ている場合は蓄積
        if (_accumulateContent)
        {
            if (Content != null)
            {
                (_contentBuffer ??= []).AddRange(Content);
            }
            return;
        }

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

        // コンテキスト情報（rotate buffer で収集）
        ContextData? context = null;
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
                // 行範囲を置換 + コンテキスト収集（rotate buffer パターン）
                bool collectContext = fileExists && LineRange != null;

                string? warningMessage;
                (linesRemoved, linesInserted, totalLines, warningMessage, context) = ReplaceLineRangeWithContext(
                    resolvedPath,
                    tempFile,
                    metadata,
                    startLine,
                    endLine,
                    contentLines,
                    collectContext);

                // 警告があれば出力
                if (!string.IsNullOrEmpty(warningMessage))
                {
                    WriteWarning(warningMessage);
                }
            }

            // アトミックに置換
            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

            // コンテキスト表示（rotate buffer から表示、ファイル再読込なし）
            if (context != null)
            {
                OutputUpdateContext(originalPath, resolvedPath,
                    context,
                    startLine,
                    linesInserted,
                    totalLines,
                    contentLines);
            }

            // 結果メッセージ
            string message = GenerateResultMessage(fileExists, linesRemoved, linesInserted);
            string prefix = fileExists ? "Updated" : "Created";
            WriteObject(AnsiColors.Success($"{prefix} {GetDisplayPath(originalPath, resolvedPath)}: {message}"));
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
            return $"{linesInserted} line(s) (net: +{linesInserted})";
        }

        // linesRemoved は実際に処理された行数

        if (linesInserted == 0)
        {
            return $"Removed {linesRemoved} line(s) (net: -{linesRemoved})";
        }

        // 行数に応じたメッセージを生成（常にnetを表示）
        int netChange = linesInserted - linesRemoved;
        string netStr = netChange > 0 ? $"+{netChange}" : netChange.ToString();
        return $"Replaced {linesRemoved} line(s) with {linesInserted} line(s) (net: {netStr})";
    }


    /// <summary>
    /// 行範囲を置換しながらコンテキストバッファを構築（1 pass）
    /// </summary>
    private (int linesRemoved, int linesInserted, int totalLines, string? warningMessage, ContextData? context) ReplaceLineRangeWithContext(
        string inputPath,
        string outputPath,
        TextFileUtility.FileMetadata metadata,
        int startLine,
        int endLine,
        string[] contentLines,
        bool collectContext)
    {
        // Context 収集用の変数を初期化
        ContextData? context = collectContext ? new ContextData() : null;
        bool isDelete = contentLines.Length == 0;

        int currentLine = 1;
        int outputLine = 1;
        int linesRemoved = 0;  // 実際に処理した行数をカウント
        int linesInserted = contentLines.Length;
        string? warningMessage = null;
        bool insertedContent = false;


        // 削除時の情報収集用
        int deletedCount = 0;

        // 後コンテキストカウンタ
        int afterCounter = 0;

        // 置換範囲の後に行があるかどうかを記録
        bool hasLinesAfterRange = false;

        using (var reader = new StreamReader(inputPath, metadata.Encoding))
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            writer.NewLine = metadata.NewlineSequence;
            string? line;
            bool hasNextLine;

            while ((line = reader.ReadLine()) != null)
            {
                hasNextLine = reader.Peek() != -1;

                // 前2行のコンテキスト収集（範囲の直前）
                if (context != null)
                {
                    if (currentLine == startLine - 2)
                    {
                        context.ContextBefore2 = line;
                        context.ContextBefore2Line = currentLine;
                    }
                    else if (currentLine == startLine - 1)
                    {
                        context.ContextBefore1 = line;
                        context.ContextBefore1Line = currentLine;
                    }
                }

                // 置換範囲の開始時に新しい内容を挿入
                if (currentLine == startLine && !insertedContent)
                {
                    for (int i = 0; i < contentLines.Length; i++)
                    {
                        writer.Write(contentLines[i]);

                        // 各行の後に改行を追加（最終行は別処理）
                        if (i < contentLines.Length - 1)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                        // 最終行の処理は置換範囲の終わりで判定
                        outputLine++;
                    }
                    insertedContent = true;
                }

                // 置換範囲内の行を処理
                if (currentLine >= startLine && currentLine <= endLine)
                {
                    // 削除時：削除される行を保存
                    if (isDelete && context != null)
                    {
                        deletedCount++;

                        // 先頭2行を保存
                        if (deletedCount == 1)
                        {
                            context.DeletedFirst = line;
                        }
                        else if (deletedCount == 2)
                        {
                            context.DeletedSecond = line;
                        }

                        // リングバッファで末尾3行を更新
                        context.DeletedThirdLast = context.DeletedSecondLast;
                        context.DeletedSecondLast = context.DeletedLast;
                        context.DeletedLast = line;
                    }
                    linesRemoved++;  // 実際に削除/置換された行をカウント

                    // 置換範囲の最終行に達したら、後続行があるかチェック
                    if (currentLine == endLine)
                    {
                        hasLinesAfterRange = hasNextLine;

                        // 新しい内容の最終行の改行を書き込む
                        if (contentLines.Length > 0)
                        {
                            if (hasLinesAfterRange)
                            {
                                // 後続行がある場合は必ず改行
                                writer.Write(metadata.NewlineSequence);
                            }
                            else if (metadata.HasTrailingNewline)
                            {
                                // ファイル末尾で、元のファイルに末尾改行があった場合のみ改行
                                writer.Write(metadata.NewlineSequence);
                            }
                        }
                    }

                    currentLine++;
                    continue;
                }

                // 置換範囲外の行をコピー
                writer.Write(line);

                // 後コンテキスト収集（範囲の直後の2行）
                if (context != null && currentLine > endLine && afterCounter < 2)
                {
                    if (afterCounter == 0)
                    {
                        context.ContextAfter1 = line;
                        context.ContextAfter1Line = outputLine;
                    }
                    else if (afterCounter == 1)
                    {
                        context.ContextAfter2 = line;
                        context.ContextAfter2Line = outputLine;
                    }
                    afterCounter++;
                }

                // 改行の追加判定
                if (hasNextLine)
                {
                    // 後続行がある場合は必ず改行
                    writer.Write(metadata.NewlineSequence);
                }
                else if (metadata.HasTrailingNewline)
                {
                    // 最終行で元のファイルに末尾改行があった場合のみ改行
                    writer.Write(metadata.NewlineSequence);
                }

                currentLine++;
                outputLine++;
            }

            // ファイル末尾で置換範囲に達しなかった場合
            if (currentLine <= startLine && !insertedContent)
            {
                int actualLineCount = currentLine - 1;

                if (startLine > actualLineCount)
                {
                    throw new ArgumentException(
                        $"Line range {startLine}-{endLine} is out of bounds. File has only {actualLineCount} line(s).",
                        nameof(startLine));
                }

                if (endLine > actualLineCount)
                {
                    warningMessage = $"End line {endLine} exceeds file length ({actualLineCount} lines). Will process up to line {actualLineCount}.";
                }

                // 新しい内容を末尾に追加
                for (int i = 0; i < contentLines.Length; i++)
                {
                    writer.Write(contentLines[i]);

                    if (i < contentLines.Length - 1)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    // 最終行の処理：元のファイルに末尾改行があった場合のみ改行
                    else if (metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    outputLine++;
                }
            }
        }

        // Context 情報を設定
        if (context != null && isDelete)
        {
            context.DeletedCount = deletedCount;
            context.DeletedStartLine = startLine;
        }

        int totalLines = outputLine - 1;
        return (linesRemoved, linesInserted, totalLines, warningMessage, context);
    }


    /// <summary>
    /// 更新コンテキストを表示（rotate buffer から、ファイル再読込なし）
    /// </summary>
    private void OutputUpdateContext(string originalPath, string filePath,
        ContextData context,
        int startLine,
        int linesInserted,
        int totalLines,
        string[] contentLines)
    {
        var displayPath = GetDisplayPath(originalPath, filePath);

        // ヘッダー出力
        WriteObject(AnsiColors.Header(displayPath));

        int endLine = startLine + linesInserted - 1;

        // 前2行のコンテキスト
        if (context.ContextBefore2Line > 0)
        {
            WriteObject($"{context.ContextBefore2Line,3}- {context.ContextBefore2}");
        }
        if (context.ContextBefore1Line > 0)
        {
            WriteObject($"{context.ContextBefore1Line,3}- {context.ContextBefore1}");
        }

        // 更新された行を表示
        if (linesInserted == 0)
        {
            // 空配列: : のみを表示
            WriteObject($"   :");
        }
        else if (linesInserted <= 5)
        {
            // 1-5行: すべて反転表示
            for (int i = 0; i < linesInserted; i++)
            {
                int lineNum = startLine + i;
                WriteObject($"{lineNum,3}: {AnsiColors.Inserted(contentLines[i])}");
            }
        }
        else
        {
            // 6行以上: 先頭2行 + 省略マーカー + 末尾2行
            // 先頭2行
            WriteObject($"{startLine,3}: {AnsiColors.Inserted(contentLines[0])}");
            WriteObject($"{startLine + 1,3}: {AnsiColors.Inserted(contentLines[1])}");

            // 省略マーカー
            WriteObject("   :");

            // 末尾2行
            WriteObject($"{endLine - 1,3}: {AnsiColors.Inserted(contentLines[linesInserted - 2])}");
            WriteObject($"{endLine,3}: {AnsiColors.Inserted(contentLines[linesInserted - 1])}");
        }

        // 後2行のコンテキスト
        if (context.ContextAfter1Line > 0)
        {
            WriteObject($"{context.ContextAfter1Line,3}- {context.ContextAfter1}");
        }
        if (context.ContextAfter2Line > 0)
        {
            WriteObject($"{context.ContextAfter2Line,3}- {context.ContextAfter2}");
        }

        // 空行でコンテキストとサマリを分離
        WriteObject("");
    }

    protected override void EndProcessing()
    {
        // パイプ入力蓄積モードでない場合は何もしない
        if (!_accumulateContent)
        {
            return;
        }

        // パイプから入力があった場合
        if (_contentBuffer is { Count: > 0 })
        {
            Content = _contentBuffer!.ToArray();
        }
        // パイプから入力がない + LineRange 指定あり → エラー
        else if (LineRange != null)
        {
            ThrowTerminatingError(new ErrorRecord(
                new PSArgumentException("Content is required when LineRange is specified. Use -Content @() to explicitly delete lines."),
                "ContentRequired",
                ErrorCategory.InvalidArgument,
                null));
        }
        // パイプから入力がない + LineRange なし → 何もしない
        else
        {
            return;
        }

        // LineRangeバリデーション
        ValidateLineRange(LineRange);

        // LineRange指定時は既存ファイルが必要
        bool allowNewFiles = (LineRange == null);

        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles, requireExisting: !allowNewFiles))
        {
            ProcessFile(fileInfo.InputPath, fileInfo.ResolvedPath);
        }
    }
}
