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
        public string Path { get; set; } = null!;

        [Parameter(Position = 1)]
        public object Content { get; set; } = null!;

        [Parameter]
        public int[]? LineRange { get; set; }

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
                string[] contentLines = TextFileUtility.ConvertToStringArray(Content);
                
                var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
                bool isFullFileReplace = LineRange == null;

                if (ShouldProcess(resolvedPath, "Set file content"))
                {
                    if (Backup)
                    {
                        var backupPath = TextFileUtility.CreateBackup(resolvedPath);
                        WriteVerbose($"Created backup: {backupPath}");
                    }

                    var tempFile = System.IO.Path.GetTempFileName();

                    try
                    {
                        // 共通メソッドを使用
                        int linesChanged = TextFileUtility.ReplaceLineRange(
                            resolvedPath,
                            tempFile,
                            metadata,
                            LineRange,
                            contentLines);

                        // アトミックに置換
                        TextFileUtility.ReplaceFileAtomic(resolvedPath, tempFile);

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

    }
}
