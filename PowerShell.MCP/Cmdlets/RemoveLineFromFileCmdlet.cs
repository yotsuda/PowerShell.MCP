using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets
{
    [Cmdlet(VerbsCommon.Remove, "LineFromFile", SupportsShouldProcess = true)]
    public class RemoveLineFromFileCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
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
                var resolvedPaths = GetResolvedProviderPathFromPSPath(path, out _);
                
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

                        if (ParameterSetName == "LineRange")
                        {
                            (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
                        }
                        else
                        {
                            regex = new Regex(Pattern);
                        }

                        if (ShouldProcess(resolvedPath, "Remove lines from file"))
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
                                using (var enumerator = File.ReadLines(resolvedPath, metadata.Encoding).GetEnumerator())
                                using (var writer = new StreamWriter(tempFile, false, metadata.Encoding, 65536))
                                {
                                    if (!enumerator.MoveNext())
                                    {
                                        // 空ファイル
                                        File.Delete(tempFile);
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
                                            // 最初の出力行以外は改行を前に追加
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
                                            // 最終行：元々末尾改行があった場合のみ追加
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

                                WriteInformation(new InformationRecord(
                                    $"Removed {linesRemoved} line(s) from {System.IO.Path.GetFileName(resolvedPath)}",
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
                        WriteError(new ErrorRecord(ex, "RemoveLineFailed", ErrorCategory.WriteError, resolvedPath));
                    }
                }
            }
        }
    }
}