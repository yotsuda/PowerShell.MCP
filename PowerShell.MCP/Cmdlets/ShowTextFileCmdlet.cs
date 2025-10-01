using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.RegularExpressions;

namespace PowerShell.MCP.Cmdlets
{
    [Cmdlet(VerbsCommon.Show, "TextFile")]
    public class ShowTextFileCmdlet : PSCmdlet
    {
        [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
        public string Path { get; set; }

        [Parameter(ParameterSetName = "LineRange")]
        public int[] LineRange { get; set; }

        [Parameter(ParameterSetName = "Pattern", Mandatory = true)]
        public string Pattern { get; set; }


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
                var encoding = TextFileUtility.DetectEncoding(resolvedPath);

                if (!string.IsNullOrEmpty(Pattern))
                {
                    ShowWithPattern(resolvedPath, encoding);
                }
                else
                {
                    ShowWithLineRange(resolvedPath, encoding);
                }
            }
            catch (Exception ex)
            {
                WriteError(new ErrorRecord(ex, "ShowTextFileFailed", ErrorCategory.ReadError, resolvedPath));
            }
        }

        private void ShowWithLineRange(string filePath, System.Text.Encoding encoding)
        {
            int startLine = 1;
            int endLine = int.MaxValue;

            if (LineRange != null && LineRange.Length > 0)
            {
                startLine = LineRange[0];
                endLine = LineRange.Length > 1 ? LineRange[1] : startLine;
            }

            // Skip/Take で必要な範囲だけを取得（LINQの遅延評価で効率的）
            int skipCount = startLine - 1;
            int takeCount = endLine - startLine + 1;
            
            var lines = File.ReadLines(filePath, encoding)
                .Skip(skipCount)
                .Take(takeCount);

            int currentLine = startLine;
            foreach (var line in lines)
            {
                WriteObject($"{currentLine.ToString().PadLeft(4)}: {line}");
                currentLine++;
            }
        }

        private void ShowWithPattern(string filePath, System.Text.Encoding encoding)
        {
            var regex = new Regex(Pattern);
            
            // 1パス処理：マッチした行のみ表示
            int lineNumber = 1;
            foreach (var line in File.ReadLines(filePath, encoding))
            {
                if (regex.IsMatch(line))
                {
                    WriteObject($"*{lineNumber.ToString().PadLeft(3)}: {line}");
                }
                lineNumber++;
            }
        }
    }
}
