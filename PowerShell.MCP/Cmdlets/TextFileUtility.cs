using System.Text;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// テキストファイル編集のためのユーティリティクラス
/// メタデータの検出・保持、バックアップ、アトミック書き込みを提供
/// LLM最適化：予測可能で安全な操作
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
        var fileInfo = new FileInfo(filePath);
        
        // 空ファイルの場合はデフォルトのメタデータを返す
        if (fileInfo.Length == 0)
        {
            return new FileMetadata
            {
                Encoding = new UTF8Encoding(false),
                NewlineSequence = Environment.NewLine,
                HasTrailingNewline = false
            };
        }
        
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
    /// エンコーディングを検出（BOM検出 + ヒューリスティック）
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
    /// エンコーディングを取得（明示的指定または自動検出）
    /// </summary>
    public static Encoding GetEncoding(string filePath, string? encodingName)
    {
        if (!string.IsNullOrEmpty(encodingName))
        {
            try
            {
                // 特別なエイリアスの処理
                return encodingName.ToLowerInvariant() switch
                {
                    "utf-8" or "utf8" => new UTF8Encoding(false),
                    "utf-8-bom" or "utf8-bom" or "utf8bom" => new UTF8Encoding(true),
                    "shift_jis" or "shift-jis" or "shiftjis" or "sjis" => Encoding.GetEncoding("shift_jis"),
                    "euc-jp" or "euc_jp" or "eucjp" => Encoding.GetEncoding("euc-jp"),
                    "iso-2022-jp" or "iso2022jp" or "iso2022-jp" or "jis" => Encoding.GetEncoding("iso-2022-jp"),
                    "ascii" => Encoding.ASCII,
                    "unicode" or "utf-16" or "utf16" or "utf-16le" or "utf16le" => Encoding.Unicode,
                    "utf-16be" or "utf16be" => Encoding.BigEndianUnicode,
                    "utf-32" or "utf32" or "utf-32le" or "utf32le" => Encoding.UTF32,
                    "utf-32be" or "utf32be" => new UTF32Encoding(true, true),
                    _ => Encoding.GetEncoding(encodingName)
                };
            }
            catch
            {
                // エンコーディング名が無効な場合は自動検出にフォールバック
                return DetectEncoding(filePath);
            }
        }

        return DetectEncoding(filePath);
    }

    /// <summary>
    /// ファイルのメタデータを検出（明示的エンコーディング指定対応）
    /// </summary>
    public static FileMetadata DetectFileMetadata(string filePath, string? encodingName)
    {
        var fileInfo = new FileInfo(filePath);
        
        // 空ファイルの場合はデフォルトのメタデータを返す
        if (fileInfo.Length == 0)
        {
            var defaultEncoding = !string.IsNullOrEmpty(encodingName) 
                ? GetEncoding(filePath, encodingName) 
                : new UTF8Encoding(false);
            
            return new FileMetadata
            {
                Encoding = defaultEncoding,
                NewlineSequence = Environment.NewLine,
                HasTrailingNewline = false
            };
        }
        
        var encoding = GetEncoding(filePath, encodingName);
        var (newline, hasTrailing) = DetectNewline(filePath, encoding);

        return new FileMetadata
        {
            Encoding = encoding,
            NewlineSequence = newline,
            HasTrailingNewline = hasTrailing
        };
    }

    /// <summary>
    /// 改行コードと末尾改行を検出（ストリーミング方式、大きなファイルに対応）
    /// </summary>
    public static (string NewlineSequence, bool HasTrailingNewline) DetectNewline(string filePath, Encoding encoding)
    {
        using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new StreamReader(stream, encoding))
        {
            if (stream.Length == 0)
                return (Environment.NewLine, false);
            
            // 最初の改行を検出（ストリーミング）
            string detectedNewline = Environment.NewLine;
            bool foundNewline = false;
            int ch;
            
            while ((ch = reader.Read()) != -1)
            {
                if (ch == '\r')
                {
                    detectedNewline = reader.Peek() == '\n' ? "\r\n" : "\r";
                    foundNewline = true;
                    break;
                }
                else if (ch == '\n')
                {
                    detectedNewline = "\n";
                    foundNewline = true;
                    break;
                }
            }
            
            // 末尾改行を検出（末尾の数バイトのみ読む）
            bool hasTrailingNewline = false;
            
            if (stream.Length > 0)
            {
                // 末尾から最大4バイト読む（UTF-32の\rまたは\nが最大4バイト）
                long seekPosition = Math.Max(0, stream.Length - 4);
                stream.Seek(seekPosition, SeekOrigin.Begin);
                
                // 新しいStreamReaderで末尾を読む
                using (var tailReader = new StreamReader(stream, encoding, false, 1024, true))
                {
                    var tail = tailReader.ReadToEnd();
                    hasTrailingNewline = tail.EndsWith("\r\n") || tail.EndsWith("\n") || tail.EndsWith("\r");
                }
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
    /// 1パスストリーミング処理でファイルを処理（最適化版）
    /// 行を1つずつ処理し、メモリ効率的に書き込む
    /// </summary>
    public static void ProcessFileStreaming(
        string inputPath,
        string outputPath,
        FileMetadata metadata,
        Func<string, int, string> lineProcessor)
    {
        using var reader = new StreamReader(inputPath, metadata.Encoding, false, 65536); // 64KB buffer
        using var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536); // 64KB buffer

        string? currentLine = reader.ReadLine();
        if (currentLine == null)
        {
            // 空ファイル
            return;
        }
            
        int lineNumber = 1;
        string? nextLine = reader.ReadLine();
            
        while (true)
        {
            // 行を処理
            string processedLine = lineProcessor(currentLine, lineNumber);
            writer.Write(processedLine);
                
            if (nextLine != null)
            {
                // 次の行があるので改行を追加
                writer.Write(metadata.NewlineSequence);
                lineNumber++;
                currentLine = nextLine;
                nextLine = reader.ReadLine();
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

    /// <summary>
    /// オブジェクトを文字列配列に変換
    /// </summary>
    public static string[] ConvertToStringArray(object? content)
    {
        if (content == null)
            return Array.Empty<string>();

        if (content is string str)
        {
            return str.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        }
        else if (content is string[] arr)
        {
            return arr;
        }
        else if (content is object[] objArr)
        {
            // object[] の場合、各要素を処理
            var result = new List<string>();
            foreach (var item in objArr)
            {
                if (item is string s)
                {
                    // 文字列の場合、改行で分割
                    result.AddRange(s.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
                }
                else if (item != null)
                {
                    result.Add(item.ToString() ?? string.Empty);
                }
            }
            return result.ToArray();
        }
        else if (content is System.Collections.IEnumerable enumerable)
        {
            return enumerable.Cast<object>().Select(o => o?.ToString() ?? string.Empty).ToArray();
        }
        else
        {
            return [content.ToString()];
        }
    }

    /// <summary>
    /// 一時ファイルを使ってアトミックにファイルを置換
    /// </summary>
    public static void ReplaceFileAtomic(string targetPath, string tempFile)
    {
        // 新規ファイルの場合は単純に移動
        if (!File.Exists(targetPath))
        {
            File.Move(tempFile, targetPath);
            return;
        }

        // 既存ファイルの場合はアトミック置換
        var backupTemp = targetPath + ".tmp";
        if (File.Exists(backupTemp))
        {
            File.Delete(backupTemp);
        }

        File.Move(targetPath, backupTemp);
        File.Move(tempFile, targetPath);
        File.Delete(backupTemp);
    }

    /// <summary>
    /// LineRange パラメータから開始行と終了行を取得
    /// </summary>
    public static (int StartLine, int EndLine) ParseLineRange(int[]? lineRange)
    {
        if (lineRange == null || lineRange.Length == 0)
            return (1, int.MaxValue);
        
        if (lineRange.Length > 2)
        {
            throw new ArgumentException("LineRange accepts 1 or 2 values: start line, or start and end line. For example: -LineRange 5 or -LineRange 10,20");
        }
        
        int startLine = lineRange[0];
        int endLine = lineRange.Length > 1 ? lineRange[1] : startLine;
        
        return (startLine, endLine);
    }
    
    /// <summary>
    /// ファイル全体を新しい内容で置換
    /// LLM向け：シンプルで予測可能な動作
    /// </summary>
    public static (int LinesRemoved, int LinesInserted) ReplaceEntireFile(
        string inputPath,
        string outputPath,
        FileMetadata metadata,
        string[] contentLines)
    {
        int originalLineCount = 0;
        
        // 元のファイルの行数をカウント（情報提供用）
        if (File.Exists(inputPath))
        {
            using (var reader = new StreamReader(inputPath, metadata.Encoding))
            {
                while (reader.ReadLine() != null)
                {
                    originalLineCount++;
                }
            }
        }

        // ファイル全体を置換
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            if (contentLines.Length > 0)
            {
                for (int i = 0; i < contentLines.Length; i++)
                {
                    writer.Write(contentLines[i]);
                    if (i < contentLines.Length - 1 || metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                }
            }
        }
        
        return (originalLineCount, contentLines.Length);
    }

    /// <summary>
    /// 指定行範囲を置換（1パスストリーミング）
    /// LLM向け：メモリ効率的で大きなファイルに対応
    /// </summary>
    public static (int LinesRemoved, int LinesInserted) ReplaceLineRangeStreaming(
        string inputPath,
        string outputPath,
        FileMetadata metadata,
        int startLine,
        int endLine,
        string[] contentLines)
    {
        int linesChanged = 0;

        using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            if (!enumerator.MoveNext())
            {
                // 空ファイル：何もしない
                return (0, 0);
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
                    // 置換範囲より前：そのまま書き込む
                    if (!isFirstLine) writer.Write(metadata.NewlineSequence);
                    writer.Write(currentLine);
                    isFirstLine = false;
                }
                else if (lineNumber >= startLine && lineNumber <= endLine)
                {
                    // 置換範囲内：最初の行で置換内容を書き込む
                    if (!replacementDone)
                    {
                        if (contentLines.Length > 0)
                        {
                            if (!isFirstLine) writer.Write(metadata.NewlineSequence);
                            for (int i = 0; i < contentLines.Length; i++)
                            {
                                if (i > 0) writer.Write(metadata.NewlineSequence);
                                writer.Write(contentLines[i]);
                            }
                            isFirstLine = false;
                        }
                        replacementDone = true;
                        linesChanged = endLine - startLine + 1;
                    }
                    // 置換範囲内の行はスキップ
                }
                else
                {
                    // 置換範囲より後：そのまま書き込む
                    if (!isFirstLine) writer.Write(metadata.NewlineSequence);
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
                    // 最終行処理完了：元々末尾改行があった場合のみ追加
                    if (metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    break;
                }
            }
        }

        return (linesChanged, contentLines.Length);
    }

    /// <summary>
    /// カレントディレクトリからの相対パスを取得
    /// </summary>
    public static string GetRelativePath(string fromPath, string toPath)
    {
        try
        {
            // 両方のパスを正規化
            fromPath = System.IO.Path.GetFullPath(fromPath);
            toPath = System.IO.Path.GetFullPath(toPath);
                
            // 同じドライブかチェック
            if (System.IO.Path.GetPathRoot(fromPath) != System.IO.Path.GetPathRoot(toPath))
            {
                // 異なるドライブの場合は絶対パスを返す
                return toPath;
            }
                
            var fromUri = new Uri(fromPath.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()) 
                ? fromPath 
                : fromPath + System.IO.Path.DirectorySeparatorChar);
            var toUri = new Uri(toPath);
                
            var relativeUri = fromUri.MakeRelativeUri(toUri);
            var relativePath = Uri.UnescapeDataString(relativeUri.ToString());
                
            // バックスラッシュに変換（Windows）
            return relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        }
        catch
        {
            // エラー時は絶対パスを返す
            return toPath;
        }
    }
}
