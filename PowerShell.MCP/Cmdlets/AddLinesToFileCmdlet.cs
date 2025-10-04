using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルに行を追加（新規作成も可能）
/// LLM最適化：新規・既存ファイル共に-AtEnd/-LineNumberが必須（2つのパラメータセット）
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
        // -AtEnd と -LineNumber の排他チェック
        if (AtEnd.IsPresent && LineNumber > 0)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Cannot specify both -AtEnd and -LineNumber."),
                "ConflictingParameters",
                ErrorCategory.InvalidArgument,
                null));
        }

        // どちらも指定されていない場合はエラー
        if (!AtEnd.IsPresent && LineNumber == 0)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Either -AtEnd or -LineNumber must be specified."),
                "ParameterRequired",
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
                // 新規ファイルの場合、-LineNumber は1のみ許可
                if (isNewFile && LineNumber > 1)
                {
                    WriteError(new ErrorRecord(
                        new InvalidOperationException($"For new files, -LineNumber must be 1. Specified: {LineNumber}"),
                        "InvalidLineNumber",
                        ErrorCategory.InvalidArgument,
                        inputPath));
                    continue;
                }

                try
                {
                    if (isNewFile)
                    {
                        // 新規ファイル作成
                        CreateNewFile(resolvedPath, inputPath);
                    }
                    else
                    {
                        // 既存ファイルへの追加
                        AddToExistingFile(resolvedPath, inputPath);
                    }
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, resolvedPath));
                }
            }
        }
    }

    /// <summary>
    /// 新規ファイルを作成
    /// </summary>
    private void CreateNewFile(string resolvedPath, string originalPath)
    {
        // 新規ファイル作成時に -Backup が指定されている場合は警告
        if (Backup)
        {
            WriteWarning($"Backup parameter is ignored for new file creation: {GetDisplayPath(originalPath, resolvedPath)}");
        }
        
        var metadata = new TextFileUtility.FileMetadata
        {
            Encoding = string.IsNullOrEmpty(Encoding) ? new System.Text.UTF8Encoding(false) : TextFileUtility.GetEncoding(resolvedPath, Encoding),
            NewlineSequence = Environment.NewLine,
            HasTrailingNewline = false
        };

        string[] contentLines = TextFileUtility.ConvertToStringArray(Content);
        
        if (ShouldProcess(resolvedPath, $"Create new file with {contentLines.Length} line(s)"))
        {
            var tempFile = System.IO.Path.GetTempFileName();

            try
            {
                WriteNewContent(tempFile, contentLines, metadata);
                File.Move(tempFile, resolvedPath, true);
                WriteObject($"Created {GetDisplayPath(originalPath, resolvedPath)}: Created {contentLines.Length} line(s)");
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
    /// 既存ファイルに行を追加
    /// </summary>
    private void AddToExistingFile(string resolvedPath, string originalPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);
        string[] contentLines = TextFileUtility.ConvertToStringArray(Content);

        int insertAt = AtEnd.IsPresent ? int.MaxValue : LineNumber;
        
        string actionDescription = AtEnd.IsPresent 
            ? $"Add {contentLines.Length} line(s) at end" 
            : $"Add {contentLines.Length} line(s) at line {insertAt}";

        if (ShouldProcess(resolvedPath, actionDescription))
        {
            if (Backup)
            {
                var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                WriteVerbose($"Created backup: {backupPath}");
            }

            var tempFile = System.IO.Path.GetTempFileName();

            try
            {
                // ファイルが空かどうかをチェック
                var fileInfo = new FileInfo(resolvedPath);
                if (fileInfo.Length == 0)
                {
                    // 空ファイル：新しい内容のみを書き込む（CreateNewFileと同じロジック）
                    WriteNewContent(tempFile, contentLines, metadata);
                }
                else
                {
                    // 通常のファイル：挿入処理
                    InsertLines(resolvedPath, tempFile, contentLines, metadata, insertAt);
                }

                // アトミックに置換
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                string locationMessage = AtEnd.IsPresent 
                    ? "at end" 
                    : $"at line {insertAt}";
                
                WriteObject($"Added {contentLines.Length} line(s) to {GetDisplayPath(originalPath, resolvedPath)} {locationMessage}");
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


