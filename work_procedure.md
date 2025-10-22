# 作業手順書：1 pass 実装の原則と手法

## 📌 概要

**目的：** すべてのファイル処理を 1 pass（ファイル1回読み込み）で完了させる

**ディレクトリ：** C:\MyProj\PowerShell.MCP

**品質基準：**
- ✅ すべての処理が 1 pass で完了すること
- ✅ File.ReadAllLines() や ReadLines().ToArray() を使用しないこと
- ✅ ファイル再読込を行わないこと
- ✅ すべての統合テストがパスすること

## 🔍 1 pass 実装の原則

### 1. ファイル全体を読み込まない

**❌ 避けるべきパターン：**
```csharp
// ファイル全体をメモリに読み込む
var lines = File.ReadAllLines(filePath);  
var lines = File.ReadLines(filePath).ToArray();
```

**✅ 推奨パターン：**
```csharp
// ストリーミング処理
var enumerator = File.ReadLines(filePath, encoding).GetEnumerator();
// または
using var reader = new StreamReader(filePath, encoding);
```

### 2. 必要なデータのみバッファリング

**コンテキスト表示用バッファ：**
- マッチ行の前後2行 + マッチ行自体のみ保持
- Dictionary<int, string> で行番号とコンテンツを管理

### 3. rotate buffer パターン

**目的：** 前N行を常時保持し、マッチ時に即座にコンテキストとして使用

**実装例（前2行保持）：**
```csharp
string? prevPrevLine = null;
string? prevLine = null;

while (hasNext)
{
    // 現在の行を処理
    if (matched)
    {
        // 前2行をバッファに追加
        if (prevPrevLine != null)
            contextBuffer[lineNumber - 2] = prevPrevLine;
        if (prevLine != null)
            contextBuffer[lineNumber - 1] = prevLine;
    }
    
    // rotate buffer 更新（元の行を保存）
    prevPrevLine = prevLine;
    prevLine = currentLine;
    
    lineNumber++;
    currentLine = enumerator.Current;
    hasNext = enumerator.MoveNext();
}
```

### 4. 後続コンテキストカウンタ

**目的：** マッチ後のN行を効率的に収集

**実装例（後2行収集）：**
```csharp
int afterMatchCounter = 0;

while (hasNext)
{
    if (matched)
    {
        // カウンタをセット
        afterMatchCounter = 2;
    }
    else
    {
        // 後続コンテキストの収集
        if (afterMatchCounter > 0)
        {
            contextBuffer[lineNumber] = currentLine;
            afterMatchCounter--;
        }
    }
}
```

### 5. 効率的なファイル処理パターン

**GetEnumerator() + hasNext パターン（推奨）：**
```csharp
var enumerator = File.ReadLines(filePath, encoding).GetEnumerator();
bool hasLines = enumerator.MoveNext();

if (!hasLines)
{
    // 空ファイル処理
    return;
}

string currentLine = enumerator.Current;
bool hasNext = enumerator.MoveNext();

while (true)
{
    // 現在の行を処理
    writer.Write(currentLine);
    
    // 次の行がある場合のみ改行を追加
    if (hasNext)
    {
        writer.Write(newlineSequence);
        currentLine = enumerator.Current;
        hasNext = enumerator.MoveNext();
    }
    else
    {
        break;
    }
}

// 元のファイルに末尾改行があれば保持
if (metadata.HasTrailingNewline)
{
    writer.Write(newlineSequence);
}
```

**メリット：**
- ✅ 次の行の有無を hasNext フラグで高速判定
- ✅ reader.Peek() よりもオーバーヘッドが少ない
- ✅ 最終行の改行を正確に制御

**❌ 非推奨パターン：**
```csharp
// reader.Peek() は毎回オーバーヘッドがある
while ((line = reader.ReadLine()) != null)
{
    writer.Write(line);
    if (reader.Peek() >= 0)  // ❌ 毎行で Peek() を呼び出す
    {
        writer.Write(newlineSequence);
    }
}
```

### 6. 改行コードと末尾改行の保持

```csharp
// メタデータ検出（エンコーディング、改行コード、末尾改行）
var metadata = TextFileUtility.DetectFileMetadata(filePath);

// StreamWriter に改行コードを設定
writer.NewLine = metadata.NewlineSequence;

// 処理完了後、末尾改行を保持
if (metadata.HasTrailingNewline)
{
    writer.Write(metadata.NewlineSequence);
}
```

## 🔧 実装済み cmdlet

### Add-LinesToFile
- rotate buffer で末尾追加時のコンテキスト表示
- GetEnumerator() + hasNext パターン

### Update-LinesInFile
- 削除時の 1 pass 化（OutputDeleteContext メソッド削除）

### Update-MatchInFile
- rotate buffer + 後続コンテキストカウンタ
- GetEnumerator() + hasNext パターン
- ファイル再読込なしでコンテキスト表示

### Show-TextFile
- rotate buffer + 後続コンテキストカウンタ
- GetEnumerator() + hasNext パターン
- ファイル再読込なしでコンテキスト表示
- CalculateAndMergeRangesFromBuffer + OutputFromBuffer パターン

## 💡 重要な注意点

### rotate buffer の保存内容
- **置換前の元の行** を保存（置換後ではない）
- マッチ行のコンテキストバッファには **反転表示付きの置換後の行** を保存

### 空ファイル/新規ファイル
- 末尾追加時のコンテキスト表示は不要（自明なため）

### パフォーマンス
- rotate buffer を常に動作させる（条件分岐なし）
- 参照の代入（ポインタコピー）は文字列のコピーではないため、パフォーマンス影響なし

---

**作成日時:** 2025-10-22 11:15
**最終更新:** 2025-10-22 14:35
**バージョン:** 1.4

## ✅ 解決済みの問題

### Update-LinesInFile / Update-MatchInFile のコンテキスト表示

**問題：**
- 更新処理後のコンテキスト表示が**更新前**の内容を表示していた
- ユーザーは更新**後**の内容を確認したい

**解決策：**

**Update-LinesInFile.cs:**
- 削除時のみ古い行を contextBuffer に保存するように修正
- 更新時は新しい内容のみを contextBuffer に保存
```csharp
// 削除時のみ：コンテキスト範囲内の前後行も保存（削除前のコンテキスト）
if (deletedLines != null)
{
    deletedLines[currentLine] = line;
    
    if (contextBuffer != null && currentLine >= contextStart && currentLine <= endLine)
    {
        contextBuffer[currentLine] = line;
    }
}
```

**Update-MatchInFile.cs:**
- `TryAdd` を使用して、すでに更新済みの行を上書きしないように修正
- rotate buffer から前2行を追加する際、更新済みの行は保持
```csharp
// 前2行をコンテキストバッファに追加（rotate bufferから）
// ただし、すでに更新済みの行は上書きしない
if (prevPrevLine != null)
{
    contextBuffer.TryAdd(lineNumber - 2, prevPrevLine);
}
if (prevLine != null)
{
    contextBuffer.TryAdd(lineNumber - 1, prevLine);
}
```

**修正日時:** 2025-10-22 13:30

---

## 🔧 トラブルシューティング

### コンパイルエラー: CS1513 (} expected)

**原因パターン：**
1. **重複コード**：編集ミスで同じコードブロックが2回存在
2. **括弧のバランス崩れ**：開き括弧 `{` と閉じ括弧 `}` の数が不一致
3. **コメントアウト漏れ**：削除予定のコードが残存

**診断手順：**
1. エラー行番号周辺のコード構造を確認（±50行）
2. `while`, `if`, `for` などの制御構造の開始/終了を追跡
3. インデントレベルで論理構造を確認
4. バックアップファイルと比較して変更箇所を特定

**修正アプローチ：**
1. **段階的修正**：複数のバックアップを作成しながら、一つずつ修正
2. **構文の最小化**：まず構文エラーを解消（コメントアウトでも可）
3. **ビルド確認**：各修正後にビルドして問題を分離
4. **機能復元**：構文が正しくなったら、必要な機能を段階的に追加

**今回のケース：**
- 重複する `while (true)` ループ（286-297行と298行以降）
- ギャップ検出コードの配置ミス
- 解決策：重複削除 → ギャップ検出復元 → 括弧調整
## 📝 学んだこと（2025-10-22）

### テストカバレッジの拡充
- すべてのcmdletに対してAdditionalEdgeCases.Tests.ps1を追加
- エッジケース、境界値、エラーハンドリングのテストを充実
- ユニットテスト96個、統合テスト272個を維持

### テスト作成のベストプラクティス
- 各cmdletのエッジケースを網羅的にテスト
- エラーメッセージの検証を含める
- 境界値（0, -1, 範囲外）のテストを追加


## 📝 学んだこと（2025-10-22 18:40）

### コンテキスト行のハイライト処理

**問題：**
Show-TextFile でマッチ行のみが反転表示され、コンテキスト行にマッチが含まれていても反転表示されなかった。

**解決策：**
- ヘルパーメソッド `ApplyHighlightingIfMatched` を追加
- コンテキスト行を contextBuffer に追加する際、行がマッチを含むかチェック
- マッチする場合は反転表示を適用してから保存
- これにより、grep --color と同じ動作（すべてのマッチを反転表示）を実現

**実装パターン：**
```csharp
private string ApplyHighlightingIfMatched(
    string line, 
    Func<string, bool> matchPredicate, 
    string matchValue, 
    bool isRegex, 
    string reverseOn, 
    string reverseOff)
{
    if (!matchPredicate(line)) return line;
    
    if (isRegex)
    {
        var regex = new Regex(matchValue, RegexOptions.Compiled);
        return regex.Replace(line, m => $"{reverseOn}{m.Value}{reverseOff}");
    }
    else
    {
        return line.Replace(matchValue, $"{reverseOn}{matchValue}{reverseOff}");
    }
}
```

**使用例：**
```csharp
// 前2行のコンテキスト行にも反転表示を適用
if (prevPrevLine != null)
{
    contextBuffer[lineNumber - 2] = ApplyHighlightingIfMatched(
        prevPrevLine, matchPredicate, matchValue, isRegex, reverseOn, reverseOff);
}
```

---

## 📝 学んだこと（2025-10-22）
## 📝 Pester エラー出力の簡潔化（2025-10-22）

### 最も簡潔な Pester 設定

**PesterConfiguration.psd1：**
```powershell
@{
    Output = @{
        Verbosity = 'Minimal'              # 最小限の出力
        StackTraceVerbosity = 'None'       # スタックトレースを非表示
        CIFormat = 'None'
    }
    Debug = @{
        ShowFullErrors = $false            # 完全なエラーを非表示
    }
}
```

### 制限事項

**Pester の設定では解決できない問題：**
- PowerShell が例外をラップするため、内部例外とラッパー例外の両方が表示される
- 例：ArgumentNullException と MethodInvocationException が両方表示される
- Pester は PowerShell の例外フォーマットをそのまま表示する仕様

**さらに簡潔にする方法：**
1. テストコード内で例外をキャッチして簡潔なメッセージを表示
2. カスタムのエラーフォーマッターを実装（高度）
3. テスト実行スクリプトで出力をフィルタリング

## 📝 学んだこと（2025-10-22 20:45）

### 整数オーバーフロー問題

**問題：**
LineRange が null の場合、ParseLineRange(null) は (1, int.MaxValue) を返す。
この endLine に対して ndLine + 2 を計算すると、整数オーバーフローが発生して負の値になる。

**症状：**
`csharp
int contextEndLine = endLine + 2;  // int.MaxValue + 2 → 負の値
if (lineNumber >= contextEndLine && afterMatchCounter == 0)  // すぐに true になる
`
結果として、ループが1行目で即座に終了し、マッチが見つからない。

**解決策：**
`csharp
int contextEndLine = (endLine == int.MaxValue) ? int.MaxValue : endLine + 2;
`

**教訓：**
- int.MaxValue を使用する際は、算術演算でオーバーフローが起こらないか注意する
- 特に「ファイル全体」を表現するために int.MaxValue を使う場合、+1, +2 などの演算は危険
- 条件付き演算子で事前にチェックする

## 📝 学んだこと（2025-10-22 20:58）

### テストでのファイルパス区切り文字の扱い

**問題：**
Show-TextFile の出力にはヘッダー行（==> C:\path <==）が含まれ、Windows のパス区切り文字 : が正規表現 $_ -match ":" にマッチしてしまう。

**解決策：**
マッチ行のみを選択するには、行番号パターンを使用：
`powershell
# ❌ 間違い：ヘッダー行もマッチする
$result | Where-Object { $_ -match ":" }

# ✅ 正しい：行番号付き行のみマッチ
$result | Where-Object { $_ -match "^\s+\d+:" }
`

**パターン解説：**
- ^ : 行頭
- \s+ : 1つ以上の空白
- \d+ : 1つ以上の数字（行番号）
- : : コロン（マッチ行のマーカー）

**適用例：**
- 空行マッチ検証: $_ -match "^\s+2:" で2行目のみ選択
- マッチ行カウント: ( | Where-Object {  -match "^\s+\d+:" }).Count

---