using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルの内容を完全に置き換え
/// LLM最適化：行範囲指定または全体置換、Content省略で削除
/// </summary>
[Cmdlet(VerbsCommon.Set, "FileContent", SupportsShouldProcess = true)]
public class SetFileContentCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(Position = 1)]
    public object Content { get; set; }

    [Parameter]
    public int[]? LineRange { get; set; }

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
                        int linesChanged = 0;

                        try
                        {
                            if (isFullFileReplace)
                            {
                                // ファイル全体を置換
                                File.Copy(resolvedPath, tempFile, true);
                                linesChanged = TextFileUtility.ReplaceEntireFile(tempFile, metadata, contentLines);
                            }
                            else
                            {
                                // 行範囲を置換
                                linesChanged = TextFileUtility.ReplaceLineRangeStreaming(
                                    resolvedPath,
                                    tempFile,
                                    metadata,
                                    startLine,
                                    endLine,
                                    contentLines);
                            }

                            // アトミックに置換
                            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                            var action = Content == null ? "deleted" : "replaced";
                            WriteInformation(new InformationRecord(
                                $"Updated {TextFileUtility.GetRelativePath(GetResolvedProviderPathFromPSPath(SessionState.Path.CurrentFileSystemLocation.Path, out _).FirstOrDefault() ?? SessionState.Path.CurrentFileSystemLocation.Path, resolvedPath)}: {linesChanged} line(s) {action}",
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
                    WriteError(new ErrorRecord(ex, "SetFailed", ErrorCategory.WriteError, resolvedPath));
                }
            }
        }
    }
}


