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
        [Parameter(Mandatory = true, Position = 0)]
        public string Path { get; set; }

        [Parameter(ParameterSetName = "LineRange", Mandatory = true)]
        public int[] LineRange { get; set; }

        [Parameter(ParameterSetName = "Pattern", Mandatory = true)]
        public string Pattern { get; set; }

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

                int startLine = int.MaxValue;
                int endLine = int.MaxValue;
                Regex regex = null;

                if (ParameterSetName == "LineRange")
                {
                    startLine = LineRange[0];
                    endLine = LineRange.Length > 1 ? LineRange[1] : startLine;
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
                            return;
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
