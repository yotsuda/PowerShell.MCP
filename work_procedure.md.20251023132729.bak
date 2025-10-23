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

## 📝 学んだこと（2025-10-22 21:19）

### Cmdlet 設計：エラー vs 警告の選択

**原則：**
ユーザーの意図が明確で、安全に続行できる場合は**警告**を使い、完全に無効な操作の場合のみ**エラー**を使う。

**Add-LinesToFile の事例：**

**エラーを出すべきケース：**
- 完全に無効な操作（例：LineNumber が 0 や負の数）
- データ損失のリスク（例：既存ファイルの上書き without confirmation）
- 意図が不明確（例：ワイルドカードで新規ファイル作成）

**警告で済むケース：**
- ユーザーの意図は明確だが、予期しない結果になる可能性がある
- 例：存在しないファイルに LineNumber 5 を指定
  - 意図：5行目に追加したい
  - 実際：新規ファイルの1行目になる
  - 対応：警告を出して続行（ユーザーが -WarningAction で制御可能）

**PowerShell の慣習：**
- Add-Content, Set-Content は存在しないファイルを作成する
- Update-*, Remove-* cmdlet は存在チェックでエラーを出す
- 「Add」は増やす操作なので、空からの開始（新規ファイル）も自然

**実装パターン：**
`csharp
if (potentiallyUnexpectedBehavior)
{
    WriteWarning("What will actually happen instead of what you might expect");
}
// Continue with the operation
`

---

## 📝 学んだこと（2025-10-22 22:08）

### Show-TextFile の真の1 pass実装

**問題：**
- Dictionary + List でバッファリングする「偽の1 pass」実装だった
- ファイルは1回しか読まないが、全マッチ行とコンテキストをメモリに保持
- 後から CalculateAndMergeRanges と OutputFromBuffer で出力

**解決策：真の1 pass実装**

**rotate buffer の設計（3変数）：**
`csharp
string? prevPrevLine = null;  // 前々行
string? prevLine = null;       // 前行
string? gapLine = null;        // ギャップ候補（1行のみ）
`

**ギャップ検出ロジック：**
- lastOutputLine で最後に出力した行番号を追跡
- lastOutputLine + 1 行目 → gapLine に保持（ギャップ候補）
- lastOutputLine + 2 行目で：
  - マッチした場合 → gapLine を出力してから新しいマッチを出力（範囲結合）
  - マッチしない場合 → 空行を挿入（ギャップが2行以上）

**リアルタイム出力パターン：**
`csharp
if (matched)
{
    // ギャップがあれば出力（1行のギャップを結合）
    if (gapLine != null)
    {
        WriteObject(\$"{lastOutputLine + 1,3}- {gapLine}");
        gapLine = null;
    }
    
    // 前2行を出力（rotate buffer から）
    if (prevPrevLine != null) WriteObject(...);
    if (prevLine != null) WriteObject(...);
    
    // マッチ行を出力
    WriteObject(\$"{lineNumber,3}: {displayLine}");
    
    afterMatchCounter = 2;
    lastOutputLine = lineNumber;
}
else if (afterMatchCounter > 0)
{
    // 後コンテキスト出力
    WriteObject(\$"{lineNumber,3}- {currentLine}");
    afterMatchCounter--;
    lastOutputLine = lineNumber;
}
else if (lastOutputLine > 0)
{
    // ギャップ検出モード
    if (lineNumber == lastOutputLine + 1)
    {
        gapLine = currentLine; // 保持
    }
    else if (lineNumber == lastOutputLine + 2)
    {
        WriteObject(\"\"); // 空行挿入
        gapLine = null;
        lastOutputLine = 0;
    }
}

// rotate buffer 更新
prevPrevLine = prevLine;
prevLine = currentLine;
`

**メリット：**
1. **真の1 pass**: Dictionary/List を使わない
2. **メモリ効率**: 3つの string 変数のみ
3. **リアルタイム出力**: バッファリング不要
4. **ギャップ検出**: 1行ギャップは結合、2行以上は空行

**重要なポイント：**
- lastOutputLine は using ブロックの外で定義（スコープエラー回避）
- ヘッダーは各メソッドで出力（ProcessRecord では出力しない）
- ApplyHighlightingIfMatched で反転表示を適用

## 📝 学んだこと（2025-10-22 23:07）

### HashSet/ToArray()/ToList() の不要な使用を排除

**問題提起：**
- ToArray() / ToList() は全要素をメモリに読み込むため、可能な限り避けるべき
- HashSet も、単純な範囲チェックで済む場合は不要

**UpdateLinesInFileCmdlet での削除：**
1. **HashSet<int> updatedLinesSet の削除**
   - 更新される行は startLine から endLine までの連続した範囲
   - HashSet は不要、範囲チェック (lineNumber >= startLine && lineNumber <= endLine) で十分

2. **ToList() の削除**
   - CalculateAndMergeRanges は IEnumerable<int> を受け取る
   - 呼び出し側で ToList() する必要なし

3. **ToArray() の削除（549行目）**
   - deletedLines.OrderBy().ToArray() → foreach で1 pass処理
   - index カウンタで先頭2+末尾2を判定

4. **インデックスアクセスの排除**
   - updatedLines[0] → startLine
   - updatedLines[Count-1] → endLine
   - 直接計算で対応

**許容される ToArray() / ToList() の使用：**
- TextFileUtility.ConvertToStringArray: Content パラメータ（数行～数十行の小データ）の変換用
- ファイル処理ではないため問題なし

**原則：**
- ファイル処理では ToArray() / ToList() を使わない
- HashSet は重複チェックが必要な場合のみ使用
- 範囲チェックは単純な比較で十分

## 📝 学んだこと（2025-10-23 08:00）

### rotate buffer での出力重複問題

**問題パターン：**
連続するマッチ行を処理する際、後コンテキストとして出力した行が、次のマッチの前コンテキストとして再出力される。

**根本原因：**
- rotate buffer（prevLine, prevPrevLine）の更新が常に実行される
- 既に出力済みの行も rotate buffer に保存される
- 次のマッチ時に、出力済みの行が前コンテキストとして再度出力される

**症状例：**
`
期待：1: line1
      2: line2
      3: line3

実際：1: line1
      1- line1 （1行目の後コンテキスト）
      2: line2
      1- line1 （2行目の前コンテキスト）← 重複！
      2- line2 （2行目の後コンテキスト）
      3: line3
`

**解決策：**
前コンテキストを出力する際、lastOutputLine と比較して既に出力済みの行を除外：
`csharp
// 修正前
if (prevPrevLine != null && lineNumber >= 3)

// 修正後
if (prevPrevLine != null && lineNumber >= 3 && lineNumber - 2 > lastOutputLine)
`

**教訓：**
- rotate buffer を使用する場合、出力済み行を追跡する lastOutputLine が必須
- 前コンテキストの出力条件に「未出力であること」のチェックを追加
- テストで連続マッチのケースを必ず含める（重複検出のため）

**テストパターン：**
`powershell
# 連続マッチで重複を検出
$result = Show-TextFile -Path $file -Pattern "line"
# 期待：ヘッダー + N行 = N+1行
$result.Count | Should -Be ($lineCount + 1)
# 各行が1回だけ出力されることを確認
($result | Where-Object { $_ -match "^\s+1:" }).Count | Should -Be 1
`

**統一された最適化パターン：**
- ShowTextFileCmdlet: rotate buffer（リアルタイム出力）
- UpdateMatchInFileCmdlet: HashSet<int>（行番号のみ） + rotate buffer
- UpdateLinesInFileCmdlet: ContextData（rotate buffer パターン）

## 📝 学んだこと（2025-10-23 09:20）

### UpdateLinesInFileCmdlet の Dictionary 削除と rotate buffer 実装

**課題：**
UpdateLinesInFileCmdlet は Dictionary<int, string> を使用していたが、Show-TextFile と同様に rotate buffer パターンで実装すべき。

**解決策：**

**1. ContextData クラスの導入**
`csharp
private class ContextData
{
    // 前2行コンテキスト
    public string? ContextBefore2 { get; set; }
    public string? ContextBefore1 { get; set; }
    public int ContextBefore2Line { get; set; }
    public int ContextBefore1Line { get; set; }
    
    // 削除時のみ使用
    public string? DeletedFirst { get; set; }
    public string? DeletedSecond { get; set; }
    public string? DeletedSecondLast { get; set; }  // リングバッファ
    public string? DeletedLast { get; set; }        // リングバッファ
    public int DeletedCount { get; set; }
    public int DeletedStartLine { get; set; }
    
    // 後2行コンテキスト
    public string? ContextAfter1 { get; set; }
    public string? ContextAfter2 { get; set; }
    public int ContextAfter1Line { get; set; }
    public int ContextAfter2Line { get; set; }
}
`

**2. リングバッファパターン（削除時の末尾2行）**
`csharp
// 範囲内の各行で更新
context.DeletedSecondLast = context.DeletedLast;  // 1つシフト
context.DeletedLast = line;                        // 新しい値を保存
`

**3. 更新時の表示**
`csharp
// contentLines 配列を直接使用（Dictionary 不要）
if (linesInserted <= 5)
{
    for (int i = 0; i < linesInserted; i++)
    {
        WriteObject(\$"{startLine + i,3}: \\x1b[7m{contentLines[i]}\\x1b[0m");
    }
}
else
{
    // 先頭2行
    WriteObject(\$"{startLine,3}: \\x1b[7m{contentLines[0]}\\x1b[0m");
    WriteObject(\$"{startLine + 1,3}: \\x1b[7m{contentLines[1]}\\x1b[0m");
    // 省略マーカー
    WriteObject(\$"   : \\x1b[7m... ({linesInserted - 4} lines omitted) ...\\x1b[0m");
    // 末尾2行
    WriteObject(\$"{endLine - 1,3}: \\x1b[7m{contentLines[linesInserted - 2]}\\x1b[0m");
    WriteObject(\$"{endLine,3}: \\x1b[7m{contentLines[linesInserted - 1]}\\x1b[0m");
}
`

**4. 削除時の表示ルール変更**
- **旧ルール**: 1-5行すべて表示、6行以上は省略
- **新ルール**: 1-4行すべて表示、5行以上は省略

**理由：**
リングバッファでは末尾2行しか保持できない。5行削除の場合、3行目の情報が失われる。
そのため、5行以上の場合は一律「先頭2行 + 省略マーカー + 末尾2行」のパターンを使用。

**5. メモリ効率の比較**

| 実装 | データ構造 | 典型的な使用量 | 大規模時の使用量 |
|------|-----------|--------------|--------------|
| 旧 | Dictionary<int,string> | 約1.4KB (14行) | 約100KB (1004行) |
| 新 | ContextData (16プロパティ) | 約400-800バイト | 約400-800バイト |

**改善効果：**
- メモリ使用量: 約50-99%削減（ケースに依存）
- Dictionary のオーバーヘッド削除: 約2-5%のパフォーマンス改善

**教訓：**
1. LineRange が事前に分かっている場合、Dictionary は不要
2. リングバッファで末尾N行を保持できる
3. 表示ルールはデータ構造の制約に合わせて調整する
4. contentLines は既にメモリにあるので、再度保存する必要はない

**更新日時:** 2025-10-23 09:20

## 📝 学んだこと（2025-10-23 09:26）

### rotate buffer のサイズ決定：表示要件から逆算

**問題：**
- 削除5行以下をすべて表示したい
- 当初の rotate buffer は2行（DeletedSecondLast, DeletedLast）
- これでは4行までしか対応できない

**解決策：**
rotate buffer を3行に拡張

**実装：**
`csharp
// ContextData クラス
public string? DeletedThirdLast { get; set; }
public string? DeletedSecondLast { get; set; }
public string? DeletedLast { get; set; }

// リングバッファの更新（3行）
context.DeletedThirdLast = context.DeletedSecondLast;  // 1つシフト
context.DeletedSecondLast = context.DeletedLast;       // 1つシフト
context.DeletedLast = line;                            // 新しい値
`

**削除行数と必要な変数の関係：**
| 表示要件 | 必要な変数 | 内訳 |
|---------|----------|-----|
| 1-2行すべて | 2変数 | First, Second |
| 1-3行すべて | 3変数 | First, Second, Last |
| 1-4行すべて | 4変数 | First, Second, SecondLast, Last |
| 1-5行すべて | 5変数 | First, Second, ThirdLast, SecondLast, Last |
| 1-N行すべて | N変数 | First, Second, ... + rotate buffer (N-2行) |

**rotate buffer のサイズ決定式：**
`
rotate_buffer_size = max_display_lines - fixed_head_lines
`

例：
- 5行まで表示 → rotate buffer = 5 - 2 = 3行
- 10行まで表示 → rotate buffer = 10 - 2 = 8行

**トレードオフ：**
- **rotate buffer を大きくする**: より多くの行を完全に表示できる
- **rotate buffer を小さくする**: メモリ効率が良い、コードがシンプル

**設計判断：**
- UpdateLinesInFileCmdlet: 5行まで表示で十分 → rotate buffer 3行
- より多くの行を表示する必要がある場合は、Dictionary に戻すか、固定配列を使う

**教訓：**
1. rotate buffer のサイズは表示要件から逆算して決める
2. 先頭N行は固定変数、末尾M行は rotate buffer で対応
3. 表示行数とメモリ使用量のバランスを考慮

**更新日時:** 2025-10-23 09:26

## 📊 パフォーマンス計測（2025-10-23 09:39）

### Dictionary vs Rotate Buffer のベンチマーク

**計測条件：**
- Update-LinesInFile コマンド
- 100回反復実行
- 1000行のテストファイル

**結果：**

| 操作 | Dictionary版 | Rotate Buffer版 | 差分 | 変化率 |
|------|-------------|----------------|------|--------|
| 5行置換 | 25.17 ms | 26.72 ms | +1.55 ms | +6.2% |
| 50行置換 | 27.91 ms | 26.53 ms | -1.38 ms | -4.9% |
| 200行置換 | 26.54 ms | 26.86 ms | +0.32 ms | +1.2% |
| 10行削除 | 52.84 ms | 52.76 ms | -0.08 ms | -0.2% |
| **平均** | **33.12 ms** | **33.22 ms** | **+0.10 ms** | **+0.3%** |

**分析：**

1. **実行速度**: 
   - 平均差は0.1ms（0.3%）
   - 計測誤差の範囲内で「ほぼ同等」と判断
   - Dictionary のハッシュ計算オーバーヘッドはマイクロベンチマークレベル

2. **メモリ使用量**:
   - Dictionary<int, string>: 約1-100KB（エントリ数に依存）
   - ContextData: 約400-800バイト（固定）
   - **削減率: 50-99%**（ケースに応じて変動）

3. **実装の一貫性**:
   - ShowTextFileCmdlet: rotate buffer
   - UpdateMatchInFileCmdlet: HashSet + rotate buffer
   - UpdateLinesInFileCmdlet: ContextData (rotate buffer)
   - すべてのcmdletが統一パターンを使用

**結論：**

当初予想していた「2-5%のパフォーマンス改善」は実測では確認されなかったが、これは以下の理由による：
- ファイルI/Oが支配的（90%以上）
- Dictionary操作は全体の数%程度
- メモリアロケーション/GCの影響は計測されず

しかし、**メモリ効率の大幅改善**（50-99%削減）と**コードの一貫性向上**により、十分に価値のある最適化である。

**教訓：**
- マイクロ最適化は計測して評価すべき
- 実行速度だけでなく、メモリ効率も重要
- コードの一貫性・保守性も最適化の評価軸

**更新日時:** 2025-10-23 09:39

## 📝 学んだこと（2025-10-23 13:18）

### テストでの例外出力の完全抑制

**問題：**
- Pester テストで意図通り例外がスローされるケースで、大量のエラーメッセージとスタックトレースが表示される
- 内部例外とラッパー例外の両方が表示される（例：ArgumentNullException + MethodInvocationException）
- テスト出力が冗長で、実際のエラーと区別しにくい

**解決策：Test-ThrowsQuietly のパターン**

```powershell
function Test-ThrowsQuietly {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ScriptBlock]$ScriptBlock,
        
        [Parameter(Mandatory = $false)]
        [string]$ExpectedMessage
    )
    
    $caught = $false
    $exceptionMessage = $null
    
    # エラーレコードをクリア
    $Error.Clear()
    
    try {
        # 出力を完全に抑制して実行（標準エラーもリダイレクト）
        $null = & $ScriptBlock -ErrorAction Stop 2>&1
    }
    catch {
        $caught = $true
        $exceptionMessage = $_.Exception.Message
    }
    
    # エラーレコードを再度クリア
    $Error.Clear()
    
    # 例外がスローされたことを検証
    $caught | Should -BeTrue -Because "An exception should have been thrown"
    
    # 期待されるメッセージが指定されている場合、メッセージを検証
    if ($ExpectedMessage) {
        $exceptionMessage | Should -Match $ExpectedMessage
    }
}
```

**重要なポイント：**

1. **2>&1 リダイレクト**
   - 標準エラーストリーム（2）を標準出力（1）にリダイレクト
   - PowerShell がエラーを画面に出力する前にキャッチ

2. ** = ... による破棄**
   - すべての出力を $null に代入して完全に破棄
   - 画面には何も表示されない

3. **Cannot validate argument on parameter 'LineRange'. Start line must be 1 or greater (1-based indexing). Invalid value: 0 Cannot validate argument on parameter 'LineRange'. Start line must be 1 or greater (1-based indexing). Invalid value: 0 The specified module '.\PowerShell.MCP\bin\Debug\netstandard2.0\PowerShell.MCP.dll' was not loaded because no valid module file was found in any module directory. File not found: Tests\Integration\Cmdlets\Show-TextFile.AdditionalEdgeCases.Tests.ps1 Cannot bind argument to parameter 'TestFiles' because it is null. Cannot find path 'C:\home\claude\Convert-ShouldThrowTests.ps1' because it does not exist. Cannot bind argument to parameter 'Path' because it is null. Cannot find path 'C:\MyProj\PowerShell.MCP\Tests\Tests\' because it does not exist. Cannot find path 'C:\MyProj\PowerShell.MCP\Tests\Tests\' because it does not exist. The specified module 'Tests\TestHelpers.psm1' was not loaded because no valid module file was found in any module directory. Cannot find path 'C:\home\claude\TestHelpers.ps1' because it does not exist. Cannot bind argument to parameter 'Content' because it is an empty array..Clear()**
   - PowerShell のエラーレコード配列をクリア
   - try 前後で2回実行することで、エラー履歴を完全に削除

4. **-ErrorAction Stop**
   - 例外を terminating error にして catch でキャッチ可能にする
   - 2>&1 と組み合わせることで、エラーメッセージも抑制

**使用例：**

```powershell
# ❌ 従来の方法（出力が多い）
{ Show-TextFile -Path $file -LineRange @(0, 10) } | Should -Throw

# ✅ 新しい方法（簡潔）
Test-ThrowsQuietly { Show-TextFile -Path $file -LineRange @(0, 10) }

# メッセージも検証する場合
Test-ThrowsQuietly { Add-LinesToFile -Path $file -Content @() } -ExpectedMessage "empty array"
```

**Pester 設定との併用：**

PesterConfiguration.psd1 と併用することで、さらに簡潔な出力を実現：

```powershell
@{
    Output = @{
        Verbosity = 'Minimal'              # 最小限の出力
        StackTraceVerbosity = 'None'       # スタックトレースを非表示
    }
    Debug = @{
        ShowFullErrors = $false            # 完全なエラーを非表示
    }
}
```

**教訓：**
1. **意図的な例外テストは Test-ThrowsQuietly を使用**：Should -Throw は避ける
2. **2>&1 と  = ... のパターン**：PowerShell でのエラー抑制の基本
3. **Cannot validate argument on parameter 'LineRange'. Start line must be 1 or greater (1-based indexing). Invalid value: 0 Cannot validate argument on parameter 'LineRange'. Start line must be 1 or greater (1-based indexing). Invalid value: 0 The specified module '.\PowerShell.MCP\bin\Debug\netstandard2.0\PowerShell.MCP.dll' was not loaded because no valid module file was found in any module directory. File not found: Tests\Integration\Cmdlets\Show-TextFile.AdditionalEdgeCases.Tests.ps1 Cannot bind argument to parameter 'TestFiles' because it is null. Cannot find path 'C:\home\claude\Convert-ShouldThrowTests.ps1' because it does not exist. Cannot bind argument to parameter 'Path' because it is null. Cannot find path 'C:\MyProj\PowerShell.MCP\Tests\Tests\' because it does not exist. Cannot find path 'C:\MyProj\PowerShell.MCP\Tests\Tests\' because it does not exist. The specified module 'Tests\TestHelpers.psm1' was not loaded because no valid module file was found in any module directory. Cannot find path 'C:\home\claude\TestHelpers.ps1' because it does not exist. Cannot bind argument to parameter 'Content' because it is an empty array..Clear() の重要性**：エラー履歴を残さないために必須
4. **Test-ParameterValidationError は別途用意**：パラメータバインディングエラー専用

**更新日時：** 2025-10-23 13:18:22