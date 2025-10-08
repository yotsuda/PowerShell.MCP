using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルに行を追加（新規作成も可能）
/// LLM最適化:新規ファイルはパラメータ省略可(末尾追加、-LineNumber 1 のみ許可)、既存ファイルは-AtEndまたは-LineNumberが必須
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
    public SwitchParameter AtEnd { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        // -AtEnd と -LineNumber の排他チェックのみ
        if (AtEnd.IsPresent && LineNumber > 0)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Cannot specify both -AtEnd and -LineNumber."),
                "ConflictingParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
    }
    protected override void ProcessRecord()
    {
        // -Path または -LiteralPath から処理対象を取得
        string[] inputPaths = Path ?? LiteralPath;
        bool isLiteralPath = (LiteralPath != null);
        
        foreach (var inputPath in inputPaths)
        {
            System.Collections.ObjectModel.Collection<string> resolvedPaths;
            bool isNewFile = false;
            
            if (isLiteralPath)
            {
                // -LiteralPath: ワイルドカード展開なし
                // 存在しないファイルでもパスを取得
                var resolved = GetUnresolvedProviderPathFromPSPath(inputPath);
                if (File.Exists(resolved))
                {
                    resolvedPaths = new System.Collections.ObjectModel.Collection<string> { resolved };
                }
                else
                {
                    // 新規ファイル
                    resolvedPaths = new System.Collections.ObjectModel.Collection<string> { resolved };
                    isNewFile = true;
                }
            }
            else
            {
                // -Path: ワイルドカード展開あり
                try
                {
                    resolvedPaths = GetResolvedProviderPathFromPSPath(inputPath, out _);
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
                    
                    // 親ディレクトリが存在すれば新規作成を試みる
                    var parentPath = System.IO.Path.GetDirectoryName(inputPath);
                    if (string.IsNullOrEmpty(parentPath))
                    {
                        parentPath = SessionState.Path.CurrentFileSystemLocation.Path;
                    }
                    
                    if (Directory.Exists(parentPath))
                    {
                        var newFilePath = System.IO.Path.Combine(parentPath, System.IO.Path.GetFileName(inputPath));
                        resolvedPaths = new System.Collections.ObjectModel.Collection<string> { newFilePath };
                        isNewFile = true;
                    }
                    else
                    {
                        WriteError(new ErrorRecord(
                            new DirectoryNotFoundException($"Parent directory not found: {parentPath}"),
                            "ParentDirectoryNotFound",
                            ErrorCategory.ObjectNotFound,
                            inputPath));
                        continue;
                    }
                }
            }
            // ワイルドカード展開された全てのファイルを処理
            foreach (var resolvedPath in resolvedPaths)
            {


                try
                {
                    // 新規・既存ファイル共通の処理
                    AddToFile(resolvedPath, inputPath, isNewFile);
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, resolvedPath));
                }
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
        // 既存ファイルの場合、-AtEnd または -LineNumber が必須
        if (!isNewFile && !AtEnd.IsPresent && LineNumber == 0)
        {
            WriteError(new ErrorRecord(
                new ArgumentException($"For existing file '{originalPath}', either -AtEnd or -LineNumber must be specified."),
                "ParameterRequiredForExistingFile",
                ErrorCategory.InvalidArgument,
                resolvedPath));
            return;
        }

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
        
        // 新規ファイルの場合はデフォルトで末尾（AtEnd）、既存ファイルの場合は指定された位置
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
            
            // 新規ファイル:パラメータ未指定なら末尾、LineNumber 1 または -AtEnd なら OK
            insertAt = LineNumber > 0 ? LineNumber : int.MaxValue;
            effectiveAtEnd = LineNumber == 0 || AtEnd.IsPresent;
        }
        else
        {
            // 既存ファイル：指定されたパラメータを使用（この時点で検証済み）
            insertAt = AtEnd.IsPresent ? int.MaxValue : LineNumber;
            effectiveAtEnd = AtEnd.IsPresent;
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
                    WriteVerbose($"Created backup: {backupPath}");
                }
            }

            var tempFile = System.IO.Path.GetTempFileName();

            try
            {
                if (isNewFile || new FileInfo(resolvedPath).Length == 0)
                {
                    // 新規ファイルまたは空ファイル：新しい内容のみを書き込む
                    WriteNewContent(tempFile, contentLines, metadata);
                }
                else
                {
                    // 既存の非空ファイル：挿入処理
                    InsertLines(resolvedPath, tempFile, contentLines, metadata, insertAt);
                }

                // アトミックに置換（または新規作成）
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                string message = isNewFile 
                    ? $"Created {GetDisplayPath(originalPath, resolvedPath)}: Created {contentLines.Length} line(s)"
                    : $"Added {contentLines.Length} line(s) to {GetDisplayPath(originalPath, resolvedPath)} {(AtEnd.IsPresent ? "at end" : $"at line {insertAt}")}";
                
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
    /// 通常のファイルへの行挿入処理
    /// </summary>
    private static void InsertLines(
        string inputPath, 
        string outputPath, 
        string[] contentLines, 
        TextFileUtility.FileMetadata metadata, 
        int insertAt)
    {
        using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            bool hasLines = enumerator.MoveNext();
            if (!hasLines)
            {
                // これは起こらないはず（空ファイルは別処理）
                WriteContentLines(writer, contentLines, metadata.NewlineSequence, false);
                return;
            }

            int lineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            bool inserted = false;

            while (true)
            {
                // 挿入位置に到達したら、新しい内容を先に書き込む
                if (!inserted && lineNumber == insertAt)
                {
                    WriteContentLines(writer, contentLines, metadata.NewlineSequence, true);
                    inserted = true;
                }

                // 現在の行を書き込む
                writer.Write(currentLine);

                if (hasNext)
                {
                    writer.Write(metadata.NewlineSequence);
                    lineNumber++;
                    currentLine = enumerator.Current;
                    hasNext = enumerator.MoveNext();
                }
                else
                {
                    // 最終行の処理
                    if (!inserted)
                    {
                        // AtEnd：末尾に追加
                        writer.Write(metadata.NewlineSequence);
                        WriteContentLines(writer, contentLines, metadata.NewlineSequence, false);
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


