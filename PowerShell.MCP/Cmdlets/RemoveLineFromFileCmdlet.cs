using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルから行を削除
/// LLM最適化：行範囲指定またはパターンマッチで削除
/// </summary>
[Cmdlet(VerbsCommon.Remove, "LineFromFile", SupportsShouldProcess = true)]
public class RemoveLineFromFileCmdlet : TextFileCmdletBase
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("FullName")]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LineRange", Mandatory = true)]
    public int[] LineRange { get; set; } = null!;

    [Parameter(ParameterSetName = "Pattern", Mandatory = true)]
    public string Pattern { get; set; } = null!;

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

                    int startLine = int.MaxValue;
                    int endLine = int.MaxValue;
                    Regex regex = null;

                    string actionDescription;
                    if (ParameterSetName == "LineRange")
                    {
                        (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
                        actionDescription = $"Remove lines {startLine}-{endLine}";
                    }
                    else
                    {
                        regex = new Regex(Pattern);
                        actionDescription = $"Remove lines matching pattern: {Pattern}";
                    }

                    if (ShouldProcess(resolvedPath, actionDescription))
                    {
                        if (Backup)
                        {
                            var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                            WriteVerbose($"Created backup: {backupPath}");
                        }

                        var tempFile = System.IO.Path.GetTempFileName();
                        int linesRemoved = 0;

                        try
                        {
                            // 空ファイルのチェック
                            var fileInfo = new FileInfo(resolvedPath);
                            if (fileInfo.Length == 0)
                            {
                                WriteWarning("File is empty. Nothing to remove.");
                                File.Delete(tempFile);
                                continue;
                            }

                            using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
                            using (var writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536))
                            {
                                if (!enumerator.MoveNext())
                                {
                                    // 空ファイル（念のため）
                                    File.Delete(tempFile);
                                    WriteWarning("File is empty. Nothing to remove.");
                                    continue;
                                }

                                int lineNumber = 1;
                                string currentLine = enumerator.Current;
                                bool hasNext = enumerator.MoveNext();
                                bool isFirstOutputLine = true;

                                while (true)
                                {
                                    bool shouldRemove = false;

                                    if (ParameterSetName == "LineRange")
                                    {
                                        shouldRemove = lineNumber >= startLine && lineNumber <= endLine;
                                    }
                                    else
                                    {
                                        shouldRemove = regex.IsMatch(currentLine);
                                    }

                                    if (!shouldRemove)
                                    {
                                        // 削除しない行：書き込む
                                        if (!isFirstOutputLine)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                        
                                        writer.Write(currentLine);
                                        isFirstOutputLine = false;
                                    }
                                    else
                                    {
                                        linesRemoved++;
                                    }

                                    if (hasNext)
                                    {
                                        lineNumber++;
                                        currentLine = enumerator.Current;
                                        hasNext = enumerator.MoveNext();
                                    }
                                    else
                                    {
                                        // 最終行の処理
                                        // 最終行を削除していない場合のみ、元の末尾改行を保持
                                        if (!shouldRemove && metadata.HasTrailingNewline)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                        break;
                                    }
                                }
                            }

                            if (linesRemoved == 0)
                            {
                                WriteWarning("No lines matched. File not modified.");
                                File.Delete(tempFile);
                                continue;
                            }

                            // アトミックに置換
                            TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

                            WriteObject($"Removed {linesRemoved} line(s) from {GetDisplayPath(path, resolvedPath)}");
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
                    WriteError(new ErrorRecord(ex, "RemoveLineFailed", ErrorCategory.WriteError, resolvedPath));
                }
            }
        }
    }
}



