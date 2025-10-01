using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイルの内容を更新
/// LLM最適化：文字列リテラル置換と正規表現置換の2つのモード
/// </summary>
[Cmdlet(VerbsData.Update, "TextFile", SupportsShouldProcess = true)]
public class UpdateTextFileCmdlet : TextFileCmdletBase
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

    [Parameter(ParameterSetName = "Literal")]
    [Parameter(ParameterSetName = "Regex")]
    public int[]? LineRange { get; set; }

    [Parameter(ParameterSetName = "Literal")]
    [Parameter(ParameterSetName = "Regex")]
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
                    ProcessStringReplacement(path, resolvedPath);
                }
                catch (Exception ex)
                {
                    WriteError(new ErrorRecord(ex, "UpdateFailed", ErrorCategory.WriteError, resolvedPath));
                }
            }
        }
    }

    /// <summary>
    /// 文字列リテラル置換または正規表現置換を処理
    /// </summary>
    private void ProcessStringReplacement(string originalPath, string resolvedPath)
    {
        var metadata = TextFileUtility.DetectFileMetadata(resolvedPath, Encoding);
        
        int replacementCount = 0;
        var isLiteral = ParameterSetName == "Literal";
        var regex = isLiteral ? null : new Regex(Pattern);
        
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // より具体的なアクション説明
        string actionDescription;
        if (isLiteral)
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace '{OldValue}' with '{NewValue}'{rangeInfo}";
        }
        else
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace pattern '{Pattern}' with '{Replacement}'{rangeInfo}";
        }

        if (ShouldProcess(resolvedPath, actionDescription))
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

                WriteObject($"Updated {GetDisplayPath(originalPath, resolvedPath)}: {replacementCount} replacement(s) made");
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



