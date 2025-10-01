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

        [Parameter]
        public int Context { get; set; } = 0;

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

                // Context を適用
                startLine = Math.Max(1, startLine - Context);
                endLine = endLine + Context;
            }


            // Skip/Take で必要な範囲だけを取得（1パスのみ）
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
            
            // ===== 1パス目: マッチ行番号を収集（行番号のみメモリに保持） =====
            var matchedLineNumbers = File.ReadLines(filePath, encoding)
                .Select((line, index) => new { LineNumber = index + 1, IsMatch = regex.IsMatch(line) })
                .Where(x => x.IsMatch)
                .Select(x => x.LineNumber)
                .ToHashSet();

            if (matchedLineNumbers.Count == 0)
            {
                WriteWarning("No matches found.");
                return;
            }

            // Context を適用して表示する行番号の範囲を計算
            var linesToShow = new HashSet<int>();
            foreach (var matchLine in matchedLineNumbers)
            {
                int contextStart = Math.Max(1, matchLine - Context);
                int contextEnd = matchLine + Context;
                
                for (int i = contextStart; i <= contextEnd; i++)
                {
                    linesToShow.Add(i);
                }
            }


            // ===== 2パス目: 表示範囲をLINQで効率的に取得 =====
            var displayLines = File.ReadLines(filePath, encoding)
                .Select((line, index) => new { 
                    LineNumber = index + 1, 
                    Line = line 
                })
                .Where(x => linesToShow.Contains(x.LineNumber)); // 該当行のみフィルタ（遅延評価）

            int? lastShownLine = null;
            foreach (var item in displayLines)
            {
                // 行が連続していない場合は区切り線を表示
                if (lastShownLine.HasValue && item.LineNumber - lastShownLine.Value > 1)
                {
                    WriteObject(new string('-', 6));
                }

                // マッチした行には * を付ける
                var prefix = matchedLineNumbers.Contains(item.LineNumber) ? "*" : " ";
                WriteObject($"{prefix}{item.LineNumber.ToString().PadLeft(3)}: {item.Line}");
                lastShownLine = item.LineNumber;
            }
        }
    }
}
