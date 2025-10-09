﻿using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// ファイルから行を削除
/// LLM最適化：行範囲指定、文字列包含、または正規表現マッチで削除（組み合わせも可能）
/// </summary>
[Cmdlet(VerbsCommon.Remove, "LinesFromFile", SupportsShouldProcess = true)]
public class RemoveLinesFromFileCmdlet : TextFileCmdletBase
{
    [Parameter(ParameterSetName = "Path", Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [SupportsWildcards]
    public string[] Path { get; set; } = null!;

    [Parameter(ParameterSetName = "LiteralPath", Mandatory = true, ValueFromPipelineByPropertyName = true)]
    [Alias("PSPath")]
    public string[] LiteralPath { get; set; } = null!;

    [Parameter]
    [ValidateLineRange]
    public int[]? LineRange { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Contains { get; set; }

    [Parameter(ParameterSetName = "Path")]
    [Parameter(ParameterSetName = "LiteralPath")]
    public string? Pattern { get; set; }

    [Parameter]
    public string? Encoding { get; set; }

    [Parameter]
    public SwitchParameter Backup { get; set; }

    protected override void BeginProcessing()
    {
        // -Contains と -Pattern の同時指定チェック
        ValidateContainsAndPatternMutuallyExclusive(Contains, Pattern);

        // LineRange、Contains、Pattern の少なくとも一方が指定されているかチェック
        if (LineRange == null && string.IsNullOrEmpty(Contains) && string.IsNullOrEmpty(Pattern))
        {
            throw new PSArgumentException("At least one of -LineRange, -Contains, or -Pattern must be specified.");
        }

        // LineRange バリデーション
        if (LineRange != null)
        {
            ValidateLineRange(LineRange);
        }
    }

    protected override void ProcessRecord()
    {
        foreach (var fileInfo in ResolveAndValidateFiles(Path, LiteralPath, allowNewFiles: false, requireExisting: true))
            try
            {
                var metadata = TextFileUtility.DetectFileMetadata(fileInfo.ResolvedPath, Encoding);

                int startLine = int.MaxValue;
                int endLine = int.MaxValue;
                Regex? regex = null;

                // 削除条件の準備
                bool useLineRange = LineRange != null;
                bool useContains = !string.IsNullOrEmpty(Contains);
                bool usePattern = !string.IsNullOrEmpty(Pattern);

                    string actionDescription;
                    if (useLineRange && useContains)
                    {
                        (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                        actionDescription = $"Remove lines {startLine}-{endLine} containing: {Contains}";
                    }
                    else if (useLineRange && usePattern)
                    {
                        (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                        regex = new Regex(Pattern!, RegexOptions.Compiled);
                        actionDescription = $"Remove lines {startLine}-{endLine} matching pattern: {Pattern}";
                    }
                    else if (useLineRange)
                    {
                        (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange!);
                        actionDescription = $"Remove lines {startLine}-{endLine}";
                    }
                    else if (useContains)
                    {
                        actionDescription = $"Remove lines containing: {Contains}";
                    }
                    else // usePattern only
                    {
                        regex = new Regex(Pattern!, RegexOptions.Compiled);
                        actionDescription = $"Remove lines matching pattern: {Pattern}";
                    }

                if (ShouldProcess(fileInfo.ResolvedPath, actionDescription))
                {
                    if (Backup)
                    {
                        var backupPath = TextFileUtility.CreateBackup(fileInfo.ResolvedPath);
                        WriteInformation($"Created backup: {backupPath}", new string[] { "Backup" });
                    }

                    var tempFile = System.IO.Path.GetTempFileName();
                    int linesRemoved = 0;

                    try
                    {
                        // 空ファイルのチェック
                        var fileInfoObj = new FileInfo(fileInfo.ResolvedPath);
                        if (fileInfoObj.Length == 0)
                        {
                            WriteWarning("File is empty. Nothing to remove.");
                            File.Delete(tempFile);
                            continue;
                        }

                        using (var enumerator = File.ReadLines(fileInfo.ResolvedPath, metadata.Encoding).GetEnumerator())
                        using (var writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536))
                        {
                            // 空ファイルは上でチェック済み
                            if (!enumerator.MoveNext())
                            {
                                throw new InvalidOperationException("Unexpected empty file");
                            }

                            int lineNumber = 1;
                            string currentLine = enumerator.Current;
                            bool hasNext = enumerator.MoveNext();
                            bool isFirstOutputLine = true;

                            while (true)
                            {
                                bool shouldRemove = false;

                                // 条件判定：LineRange AND/OR Contains/Pattern
                                if (useLineRange && useContains)
                                {
                                    // 両方指定されている場合はAND条件
                                    shouldRemove = (lineNumber >= startLine && lineNumber <= endLine) && 
                                                  currentLine.Contains(Contains!);
                                }
                                else if (useLineRange && usePattern)
                                {
                                    // 両方指定されている場合はAND条件
                                    shouldRemove = (lineNumber >= startLine && lineNumber <= endLine) && 
                                                  regex!.IsMatch(currentLine);
                                }
                                else if (useLineRange)
                                {
                                    shouldRemove = lineNumber >= startLine && lineNumber <= endLine;
                                }
                                else if (useContains)
                                {
                                    shouldRemove = currentLine.Contains(Contains!);
                                }
                                else // usePattern only
                                {
                                    shouldRemove = regex!.IsMatch(currentLine);
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
                        TextFileUtility.ReplaceFileAtomic(fileInfo.ResolvedPath, tempFile);

                        WriteObject($"Removed {linesRemoved} line(s) from {GetDisplayPath(fileInfo.InputPath, fileInfo.ResolvedPath)}");
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
                WriteError(new ErrorRecord(ex, "RemoveLineFailed", ErrorCategory.WriteError, fileInfo.ResolvedPath));
            }
        }
    }
