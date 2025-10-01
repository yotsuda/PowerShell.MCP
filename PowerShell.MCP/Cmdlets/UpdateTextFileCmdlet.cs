using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイルの内容を更新
/// LLM最適化：文字列置換、正規表現置換、行範囲置換の3つのモード
/// </summary>
[Cmdlet(VerbsData.Update, "TextFile", SupportsShouldProcess = true)]
public class UpdateTextFileCmdlet : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "Literal", Mandatory = true)]
    public string OldValue { get; set; } = null!;

    [Parameter(ParameterSetName = "Literal", Mandatory = true)]
    public string NewValue { get; set; } = null!;

    [Parameter(ParameterSetName = "Regex", Mandatory = true)]
    public string Pattern { get; set; } = null!;

    [Parameter(ParameterSetName = "Regex", Mandatory = true)]
    public string Replacement { get; set; } = null!;

    [Parameter(ParameterSetName = "ContentReplacement", Mandatory = true)]
    public object Content { get; set; } = null!;

    [Parameter(ParameterSetName = "Literal")]
    [Parameter(ParameterSetName = "Regex")]
    [Parameter(ParameterSetName = "ContentReplacement")]
    public int[] LineRange { get; set; }

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
                    if (ParameterSetName == "ContentReplacement")
                    {
                        ProcessContentReplacement(resolvedPath);
                    }
                    else
                    {
                        ProcessStringReplacement(resolvedPath);
                    }
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "UpdateFailed", ErrorCategory.WriteError, resolvedPath));
                }
            }
        }
    }

    /// <summary>
    /// 行範囲置換またはファイル全体置換を処理
    /// </summary>
    private void ProcessContentReplacement(string resolvedPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath);
        string[] contentLines = TextFileUtility.ConvertToStringArray(Content);
        
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
        bool isFullFileReplace = LineRange == null;

        string actionDescription = isFullFileReplace 
            ? "Replace entire file content" 
            : $"Replace lines {startLine}-{endLine}";

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
                    linesChanged = TextFileUtility.ReplaceEntireFile(tempFile, metadata, contentLines);
                    
                    // 元のファイルを読んで tempFile に書き込んだ後、置換
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

                var action = contentLines == null ? "deleted" : "replaced";
                WriteInformation(new InformationRecord(
                    $"Updated {System.IO.Path.GetFileName(resolvedPath)}: {linesChanged} line(s) {action}",
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

    /// <summary>
    /// 文字列リテラル置換または正規表現置換を処理
    /// </summary>
    private void ProcessStringReplacement(string resolvedPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath);
        
        int replacementCount = 0;
        var isLiteral = ParameterSetName == "Literal";
        var regex = isLiteral ? null : new Regex(Pattern);
        
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        if (ShouldProcess(resolvedPath, $"Update text in file"))
        {
            // バックアップ
            if (Backup)
            {
                var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                WriteVerbose($"Created backup: {backupPath}");
            }

            // 1パスストリーミング処理
            var tempFile = System.IO.Path.GetTempFileName();
            bool hasMatch = false;

            try
            {
                TextFileUtility.ProcessFileStreaming(
                    resolvedPath,
                    tempFile,
                    metadata,
                    (line, lineNumber) =>
                    {
                        if (lineNumber >= startLine && lineNumber <= endLine)
                        {
                            if (isLiteral)
                            {
                                if (line.Contains(OldValue))
                                {
                                    // 置換回数を正確にカウント
                                    int count = (line.Length - line.Replace(OldValue, "").Length) / 
                                                Math.Max(1, OldValue.Length);
                                    replacementCount += count;
                                    hasMatch = true;
                                    return line.Replace(OldValue, NewValue);
                                }
                            }
                            else
                            {
                                var matches = regex.Matches(line);
                                if (matches.Count > 0)
                                {
                                    replacementCount += matches.Count;
                                    hasMatch = true;
                                    return regex.Replace(line, Replacement);
                                }
                            }
                        }
                        return line;
                    });

                if (!hasMatch)
                {
                    WriteWarning("No matches found. File not modified.");
                    File.Delete(tempFile);
                    return;
                }

                // アトミックに置換
                TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                WriteInformation(new InformationRecord(
                    $"Updated {System.IO.Path.GetFileName(resolvedPath)}: {replacementCount} replacement(s) made",
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
}

