# ConPTY Interactive Command Execution

## 概要

PowerShell.MCP の `invoke_expression` に ConPTY (Windows Pseudo Console) ベースのインタラクティブ実行モードを追加する。

## 背景・課題

現在の `Invoke-Expression` ベースの実装では、外部 CLI ツール（gcloud, az, npm など）がユーザー入力を求めるプロンプトを出すと：

1. プロンプトが MCP クライアント (Claude) に表示されない
2. ユーザーが入力できない
3. コマンドがハングする

## 目標

| 要件 | 現状 | 目標 |
|------|------|------|
| 出力キャプチャ | ✅ | ✅ |
| インタラクティブ入力 | ❌ | ✅ |
| リアルタイム表示 | ❌ | ✅ |
| ANSI エスケープ対応 | ⚠️ 部分的 | ✅ |

## 技術選定

### ConPTY (Windows Pseudo Console)

- Windows 10 1809+ (Build 17763) で利用可能
- Windows Terminal, VS Code 統合ターミナルが採用
- フル端末エミュレーション
- 入出力の完全な制御が可能

### 代替案の却下理由

| 方法 | 却下理由 |
|------|----------|
| `Start-Process -UseShellExecute` | 出力キャプチャ不可 |
| `Process.Start + Redirect` | インタラクティブ入力不可 |
| `cmd /c` | stdin リダイレクトの制限 |

## API 設計

### invoke_expression パラメータ追加

```json
{
  "pipeline": "gcloud auth login",
  "interactive": true,
  "timeout_seconds": 300
}
```

### 動作モード

```
interactive = false (デフォルト):
  現在の Invoke-Expression ベースの実行
  → 高速、非インタラクティブコマンド向け

interactive = true:
  ConPTY ベースの実行
  → インタラクティブ CLI ツール向け
  → ユーザーのコンソールに出力表示
  → ユーザーからの入力を受付
  → 完了後、全出力を MCP に返却
```

## 実装計画

### Phase 1: ConPTY ラッパークラス

```csharp
// PseudoConsole.cs
public class PseudoConsole : IDisposable
{
    private IntPtr _handle;
    private SafeFileHandle _inputPipe;
    private SafeFileHandle _outputPipe;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(
        COORD size,
        SafeFileHandle hInput,
        SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    public PseudoConsole(short width, short height) { ... }
    public void Dispose() { ... }
}
```

### Phase 2: プロセス起動

```csharp
// ConPtyProcess.cs
public class ConPtyProcess : IDisposable
{
    private Process _process;
    private PseudoConsole _console;
    private StreamWriter _input;
    private StringBuilder _outputBuffer;

    public async Task<string> RunAsync(string command, CancellationToken ct)
    {
        // 1. ConPTY 作成
        // 2. STARTUPINFOEX でプロセス起動
        // 3. 出力読み取りタスク開始
        // 4. ユーザー入力転送タスク開始
        // 5. 完了待機
        // 6. バッファ内容を返却
    }
}
```

### Phase 3: MCP 統合

```csharp
// InvokeExpressionHandler.cs
public async Task<string> ExecuteAsync(InvokeExpressionParams p)
{
    if (p.Interactive)
    {
        using var conPty = new ConPtyProcess();
        return await conPty.RunAsync(p.Pipeline, cancellationToken);
    }
    else
    {
        // 既存の Invoke-Expression ベース実行
        return await ExecuteWithInvokeExpression(p.Pipeline);
    }
}
```

### Phase 4: コンソール UI

```
┌─ PowerShell.MCP Interactive ─────────────────┐
│ > gcloud auth login                          │
│                                              │
│ Go to the following link in your browser:    │
│ https://accounts.google.com/o/oauth2/...     │
│                                              │
│ Enter authorization code: █                  │
│                                              │
│ [User input here...]                         │
└──────────────────────────────────────────────┘
```

## ファイル構成

```
PowerShell.MCP/
├── src/
│   └── PowerShell.MCP/
│       ├── ConPty/
│       │   ├── NativeMethods.cs      # P/Invoke 定義
│       │   ├── PseudoConsole.cs      # ConPTY ラッパー
│       │   ├── ConPtyProcess.cs      # プロセス管理
│       │   └── InteractiveRunner.cs  # MCP 統合
│       └── Handlers/
│           └── InvokeExpressionHandler.cs  # 既存（修正）
```

## 依存関係

- Windows 10 1809+ (Build 17763)
- .NET 8.0+
- No external packages required (P/Invoke only)

## テスト計画

### 単体テスト

```csharp
[Fact]
public async Task ConPty_CapturesOutput()
{
    using var process = new ConPtyProcess();
    var output = await process.RunAsync("echo hello");
    Assert.Contains("hello", output);
}
```

### 統合テスト

```powershell
# 1. 単純な出力
invoke_expression -Interactive -Pipeline "dir"

# 2. インタラクティブ入力
invoke_expression -Interactive -Pipeline "Read-Host 'Name'"

# 3. 外部 CLI
invoke_expression -Interactive -Pipeline "gcloud auth login"
```

## リスク・課題

| リスク | 対策 |
|--------|------|
| Windows 限定 | Linux/macOS は未対応とドキュメント明記 |
| API 複雑性 | 十分なエラーハンドリングとログ |
| パフォーマンス | 非インタラクティブ時は従来方式を使用 |
| タイムアウト処理 | CancellationToken で適切にキャンセル |

## 参考資料

- [Windows Pseudo Console (ConPTY)](https://devblogs.microsoft.com/commandline/windows-command-line-introducing-the-windows-pseudo-console-conpty/)
- [Creating a Pseudoconsole Session](https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session)
- [Windows Terminal Source](https://github.com/microsoft/terminal)
- [ConPTY Sample](https://github.com/microsoft/terminal/tree/main/samples/ConPTY)

## マイルストーン

- [ ] Phase 1: ConPTY ラッパー実装 (2-3 days)
- [ ] Phase 2: プロセス管理実装 (2-3 days)
- [ ] Phase 3: MCP 統合 (1-2 days)
- [ ] Phase 4: テスト・デバッグ (2-3 days)
- [ ] ドキュメント更新 (1 day)

**推定工数: 8-12 days**
