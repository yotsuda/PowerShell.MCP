using System;
using System.IO;
using System.Text;

namespace PowerShell.MCP.Cmdlets
{
    /// <summary>
    /// テキストファイル編集のためのユーティリティクラス
    /// メタデータの検出・保持、バックアップ、アトミック書き込みを提供
    /// </summary>
    public static class TextFileUtility
    {
        public class FileMetadata
        {
            public Encoding Encoding { get; set; }
            public string NewlineSequence { get; set; }
            public bool HasTrailingNewline { get; set; }
        }

        /// <summary>
        /// ファイルのメタデータを検出
        /// </summary>
        public static FileMetadata DetectFileMetadata(string filePath)
        {
            var encoding = DetectEncoding(filePath);
            var (newline, hasTrailing) = DetectNewline(filePath, encoding);

            return new FileMetadata
            {
                Encoding = encoding,
                NewlineSequence = newline,
                HasTrailingNewline = hasTrailing
            };
        }

        /// <summary>
        /// エンコーディングを検出（BOM検出）
        /// </summary>
        public static Encoding DetectEncoding(string filePath)
        {
            var bytes = new byte[4];
            using (var fs = File.OpenRead(filePath))
            {
                int bytesRead = fs.Read(bytes, 0, 4);
                
                if (bytesRead >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    return new UTF8Encoding(true); // UTF-8 with BOM
                
                if (bytesRead >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
                    return Encoding.UTF32; // UTF-32 LE
                
                if (bytesRead >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
                    return new UTF32Encoding(true, true); // UTF-32 BE
                
                if (bytesRead >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                    return Encoding.Unicode; // UTF-16 LE
                
                if (bytesRead >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                    return Encoding.BigEndianUnicode; // UTF-16 BE
            }

            // BOMなし: UTF-8を仮定
            return new UTF8Encoding(false);
        }

        /// <summary>
        /// 改行コードと末尾改行を検出
        /// </summary>
        public static (string NewlineSequence, bool HasTrailingNewline) DetectNewline(string filePath, Encoding encoding)
        {
            using (var reader = new StreamReader(filePath, encoding))
            {
                string detectedNewline = Environment.NewLine;
                bool hasTrailingNewline = false;

                // 最初の改行を検出
                int ch;
                while ((ch = reader.Read()) != -1)
                {
                    if (ch == '\r')
                    {
                        detectedNewline = reader.Peek() == '\n' ? "\r\n" : "\r";
                        break;
                    }
                    else if (ch == '\n')
                    {
                        detectedNewline = "\n";
                        break;
                    }
                }

                // 末尾改行を検出
                if (reader.BaseStream.Length > 0)
                {
                    reader.BaseStream.Seek(-1, SeekOrigin.End);
                    int lastChar = reader.Read();
                    hasTrailingNewline = (lastChar == '\n' || lastChar == '\r');
                }

                return (detectedNewline, hasTrailingNewline);
            }
        }

        /// <summary>
        /// バックアップファイルを作成
        /// </summary>
        /// <returns>作成したバックアップファイルのパス</returns>
        public static string CreateBackup(string filePath)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var backupPath = $"{filePath}.{timestamp}.bak";
            File.Copy(filePath, backupPath);
            return backupPath;
        }

        /// <summary>
        /// アトミックにファイルを書き込み（一時ファイル経由）
        /// </summary>
        public static void WriteFileAtomic(string targetPath, Action<StreamWriter> writeAction, FileMetadata metadata)
        {
            var tempFile = System.IO.Path.GetTempFileName();

            try
            {
                using (var writer = new StreamWriter(tempFile, false, metadata.Encoding))
                {
                    writeAction(writer);
                }

                // アトミックに置換
                var backupTemp = targetPath + ".tmp";
                if (File.Exists(backupTemp))
                {
                    File.Delete(backupTemp);
                }
                
                File.Move(targetPath, backupTemp);
                File.Move(tempFile, targetPath);
                File.Delete(backupTemp);
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

        /// <summary>
        /// 1パスストリーミング処理でファイルを処理
        /// 行を1つずつ処理し、メモリ効率的に書き込む
        /// </summary>
        /// <param name="inputPath">入力ファイルパス</param>
        /// <param name="outputPath">出力ファイルパス</param>
        /// <param name="metadata">ファイルメタデータ</param>
        /// <param name="lineProcessor">行処理関数 (line, lineNumber) => processedLine</param>
        public static void ProcessFileStreaming(
            string inputPath,
            string outputPath,
            FileMetadata metadata,
            Func<string, int, string> lineProcessor)
        {
            using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
            using (var writer = new StreamWriter(outputPath, false, metadata.Encoding))
            {
                if (!enumerator.MoveNext())
                {
                    // 空ファイル
                    return;
                }
                
                int lineNumber = 1;
                string currentLine = enumerator.Current;
                bool hasNext = enumerator.MoveNext();
                
                while (true)
                {
                    // 行を処理
                    string processedLine = lineProcessor(currentLine, lineNumber);
                    writer.Write(processedLine);
                    
                    if (hasNext)
                    {
                        // 次の行があるので改行を追加
                        writer.Write(metadata.NewlineSequence);
                        lineNumber++;
                        currentLine = enumerator.Current;
                        hasNext = enumerator.MoveNext();
                    }
                    else
                    {
                        // 最終行：末尾改行が元々あった場合のみ追加
                        if (metadata.HasTrailingNewline)
                        {
                            writer.Write(metadata.NewlineSequence);
                        }
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// ファイルサイズをチェック
        /// </summary>
        /// <returns>処理を続行すべきかどうか（100MB超でForce未指定の場合false）</returns>
        public static (bool ShouldContinue, string WarningMessage) CheckFileSize(string filePath, bool force)
        {
            var fileInfo = new FileInfo(filePath);
            
            if (fileInfo.Length > 100 * 1024 * 1024 && !force)
            {
                return (false, "File is larger than 100MB. Use -Force to proceed.");
            }

            if (fileInfo.Length > 50 * 1024 * 1024)
            {
                return (true, $"File is larger than 50MB ({fileInfo.Length / 1024 / 1024}MB). This may take some time.");
            }

            return (true, null);
        }
    }
}
