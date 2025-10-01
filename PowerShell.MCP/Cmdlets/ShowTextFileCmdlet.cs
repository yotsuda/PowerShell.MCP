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
        public string Path { get; set; } = null!;

        [Parameter(ParameterSetName = "LineRange")]
        [Parameter(ParameterSetName = "Pattern")]
        public int[]? LineRange { get; set; }

        [Parameter(ParameterSetName = "Pattern", Mandatory = true)]
        public string Pattern { get; set; } = null!;


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
                WriteObject($"{currentLine,4}: {line}");
                currentLine++;
            }
        }

        private void ShowWithPattern(string filePath, System.Text.Encoding encoding)
        {
            var regex = new Regex(Pattern);

            // 行範囲を取得
            int startLine = 1;
            int endLine = int.MaxValue;
            if (LineRange != null && LineRange.Length > 0)
            {
                startLine = LineRange[0];
                endLine = LineRange.Length > 1 ? LineRange[1] : startLine;
            }

            // Skip/Take で範囲を絞り込んでからパターンマッチ
            int skipCount = startLine - 1;
            int takeCount = endLine - startLine + 1;

            var linesToSearch = File.ReadLines(filePath, encoding)
                .Skip(skipCount)
                .Take(takeCount);

            int matchCount = 0;
            int lineNumber = startLine;

            foreach (var line in linesToSearch)
            {
                if (regex.IsMatch(line))
                {
                    WriteObject($"*{lineNumber.ToString().PadLeft(3)}: {line}");
                    matchCount++;
                }
                lineNumber++;
            }

            // マッチが見つからない場合は警告
            if (matchCount == 0)
            {
                WriteWarning($"No lines matched pattern: {Pattern}");
            }
        }
    }
}
