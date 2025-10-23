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
Pester テストで意図通り例外がスローされるケースで、大量のエラーメッセージとスタックトレースが表示され、トークンを大量に消費する。

**解決策：Test-ThrowsQuietly パターン（実装済み ✅）**

**場所**: `Tests/Shared/TestHelpers.psm1`

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
    
    # ErrorActionPreference を Stop に設定
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    
    # すべてのコマンドに -ErrorAction Stop を適用
    $previousDefaultParameters = $PSDefaultParameterValues.Clone()
    $PSDefaultParameterValues['*:ErrorAction'] = 'Stop'
    
    try {
        # 出力を完全に抑制（すべてのストリームをリダイレクト）
        $null = & $ScriptBlock *>&1
    }
    catch {
        $caught = $true
        $exceptionMessage = $_.Exception.Message
    }
    finally {
        # 設定を元に戻す
        $ErrorActionPreference = $previousErrorActionPreference
        $PSDefaultParameterValues.Clear()
        foreach ($key in $previousDefaultParameters.Keys) {
            $PSDefaultParameterValues[$key] = $previousDefaultParameters[$key]
        }
    }
    
    # catch されなかったが $Error にエラーが追加された場合もチェック
    if (-not $caught -and $Error.Count -gt 0) {
        $caught = $true
        $exceptionMessage = $Error[0].Exception.Message
    }
    
    # エラーレコードを再度クリア
    $Error.Clear()
    
    # 例外がスローされたことを検証
    $caught | Should -BeTrue -Because "Expected an exception to be thrown"
    
    # 期待されるメッセージの検証（オプション）
    if ($ExpectedMessage) {
        $exceptionMessage | Should -Match $ExpectedMessage
    }
}
```

**重要なポイント：**
- `*>&1`：すべての出力ストリーム（標準出力、エラー、警告、デバッグなど）をリダイレクト
- `$null = ...`：すべての出力を破棄
- `$Error.Clear()`：エラー履歴を完全削除（try 前後で2回）
- `ErrorActionPreference = 'Stop'`：非終了エラーを例外に変換
- `$PSDefaultParameterValues['*:ErrorAction'] = 'Stop'`：すべてのコマンドに自動適用
- `$Error` の追加チェック：catch できなかったエラーも検出

**使用例：**
```powershell
# 基本的な使用
It "Should throw on missing file" {
    Test-ThrowsQuietly { Show-TextFile -Path "missing.txt" }
}

# メッセージ検証付き
It "Should throw file not found error" {
    Test-ThrowsQuietly { 
        Show-TextFile -Path "C:\NonExistent\file.txt" 
    } -ExpectedMessage "File not found"
}
```

**効果：**
- トークン消費を90%以上削減
- テスト出力が読みやすくなる
- エラーの有無とメッセージのみを簡潔に検証

**適用範囲：**
- ✅ 終了エラー（ThrowTerminatingError）
- ✅ パラメータ検証エラー（ValidateRange など）
- ⚠️ 非終了エラー（WriteError）- PowerShell と C# cmdlet の制限により部分的にサポート

**実装状況：**
- ✅ Tests\Shared\TestHelpers.psm1 に実装済み
- ✅ Export-ModuleMember で公開済み
- ✅ Tests\README.md に使用方法を文書化
- ✅ 実用例テストを作成（QuietErrorHandling.Tests.ps1）
- ✅ 比較テストを作成（ErrorOutputComparison.Tests.ps1）

**検証結果（2025-10-23）：**
- 従来の方法（Should -Throw）: 各エラーで数百〜数千文字のスタックトレース出力
- Test-ThrowsQuietly: エラー出力を完全に抑制（0文字）
- **削減率: 90%以上** → トークン消費を大幅に削減
- テスト結果が読みやすくなり、重要なエラーのみが表示される
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
**最終更新:** 2025-10-23 22:22
**バージョン:** 2.2

### 6. テスト実行時の出力制御

**問題:**
`dotnet test --verbosity normal` や `Invoke-Pester` のデフォルト出力は非常に冗長で、LLM のトークンを大量に消費する。特にビルドログは数万文字に達することがある。

**解決策:**

**C# ユニットテスト:**
```powershell
# ❌ 避けるべき - 非常に冗長な出力
dotnet test --verbosity normal

# ✅ 推奨 - 簡潔な出力
dotnet test --verbosity quiet --nologo
```

**PowerShell 統合テスト:**
```powershell
# ❌ 避けるべき - 詳細な出力
Invoke-Pester -Path .\Tests\Integration

# ✅ 推奨 - 最小限の出力
$config = New-PesterConfiguration
$config.Run.Path = ".\Tests\Integration"
$config.Output.Verbosity = "Minimal"
Invoke-Pester -Configuration $config
```

**実装:**
- ✅ `Tests\Run-AllTests.ps1` を更新（デフォルトで簡潔な出力）
- ✅ `Tests\README.md` に簡潔な実行方法を文書化
- `-Detailed` スイッチで詳細出力も可能

**効果:**
- トークン消費を90%以上削減
- テスト結果が読みやすくなる
- 失敗したテストのみが目立つ

### 7. ErrorVariable のユニーク化とバグの教訓

**問題:**
PowerShell の ErrorVariable は同一のエラーを複数回記録することがある。MCPPollingEngine.ps1 では、エラーのユニーク化処理を実装していたが、**return 文で空配列を返していた**ため、機能していなかった。

**解決策:**
```powershell
# Deduplicate errors
$uniqueErrors = @()
$seenErrors = @{}
foreach ($err in $errorVar) {
    # Create a unique key based on message, error ID, and category
    $key = if ($err -is [System.Management.Automation.ErrorRecord]) {
        "$($err.Exception.Message)|$($err.FullyQualifiedErrorId)|$($err.CategoryInfo.Category)"
    } else {
        $err.ToString()
    }
    
    if (-not $seenErrors.ContainsKey($key)) {
        $uniqueErrors += $err
        $seenErrors[$key] = $true
    }
}

return @{
    Success = $outVar
    Error = $uniqueErrors  # ← 重要: ユニーク化した配列を返す
    # ...
}
```

**重要なポイント:**
- **メッセージのみでユニーク化しない**: Message + FullyQualifiedErrorId + Category の3要素を使用
- **return 文を忘れない**: 処理したデータを必ず返す（空配列を返さない）
- **コードレビューの重要性**: 処理は正しくても、return で使われていないケースを見逃さない

**教訓:**
実装した処理が実際に使用されているか、最終的な出力まで確認する。特に return 文では、計算結果が正しく返されているか注意深く確認する。

### 8. -LineRange で -1 を使用する場合の行数計算

**問題:**
`-LineRange 5,-1` のように2番目の値に `-1` を指定すると、`int.MaxValue` （2147483647）が使用され、不正な行数が表示される。

**原因:**
`TextFileUtility.ParseLineRange()` が `-1` を `int.MaxValue` に変換するが、`linesRemoved = endLine - startLine + 1` の計算で `int.MaxValue` を使っていた。

**解決策:**
実際に処理した行数をカウントする方式に変更：

```csharp
// ❌ 避けるべき - endLine が int.MaxValue の場合に巨大な値になる
int linesRemoved = endLine - startLine + 1;

// ✅ 推奨 - 実際に処理した行数をカウント
int linesRemoved = 0;
// ...
if (currentLine >= startLine && currentLine <= endLine)
{
    linesRemoved++;  // 実際に削除/置換された行をカウント
    // ...
}
```

**重要なポイント:**
- `-1` や `0` は「ファイル末尾まで」を意味するため、事前計算できない
- 実際にループで処理した行数をカウントすることで正確な値を取得
- `int.MaxValue` を使った算術演算は避ける

**教訓:**
特殊な値（`int.MaxValue`, `-1` など）を使う場合は、算術演算ではなくカウンタやフラグで処理する。事前計算が困難な場合は、実際の処理中にカウントする方式を採用する。