using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルの行を設定
/// LLM最適化：行範囲指定または全体置換、Content省略で削除
/// </summary>
[Cmdlet(VerbsCommon.Set, "LineToFile", SupportsShouldProcess = true)]
public class SetLineToFileCmdlet : TextFileCmdletBase
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(Position = 1)]
    public object[] Content { get; set; }

    [Parameter]
    public int[]? LineRange { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void ProcessRecord()
    {
        // LineRangeバリデーション（最優先）
        ValidateLineRange(LineRange);

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
                    var metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);
                    string[] contentLines = TextFileUtility.ConvertToStringArray(Content);

                    var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
                    bool isFullFileReplace = LineRange == null;

                    string actionDescription = isFullFileReplace 
                        ? "Set entire file content" 
                        : $"Set content of lines {startLine}-{endLine}";

                    if (ShouldProcess(resolvedPath, actionDescription))
                    {
                        if (Backup)
                        {
                            var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                            WriteVerbose($"Created backup: {backupPath}");
                        }

                        var tempFile = System.IO.Path.GetTempFileName();
                        int linesRemoved = 0;
                        int linesInserted = 0;

                        try
                        {
                            if (isFullFileReplace)
                            {
                                // ファイル全体を置換
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

                            // より詳細なメッセージ
                            string message;
                            if (linesInserted == 0)
                            {
                                // 削除のみ
                                message = $"Removed {linesRemoved} line(s)";
                            }
                            else if (linesInserted == linesRemoved)
                            {
                                // 同じ行数で置換
                                message = $"Replaced {linesRemoved} line(s)";
                            }
                            else
                            {
                                // 行数が変わる置換
                                int netChange = linesInserted - linesRemoved;
                                string netStr = netChange > 0 ? $"+{netChange}" : netChange.ToString();
                                message = $"Replaced {linesRemoved} line(s) with {linesInserted} line(s) (net: {netStr})";
                            }
                            
                            WriteObject($"Updated {GetDisplayPath(path, resolvedPath)}: {message}");
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
                    WriteError(new ErrorRecord(ex, "SetFailed", ErrorCategory.WriteError, resolvedPath));
                }
            }
        }
    }
}
