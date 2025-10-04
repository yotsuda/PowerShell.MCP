using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルの行を設定
/// LLM最適化：行範囲指定または全体置換、Content省略で削除
/// LineRange未指定時：ファイル全体の置き換え（既存）または新規作成（新規）
/// </summary>
[Cmdlet(VerbsCommon.Set, "LinesToFile", SupportsShouldProcess = true)]
public class SetLinesToFileCmdlet : TextFileCmdletBase
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
    public object[]? Content { get; set; }

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void ProcessRecord()
    {
        // LineRangeバリデーション（最優先）
        ValidateLineRange(LineRange);

        // -Path または -LiteralPath から処理対象を取得
        string[] inputPaths = Path ?? LiteralPath;
        bool isLiteralPath = (LiteralPath != null);

        foreach (var inputPath in inputPaths)
        {
            System.Collections.ObjectModel.Collection<string> resolvedPaths;

            try
            {
                if (isLiteralPath)
                {
                    // -LiteralPath: ワイルドカード展開なし
                    var resolved = GetUnresolvedProviderPathFromPSPath(inputPath);
                    resolvedPaths = new System.Collections.ObjectModel.Collection<string> { resolved };
                }
                else
                {
                    // -Path: ワイルドカード展開あり
                    resolvedPaths = GetResolvedProviderPathFromPSPath(inputPath, out _);
                }
            }
            catch (ItemNotFoundException)
            {
                // パスが解決できない場合（新規ファイルの可能性）
                if (LineRange == null)
                {
                    // 新規ファイル作成を試みる
                    resolvedPaths = new System.Collections.ObjectModel.Collection<string> 
                    { 
                        GetUnresolvedProviderPathFromPSPath(inputPath) 
                    };
                }
                else
                {
                    WriteError(new ErrorRecord(
                        new FileNotFoundException(string.Format(ErrorMessageLineRangeWithoutFile, inputPath)),
                        "FileNotFoundWithLineRange",
                        ErrorCategory.ObjectNotFound,
                        inputPath));
                    continue;
                }
            }
            foreach (var resolvedPath in resolvedPaths)
            {
                ProcessFile(inputPath, resolvedPath);
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
            var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
            bool isFullFileReplace = LineRange == null;

            string actionDescription = GetActionDescription(fileExists, isFullFileReplace, startLine, endLine);

            if (ShouldProcess(resolvedPath, actionDescription))
            {
                if (Backup && fileExists)
                {
                    var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                    WriteVerbose($"Created backup: {backupPath}");
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
            WriteVerbose($"Created directory: {directory}");
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
                // 行範囲を置換
                (linesRemoved, linesInserted) = TextFileUtility.ReplaceLineRangeStreaming(
                    resolvedPath,
                    tempFile,
                    metadata,
                    startLine,
                    endLine,
                    contentLines);
            }

            // アトミックに置換
            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

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
}
