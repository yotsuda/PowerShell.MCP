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
- Dictionary<int, string> または rotate buffer で管理

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
    else if (afterMatchCounter > 0)
    {
        // 後続コンテキストの収集
        contextBuffer[lineNumber] = currentLine;
        afterMatchCounter--;
    }
}
```

### 5. GetEnumerator() + hasNext パターン（推奨）

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
- ContextData クラス（rotate buffer パターン）で 1 pass 化
- 削除時の末尾N行をリングバッファで保持

### Update-MatchInFile
- HashSet<int> で行番号のみ記録（1st pass）
- rotate buffer + 後続コンテキストカウンタ（2nd pass）
- 真の2 pass実装（メモリ効率重視）

### Show-TextFile
- rotate buffer + gapLine でリアルタイム出力
- 真の1 pass実装（Dictionary/List 不使用）

## 💡 重要な注意点

### rotate buffer の保存内容
- **置換前の元の行** を保存（置換後ではない）
- マッチ行のコンテキストバッファには **反転表示付きの置換後の行** を保存

### 出力重複の防止
- lastOutputLine で最後に出力した行番号を追跡
- 前コンテキスト出力時に `lineNumber - N > lastOutputLine` をチェック
- 既に出力済みの行は再出力しない

### パフォーマンス
- rotate buffer を常に動作させる（条件分岐なし）
- 参照の代入（ポインタコピー）は文字列のコピーではないため、パフォーマンス影響なし
- Dictionary vs rotate buffer: 実行速度はほぼ同等、メモリ使用量は50-99%削減

---

## 📝 重要な学び

### 1. Cmdlet 設計：エラー vs 警告の選択

**原則：**
ユーザーの意図が明確で、安全に続行できる場合は**警告**を使い、完全に無効な操作の場合のみ**エラー**を使う。

**エラーを出すべきケース：**
- 完全に無効な操作（例：LineNumber が 0 や負の数）
- データ損失のリスク
- 意図が不明確

**警告で済むケース：**
- ユーザーの意図は明確だが、予期しない結果になる可能性がある
- 例：存在しないファイルに LineNumber 5 を指定 → 警告を出して新規ファイル作成

**PowerShell の慣習：**
- Add-Content, Set-Content は存在しないファイルを作成する
- Update-*, Remove-* cmdlet は存在チェックでエラーを出す

### 2. rotate buffer の出力重複問題

**問題：**
連続するマッチ行を処理する際、後コンテキストとして出力した行が、次のマッチの前コンテキストとして再出力される。

**解決策：**
前コンテキストを出力する際、lastOutputLine と比較して既に出力済みの行を除外：

```csharp
// 修正前
if (prevPrevLine != null && lineNumber >= 3)

// 修正後
if (prevPrevLine != null && lineNumber >= 3 && lineNumber - 2 > lastOutputLine)
```

### 3. UpdateLinesInFileCmdlet の ContextData パターン

**Dictionary<int, string> を使わない実装：**

```csharp
private class ContextData
{
    // 前2行コンテキスト
    public string? ContextBefore2 { get; set; }
    public string? ContextBefore1 { get; set; }
    
    // 削除時の先頭2行
    public string? DeletedFirst { get; set; }
    public string? DeletedSecond { get; set; }
    
    // 削除時の末尾N行（リングバッファ）
    public string? DeletedThirdLast { get; set; }
    public string? DeletedSecondLast { get; set; }
    public string? DeletedLast { get; set; }
    
    // 後2行コンテキスト
    public string? ContextAfter1 { get; set; }
    public string? ContextAfter2 { get; set; }
}
```

**リングバッファパターン（削除時の末尾N行）：**
```csharp
// 範囲内の各行で更新
context.DeletedThirdLast = context.DeletedSecondLast;
context.DeletedSecondLast = context.DeletedLast;
context.DeletedLast = line;
```

**メモリ効率：**
- Dictionary<int, string>: 約1-100KB（エントリ数に依存）
- ContextData: 約400-800バイト（固定）
- **削減率: 50-99%**

### 4. テストでの例外出力の完全抑制

**問題：**
Pester テストで意図通り例外がスローされるケースで、大量のエラーメッセージとスタックトレースが表示される。

**解決策：Test-ThrowsQuietly パターン**

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
        # 出力を完全に抑制（標準エラーもリダイレクト）
        $null = & $ScriptBlock -ErrorAction Stop 2>&1
    }
    catch {
        $caught = $true
        $exceptionMessage = $_.Exception.Message
    }
    
    # エラーレコードを再度クリア
    $Error.Clear()
    
    # 例外がスローされたことを検証
    $caught | Should -BeTrue
    
    # 期待されるメッセージの検証（オプション）
    if ($ExpectedMessage) {
        $exceptionMessage | Should -Match $ExpectedMessage
    }
}
```

**重要なポイント：**
- `2>&1`：標準エラーを標準出力にリダイレクト
- `$null = ...`：すべての出力を破棄
- `$Error.Clear()`：エラー履歴を完全削除（try 前後で2回）

---

### 5. Update-LinesInFile のコンテキスト表示設計

**原則：**
常に「更新後の状態」を表示する。削除時も例外ではない。

**実装：**
- 削除時（-Content @()）: : のみを表示（何もない状態を表現）
- 後続コンテキストの行番号: 常に outputLine（更新後の行番号）を使用
- OutputUpdateContext を常に使用（OutputDeleteContext は不使用）

**理由：**
- ユーザーは「更新後のファイルがどうなったか」を知りたい
- 削除前の内容を見せることは、混乱を招く可能性がある
- 行番号も更新後の状態と一致させることで、ファイル全体の状態を正確に把握できる

**作成日時:** 2025-10-22 11:15
**最終更新:** 2025-10-23 13:30
**バージョン:** 2.0
