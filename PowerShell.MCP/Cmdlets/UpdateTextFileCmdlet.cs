using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets
{
    [Cmdlet(VerbsData.Update, "TextFile", SupportsShouldProcess = true)]
    public class UpdateTextFileCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0)]
        public string Path { get; set; }

        [Parameter(ParameterSetName = "Literal", Mandatory = true)]
        public string OldValue { get; set; }

        [Parameter(ParameterSetName = "Literal", Mandatory = true)]
        public string NewValue { get; set; }

        [Parameter(ParameterSetName = "Regex", Mandatory = true)]
        public string Pattern { get; set; }

        [Parameter(ParameterSetName = "Regex", Mandatory = true)]
        public string Replacement { get; set; }

        [Parameter(ParameterSetName = "ContentReplacement", Mandatory = true)]
        public object Content { get; set; }

        [Parameter(ParameterSetName = "Literal")]
        [Parameter(ParameterSetName = "Regex")]
        [Parameter(ParameterSetName = "ContentReplacement")]
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

            // ファイルサイズチェック
            var (shouldContinue, warningMsg) = TextFileUtility.CheckFileSize(resolvedPath, Force);
            if (!shouldContinue)
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException(warningMsg),
                    "FileTooLarge",
                    ErrorCategory.InvalidOperation,
                    resolvedPath));
                return;
            }
            if (warningMsg != null)
            {
                WriteWarning(warningMsg);
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

        private void ProcessContentReplacement(string resolvedPath)
        {
            var metadata = TextFileUtility.DetectFileMetadata(resolvedPath);
            
            // Content を文字列配列に変換
            string[] contentLines = TextFileUtility.ConvertToStringArray(Content);
            
            var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
            bool isFullFileReplace = LineRange == null;

            if (ShouldProcess(resolvedPath, isFullFileReplace ? "Replace entire file content" : $"Replace lines {startLine}-{endLine}"))
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
                        using (var writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536))
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
                        }

                        linesChanged = File.ReadLines(resolvedPath, metadata.Encoding).Count();
                    }
                    else
                    {
                        // 行範囲を置換（1パスストリーミング）
                        using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
                        using (var writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536))
                        {
                            if (!enumerator.MoveNext())
                            {
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
                                        // 範囲開始：置換内容を出力
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
                                        
                                        replacementDone = true;
                                        linesChanged = endLine - startLine + 1;
                                    }
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
                                        int count = (line.Length - line.Replace(OldValue, "").Length) / OldValue.Length;
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
}