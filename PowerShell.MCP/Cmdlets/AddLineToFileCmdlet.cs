using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルに行を挿入
/// LLM最適化：行番号指定または末尾追加、空ファイルにも対応
/// </summary>
[Cmdlet(VerbsCommon.Add, "LineToFile", SupportsShouldProcess = true)]
public class AddLineToFileCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(Mandatory = true, Position = 1)]
    public object Content { get; set; } = null!;

    [Parameter(ParameterSetName = "LineNumber", Mandatory = true)]
    public int LineNumber { get; set; }

    [Parameter(ParameterSetName = "AtEnd")]
    public SwitchParameter AtEnd { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void ProcessRecord()
    {
        foreach (var path in Path)
        {
            System.Collections.ObjectModel.Collection<string> resolvedPaths;
            
            try
            {
                resolvedPaths = GetResolvedProviderPathFromPSPath(path, out _);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(
                    ex,
                    "PathResolutionFailed",
                    ErrorCategory.InvalidArgument,
                    path));
                continue;
            }
            
            foreach (var resolvedPath in resolvedPaths)
            {
                if (!File.Exists(resolvedPath))
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException($"File not found: {path}"),
                        "FileNotFound",
                        ErrorCategory.ObjectNotFound,
                        path));
                    continue;
                }

                try
                {
                    var metadata = TextFileUtility.DetectFileMetadata(resolvedPath);

                    // Content を文字列配列に変換
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
                                // 空ファイル：新しい内容のみを書き込む
                                HandleEmptyFile(tempFile, contentLines, metadata);
                            }
                            else
                            {
                                // 通常のファイル：挿入処理
                                InsertLines(resolvedPath, tempFile, contentLines, metadata, insertAt);
                            }

                            // アトミックに置換
                            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                            WriteInformation(new InformationRecord(
                                $"Added {contentLines.Length} line(s) to {TextFileUtility.GetRelativePath(GetResolvedProviderPathFromPSPath(SessionState.Path.CurrentFileSystemLocation.Path, out _).FirstOrDefault() ?? SessionState.Path.CurrentFileSystemLocation.Path, resolvedPath)} at line {insertAt}",
                                resolvedPath));
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
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, resolvedPath));
                }
            }
        }
    }

    /// <summary>
    /// 空ファイルの処理：新しい内容のみを書き込む
    /// LLM向け：空ファイルは末尾改行なしとして扱う
    /// </summary>
    private void HandleEmptyFile(string outputPath, string[] contentLines, TextFileUtility.FileMetadata metadata)
    {
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            for (int i = 0; i < contentLines.Length; i++)
            {
                writer.Write(contentLines[i]);
                
                // 最後の行以外は改行を追加
                // 空ファイルなので元の末尾改行は考慮しない
                if (i < contentLines.Length - 1)
                {
                    writer.Write(metadata.NewlineSequence);
                }
            }
        }
    }

    /// <summary>
    /// 通常のファイルへの行挿入処理
    /// </summary>
    private void InsertLines(
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

