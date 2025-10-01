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
                var metadata = TextFileUtility.DetectFileMetadata(resolvedPath);
                
                int replacementCount = 0;
                var isLiteral = ParameterSetName == "Literal";
                var regex = isLiteral ? null : new Regex(Pattern);
                
                int startLine = LineRange != null && LineRange.Length > 0 ? LineRange[0] : 1;
                int endLine = LineRange != null && LineRange.Length > 1 ? LineRange[1] : int.MaxValue;

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
                        var backupTemp = resolvedPath + ".tmp";
                        if (File.Exists(backupTemp))
                        {
                            File.Delete(backupTemp);
                        }
                        
                        File.Move(resolvedPath, backupTemp);
                        File.Move(tempFile, resolvedPath);
                        File.Delete(backupTemp);

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
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "UpdateFailed", ErrorCategory.WriteError, resolvedPath));
            }
        }
    }
}
