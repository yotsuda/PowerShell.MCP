using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイル内のパターンマッチを更新
/// LLM最適化：文字列リテラル置換と正規表現置換の2つのモード
/// </summary>
[Cmdlet(VerbsData.Update, "MatchInFile", SupportsShouldProcess = true)]
public class UpdateMatchInFileCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter]
    public string? Contains { get; set; }

    [Parameter]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Replacement { get; set; }

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        bool hasLiteral = !string.IsNullOrEmpty(Contains);
        bool hasRegex = !string.IsNullOrEmpty(Pattern);
        
        // どちらも指定されていない
        if (!hasLiteral && !hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Either -Contains/-Replacement or -Pattern/-Replacement must be specified."),
                "ParameterRequired",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // 両方指定されている
        if (hasLiteral && hasRegex)
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Cannot specify both -Contains/-Replacement and -Pattern/-Replacement."),
                "ConflictingParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // Literalモードで片方だけ指定されている
        if (hasLiteral && string.IsNullOrEmpty(Replacement))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -Contains and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
        
        // Regexモードで片方だけ指定されている
        if (hasRegex && string.IsNullOrEmpty(Replacement))
        {
            ThrowTerminatingError(new ErrorRecord(
                new ArgumentException("Both -Pattern and -Replacement must be specified together."),
                "IncompleteParameters",
                ErrorCategory.InvalidArgument,
                null));
        }
    }

    protected override void ProcessRecord()
    {
        // LineRangeバリデーション
        ValidateLineRange(LineRange);

        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
        {
            try
            {
                ProcessStringReplacement(fileInfo.InputPath, fileInfo.ResolvedPath);
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "UpdateFailed", ErrorCategory.WriteError, fileInfo.ResolvedPath));
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
        var isLiteral = !string.IsNullOrEmpty(Contains);
        var regex = isLiteral ? null : new Regex(Pattern, RegexOptions.Compiled);
        
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);

        // より具体的なアクション説明
        string actionDescription;
        if (isLiteral)
        {
            string rangeInfo = LineRange != null ? $" in lines {startLine}-{endLine}" : "";
            actionDescription = $"Replace '{Contains}' with '{Replacement}'{rangeInfo}";
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
                                if (line.Contains(Contains))
                                {
                                    // 置換回数を正確にカウント
                                    int count = (line.Length - line.Replace(Contains, "").Length) / 
                                                Math.Max(1, Contains.Length);
                                    replacementCount += count;
                                    hasMatch = true;
                                    return line.Replace(Contains, Replacement);
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
                    WriteObject($"{GetDisplayPath(originalPath, resolvedPath)}: 0 replacement(s) made");
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



