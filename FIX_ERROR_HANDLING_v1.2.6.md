# 🔧 エラーハンドリング修正案

## 問題の根本原因

**ファイル**: TextFileUtility.cs の ReplaceLineRangeStreaming メソッド（420行目）

**問題点**:
\\\csharp
// 454-473行目: 範囲チェックなしでループ処理
else if (lineNumber >= startLine && lineNumber <= endLine)
{
    // 置換範囲内：最初の行で置換内容を書き込む
    if (!replacementDone)
    {
        // contentLines を書き込む
        replacementDone = true;
        linesChanged = endLine - startLine + 1;  // ← 実際の行数を考慮していない
    }
}
\\\

ファイルが5行で endLine=20 を指定しても、ループが5行で終わるため：
- lineNumber は 1→2→3→4→5 で終了
- startLine=10, endLine=20 の場合、454行目の条件に一度も入らない
- 結果として何も置換されない（正しい）

しかし、startLine=3, endLine=10 の場合：
- lineNumber が 3,4,5 の時に条件に入る
- linesChanged = 10 - 3 + 1 = 8 と計算される（実際は3行のみ）
- 3行すべてがスキップされ、contentLines が書き込まれる

## 修正案

### 方針A: 厳格なバリデーション（推奨）

UpdateLinesInFileCmdlet.cs の ProcessFile メソッドに追加：

\\\csharp
private void ProcessFile(string originalPath, string resolvedPath)
{
    bool fileExists = File.Exists(resolvedPath);

    try
    {
        // メタデータの取得または生成
        TextFileUtility.FileMetadata metadata = fileExists
            ? TextFileUtility.DetectFileMetadata(resolvedPath, Encoding)
            : CreateNewFileMetadata(resolvedPath);

        string[] contentLines = TextFileUtility.ConvertToStringArray(Content);
        var (startLine, endLine) = TextFileUtility.ParseLineRange(LineRange);
        bool isFullFileReplace = LineRange == null;

        // ★★★ 追加：範囲外チェック ★★★
        if (fileExists && !isFullFileReplace)
        {
            int actualLineCount = File.ReadLines(resolvedPath, metadata.Encoding).Count();
            
            if (startLine > actualLineCount)
            {
                throw new ArgumentException(
                    \$"Line range {startLine}-{endLine} is out of bounds. File has only {actualLineCount} line(s).",
                    nameof(LineRange));
            }
            
            if (endLine > actualLineCount)
            {
                WriteWarning(
                    \$"End line {endLine} exceeds file length ({actualLineCount} lines). " +
                    \$"Will process up to line {actualLineCount}.");
                    
                // endLine を実際の行数に調整
                endLine = actualLineCount;
            }
        }
        // ★★★ 追加終わり ★★★

        string actionDescription = GetActionDescription(fileExists, isFullFileReplace, startLine, endLine);
        
        // ... 以下既存のコード
    }
}
\\\

### 修正のポイント

1. **startLine が範囲外**: エラーを投げる（完全に無効な操作）
2. **endLine が範囲外**: 警告を出して自動調整（部分的に有効な操作）
3. **パフォーマンス**: \Count()\ の呼び出しは必要最小限（LineRange指定時のみ）

### 修正箇所

**ファイル**: C:\\MyProj\\PowerShell.MCP\\PowerShell.MCP\\Cmdlets\\UpdateLinesInFileCmdlet.cs
**行番号**: 63-97行目の ProcessFile メソッド内
**追加位置**: 75行目（\ar (startLine, endLine) = ...\ の後）

