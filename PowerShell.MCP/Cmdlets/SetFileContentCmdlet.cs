using System;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets
{
    [Cmdlet(VerbsCommon.Set, "FileContent", SupportsShouldProcess = true)]
    public class SetFileContentCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0)]
        public string Path { get; set; }

        [Parameter(Position = 1)]
        public object Content { get; set; }

        [Parameter]
        public int[] LineRange { get; set; }

        [Parameter]
        public SwitchParameter Backup { get; set; }

        [Parameter]
        public SwitchParameter Force { get; set; }

        protected override void ProcessRecord()
        {
            var resolvedPath = GetResolvedProviderPathFromPSPath(Path, out _).FirstOrDefault();
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
            {
                WriteError(new ErrorRecord(
                    new FileNotFoundException($"File not found: {Path}"),
                    "FileNotFound",
                    ErrorCategory.ObjectNotFound,
                    Path));
                return;
            }

            try
            {
                var metadata = TextFileUtility.DetectFileMetadata(resolvedPath);

                // Content を文字列配列に変換
                string[] contentLines = ConvertToStringArray(Content);

                int startLine = LineRange != null && LineRange.Length > 0 ? LineRange[0] : 1;
                int endLine = LineRange != null && LineRange.Length > 1 ? LineRange[1] : int.MaxValue;
                bool isFullFileReplace = LineRange == null;

                if (ShouldProcess(resolvedPath, "Set file content"))
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
                            using (var writer = new StreamWriter(tempFile, false, metadata.Encoding))
                            {
                                if (contentLines != null)
                                {
                                    for (int i = 0; i < contentLines.Length; i++)
                                    {
                                        writer.Write(contentLines[i]);
                                        
                                        // 最後の行以外、または元々末尾改行があった場合
                                        if (i < contentLines.Length - 1 || metadata.HasTrailingNewline)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                    }
                                }
                                // contentLines が null の場合は空ファイルになる
                            }

                            linesChanged = File.ReadLines(resolvedPath, metadata.Encoding).Count();
                        }
                        else
                        {
                            // 行範囲を置換（1パスストリーミング）
                            using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
                            using (var writer = new StreamWriter(tempFile, false, metadata.Encoding))
                            {
                                if (!enumerator.MoveNext())
                                {
                                    // 空ファイル
                                    File.Delete(tempFile);
                                    return;
                                }

                                int lineNumber = 1;
                                string currentLine = enumerator.Current;
                                bool hasNext = enumerator.MoveNext();
                                bool replacementDone = false;
                                bool isFirstLine = true;

                                while (true)
                                {
                                    if (lineNumber < startLine)
                                    {
                                        // 範囲前：そのまま出力
                                        if (!isFirstLine)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                        writer.Write(currentLine);
                                        isFirstLine = false;
                                    }
                                    else if (lineNumber >= startLine && lineNumber <= endLine)
                                    {
                                        if (!replacementDone)
                                        {
                                            // 範囲開始：置換内容を出力（削除の場合は何も出力しない）
                                            if (contentLines != null && contentLines.Length > 0)
                                            {
                                                if (!isFirstLine)
                                                {
                                                    writer.Write(metadata.NewlineSequence);
                                                }
                                                
                                                for (int i = 0; i < contentLines.Length; i++)
                                                {
                                                    if (i > 0)
                                                    {
                                                        writer.Write(metadata.NewlineSequence);
                                                    }
                                                    writer.Write(contentLines[i]);
                                                }
                                                isFirstLine = false;
                                            }
                                            // contentLines が null または空の場合は何も出力しない（削除）
                                            // isFirstLine は変更しない（次の行が最初の行になる）
                                            
                                            replacementDone = true;
                                            linesChanged = endLine - startLine + 1;
                                        }
                                        // 範囲内の残りの行はスキップ
                                    }
                                    else
                                    {
                                        // 範囲後：そのまま出力
                                        if (!isFirstLine)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                        writer.Write(currentLine);
                                        isFirstLine = false;
                                    }

                                    if (hasNext)
                                    {
                                        lineNumber++;
                                        currentLine = enumerator.Current;
                                        hasNext = enumerator.MoveNext();
                                    }
                                    else
                                    {
                                        // 最終行：元々末尾改行があった場合のみ追加
                                        if (metadata.HasTrailingNewline)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                        }
                                        break;
                                    }
                                }
                            }
                        }

                        // アトミックに置換
                        var backupTemp = resolvedPath + ".tmp";
                        if (File.Exists(backupTemp))
                        {
                            File.Delete(backupTemp);
                        }
                        
                        File.Move(resolvedPath, backupTemp);
                        File.Move(tempFile, resolvedPath);
                        File.Delete(backupTemp);

                        var action = Content == null ? "deleted" : "replaced";
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
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "SetFailed", ErrorCategory.WriteError, resolvedPath));
            }
        }

        private string[] ConvertToStringArray(object content)
        {
            if (content == null)
                return null;

            if (content is string str)
            {
                return str.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            }
            else if (content is string[] arr)
            {
                return arr;
            }
            else if (content is System.Collections.IEnumerable enumerable)
            {
                return enumerable.Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToArray();
            }
            else
            {
                return new[] { content.ToString() };
            }
        }
    }
}
