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
        return FileMetadataHelper.DetectFileMetadata(filePath);
    }

    /// <summary>
    /// エンコーディングを検出（BOM検出 + ヒューリスティック）
    /// </summary>
    public static Encoding DetectEncoding(string filePath)
    {
        return EncodingHelper.DetectEncoding(filePath);
    }
    /// <summary>
    /// エンコーディングを取得（明示的指定または自動検出）
    /// </summary>
    public static Encoding GetEncoding(string filePath, string? encodingName)
    {
        return EncodingHelper.GetEncoding(filePath, encodingName);
    }

    /// <summary>
    /// ファイルのメタデータを検出（明示的エンコーディング指定対応）
    /// </summary>
    public static FileMetadata DetectFileMetadata(string filePath, string? encodingName)
    {
        return FileMetadataHelper.DetectFileMetadata(filePath, encodingName);
    }

    /// <summary>
    /// 改行コードと末尾改行を検出
    /// </summary>
    public static (string NewlineSequence, bool HasTrailingNewline) DetectNewline(
        string filePath, Encoding encoding)
    {
        return FileMetadataHelper.DetectNewline(filePath, encoding);
    }

    /// <summary>
    /// バックアップファイルを作成
    /// </summary>
    /// <returns>作成したバックアップファイルのパス</returns>
    public static string CreateBackup(string filePath)
    {
        return FileOperationHelper.CreateBackup(filePath);
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
            return [];

        if (content is string str)
        {
            return str.Split(["\r\n", "\n"], StringSplitOptions.None);
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
            return [content.ToString() ?? ""];
        }
    }

    /// <summary>
    /// 一時ファイルを使ってアトミックにファイルを置換
    /// </summary>
    public static void ReplaceFileAtomic(string targetPath, string tempFile)
    {
        FileOperationHelper.ReplaceFileAtomic(targetPath, tempFile);
    }

    /// <summary>
    /// LineRange パラメータから開始行と終了行を取得
    /// 0以下の値は最終行を表す（例: -LineRange 100,-1 で100行目から最後まで）
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
        int endLine;
        
        if (lineRange.Length > 1)
        {
            // 2つの値が指定された場合
            endLine = lineRange[1] <= 0 ? int.MaxValue : lineRange[1];
        }
        else
        {
            // 1つの値のみ指定された場合は、その行のみ
            endLine = startLine;
        }
        
        return (startLine, endLine);
    }
    
    /// <summary>
    /// ファイル全体を新しい内容で置換
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
        return FileOperationHelper.ReplaceEntireFile(inputPath, outputPath, metadata, contentLines);
    }

    /// <summary>
    /// 指定行範囲を置換（1パスストリーミング）
    /// LLM向け：メモリ効率的で大きなファイルに対応
    /// </summary>
    public static (int LinesRemoved, int LinesInserted, string? WarningMessage) ReplaceLineRangeStreaming(
        string inputPath,
        string outputPath,
        FileMetadata metadata,
        int startLine,
        int endLine,
        string[] contentLines)
    {
        int linesChanged = 0;
        string? warningMessage = null;
        int actualLineCount = 0;

        using (var enumerator = File.ReadLines(inputPath, metadata.Encoding).GetEnumerator())
        using (var writer = new StreamWriter(outputPath, false, metadata.Encoding, 65536))
        {
            if (!enumerator.MoveNext())
            {
                // 空ファイル：startLine が指定されていればエラー
                if (startLine > 0)
                {
                    throw new ArgumentException(
                        $"Line range {startLine}-{endLine} is out of bounds. File has only 0 line(s).",
                        nameof(startLine));
                }
                return (0, 0, null);
            }

            int lineNumber = 1;
            string currentLine = enumerator.Current;
            bool hasNext = enumerator.MoveNext();
            bool replacementDone = false;
            bool isFirstLine = true;

            while (true)
            {
                actualLineCount = lineNumber;

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
                    // 最終行処理完了
                    actualLineCount = lineNumber;
                    
                    // 範囲外チェック（ファイル終端で判定）
                    if (startLine > actualLineCount)
                    {
                        throw new ArgumentException(
                            $"Line range {startLine}-{endLine} is out of bounds. File has only {actualLineCount} line(s).",
                            nameof(startLine));
                    }
                    
                    if (endLine > actualLineCount)
                    {
                        warningMessage = $"End line {endLine} exceeds file length ({actualLineCount} lines). Will process up to line {actualLineCount}.";
                        linesChanged = actualLineCount - startLine + 1;
                    }
                    else
                    {
                        linesChanged = endLine - startLine + 1;
                    }
                    
                    if (metadata.HasTrailingNewline)
                    {
                        writer.Write(metadata.NewlineSequence);
                    }
                    break;
                }
            }
        }

        return (linesChanged, contentLines.Length, warningMessage);
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
    
    /// <summary>
    /// 必要に応じてエンコーディングを UTF-8 にアップグレード
    /// Content に非 ASCII 文字が含まれ、現在のエンコーディングが ASCII の場合、UTF-8 にアップグレード
    /// </summary>
    /// <param name="metadata">ファイルメタデータ（エンコーディングが更新される可能性あり）</param>
    /// <param name="contentLines">追加/更新する内容の行配列</param>
    /// <param name="encodingExplicitlySpecified">エンコーディングが明示的に指定されているか</param>
    /// <param name="upgradeMessage">アップグレードされた場合のメッセージ（アップグレードされない場合は null）</param>
    /// <returns>エンコーディングがアップグレードされた場合は true、それ以外は false</returns>
    public static bool TryUpgradeEncodingIfNeeded(
        FileMetadata metadata, 
        string[] contentLines, 
        bool encodingExplicitlySpecified,
        out string? upgradeMessage)
    {
        return EncodingHelper.TryUpgradeEncodingIfNeeded(
            metadata, contentLines, encodingExplicitlySpecified, out upgradeMessage);
    }
}
