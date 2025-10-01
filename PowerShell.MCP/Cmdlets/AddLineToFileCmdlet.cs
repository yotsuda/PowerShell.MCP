using System.Management.Automation;

namespace PowerShell.MCP.Cmdlets
{
    [Cmdlet(VerbsCommon.Add, "LineToFile", SupportsShouldProcess = true)]
    public class AddLineToFileCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0)]
        public string Path { get; set; } = null!;

        [Parameter(Mandatory = true, Position = 1)]
        public object Content { get; set; } = null!;

        [Parameter(ParameterSetName = "LineNumber", Mandatory = true)]
        public int LineNumber { get; set; }

        [Parameter(ParameterSetName = "AtEnd")]
        public SwitchParameter AtEnd { get; set; }

        [Parameter]
        public SwitchParameter Backup { get; set; }

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
                string[] contentLines = TextFileUtility.ConvertToStringArray(Content);

                int insertAt = AtEnd.IsPresent ? int.MaxValue : LineNumber;

                if (ShouldProcess(resolvedPath, $"Add {contentLines.Length} line(s) at line {insertAt}"))
                {
                    if (Backup)
                    {
                        var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                        WriteVerbose($"Created backup: {backupPath}");
                    }

                    var tempFile = System.IO.Path.GetTempFileName();
                    bool inserted = false;

                    try
                    {
                        using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
                        using (var writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536))
                        {
                            if (!enumerator.MoveNext())
                            {
                                // 空ファイル：先頭に挿入
                                WriteContentLines(writer, contentLines, metadata, false);
                                inserted = true;
                            }
                            else
                            {
                                int lineNumber = 1;
                                string currentLine = enumerator.Current;
                                bool hasNext = enumerator.MoveNext();

                                while (true)
                                {
                                    // 挿入位置に到達したら、新しい内容を先に書き込む
                                    if (!inserted && lineNumber == insertAt)
                                    {
                                        WriteContentLines(writer, contentLines, metadata, true);
                                        inserted = true;
                                    }

                                    // 現在の行を書き込む
                                    writer.Write(currentLine);

                                    if (hasNext)
                                    {
                                        writer.Write(metadata.NewlineSequence);
                                        lineNumber++;
                                        currentLine = enumerator.Current;
                                        hasNext = enumerator.MoveNext();
                                    }
                                    else
                                    {
                                        // 最終行の後に挿入（AtEnd）
                                        if (!inserted)
                                        {
                                            writer.Write(metadata.NewlineSequence);
                                            WriteContentLines(writer, contentLines, metadata, false);
                                            inserted = true;
                                        }
                                        else if (metadata.HasTrailingNewline)
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

                        WriteInformation(new InformationRecord(
                            $"Added {contentLines.Length} line(s) to {System.IO.Path.GetFileName(resolvedPath)} at line {insertAt}",
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
                WriteError(new ErrorRecord(ex, "AddLineFailed", ErrorCategory.WriteError, resolvedPath));
            }
        }

        private static void WriteContentLines(StreamWriter writer, string[] contentLines, TextFileUtility.FileMetadata metadata, bool addTrailingNewline)
        {
            for (int i = 0; i < contentLines.Length; i++)
            {
                writer.Write(contentLines[i]);

                // 各行の後に改行を追加（最後の行は条件による）
                if (i < contentLines.Length - 1 || addTrailingNewline)
                {
                    writer.Write(metadata.NewlineSequence);
                }
            }
        }

    }
}
