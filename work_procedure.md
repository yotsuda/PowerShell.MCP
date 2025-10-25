# MCPPollingEngine.ps1 C#移植プロジェクト - 作業手順書

## 📋 概要

**目的:** MCPPollingEngine.ps1 の全機能を C# で実装し直し、ファイルIOなしで全ストリームをコンソール出力順で正確にキャプチャする

**対象ディレクトリ:** C:\MyProj\PowerShell.MCP

**対象範囲:** 全ファイル

---

## 🎯 プロジェクト目標

### 主要目標
1. **ファイルIO完全削除** - Start-Transcript のようなファイル操作を一切行わない
2. **統合ストリームキャプチャ** - すべてのストリーム（Success/Error/Warning/Info/Verbose/Debug）をコンソール出力順で統合してキャプチャ
3. **ユーザー視点の一致** - ユーザーがコンソールで見る内容と MCP response が完全に一致
4. **最高のパフォーマンス** - C# ネイティブ実装による最適化

### 品質基準
- ✅ 全ストリームの完全キャプチャ（漏れなし）
- ✅ コンソール出力順の正確な再現
- ✅ エラーの適切な表示（赤色表示含む）
- ✅ パフォーマンス: Start-Transcript 版より高速
- ✅ コード品質: 保守性・可読性の高さ
- ✅ 既存機能の完全互換性

---

## ビルドポリシー

- AI（あなた）は、PowerShell.MCP をデプロイできない
- ビルドが通ったら、ユーザー（よしふみ）にデプロイを依頼する
- よしふみが test ready といったら、すぐにテストする（ビルド・デプロイ不要）

## 📦 Git ポリシー

**使用:** ✅ はい

**コミット方針:**
- 各フェーズ完了時にコミット
- ユーザー承認後のみコミット実行
- コミットメッセージは英語一文

---

## 🔄 作業フロー

### このファイル（work_procedure.md）の更新タイミング
1. 新しい実装方針が判明したとき
2. 技術的な発見があったとき
3. 設計の大幅な変更が必要になったとき

### work_progress.txt の更新タイミング
**即時更新:** 以下の変化が発生したら即座に更新
- ファイルの status 変更（⏳→🟡→✅など）
- effort_remaining の更新
- 新規ファイルの発見・追加
- ファイルの削除・統合

---

## 📚 実装調査フェーズ

### 目的
PowerShell と .NET の内部実装を理解し、最適な実装方針を決定する

### 調査項目

#### 1. PowerShell Invoke-Expression 実装調査
**調査対象:**
- PowerShell GitHub リポジトリ
- `Microsoft.PowerShell.Commands.Utility` アセンブリ
- Invoke-Expression cmdlet のソースコード

**調査内容:**
- コマンド実行の内部メカニズム
- ストリーム処理の実装
- エラーハンドリングの方法
- パフォーマンス最適化のポイント
- Runspace の構築方法（このモジュールでは、PS console と同一の runspace を使えるようにしたい）

**成果物:**
- 実装方針ドキュメント
- 参考コードスニペット

#### 2. ストリーム統合キャプチャ実装方法調査
**技術的課題:**
PowerShell の各ストリームは独立している：
- Success (Output)
- Error
- Warning
- Information
- Verbose
- Debug

これらを「コンソール出力順」で統合する方法を見つける必要がある。

**調査方針:**
1. **PSHost UI レイヤー調査**
   - `PSHost` インターフェース
   - `PSHostUserInterface` クラス
   - カスタム PSHost 実装の可能性

2. **Runspace ストリーム処理調査**
   - `PSDataCollection<T>` の動作
   - DataAdded イベントのタイミング
   - タイムスタンプベースの統合可能性

3. **代替アプローチ調査**
   - カスタム PSHostUserInterface 実装
   - ストリームマルチプレクサーの実装
   - メモリベースのトランスクリプト実装

**成果物:**
- 実装可能性評価レポート
- プロトタイプコード

#### 3. PowerShell コンソールホスト実装調査
**調査対象:**
- ConsoleHost.cs の実装
- PSReadLine の統合方法
- コンソール出力のバッファリング

**成果物:**
- アーキテクチャ理解ドキュメント

### 📊 調査結果（2025-10-25完了）

#### 重要な発見1: MergeMyResults + リアルタイムストリーミング

PowerShell SDKには、すべてのストリームを統合し、**かつリアルタイムでコンソール出力する**公式機能が存在する:

**最終実装方法（Pipeline API + DataReady）:**
```csharp
// 既存のRunspaceを使用
Pipeline pipeline = runspace.CreatePipeline();

// キャプチャ用リスト
List<PSObject> capturedOutput = new List<PSObject>();

// リアルタイム出力＋キャプチャ
pipeline.Output.DataReady += (sender, eventArgs) => {
    PSObject obj = ((PipelineReader<PSObject>)sender).Read();
    
    // 1. リアルタイムでコンソール表示（色付き）
    switch (obj.ImmediateBaseObject)
    {
        case ErrorRecord er:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(er);
            Console.ResetColor();
            break;
        case WarningRecord wr:
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARNING: {wr.Message}");
            Console.ResetColor();
            break;
        case VerboseRecord vr:
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"VERBOSE: {vr.Message}");
            Console.ResetColor();
            break;
        case InformationRecord ir:
            Console.WriteLine(ir.MessageData);
            break;
        case DebugRecord dr:
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($"DEBUG: {dr.Message}");
            Console.ResetColor();
            break;
        default:
            Console.WriteLine(obj);
            break;
    }
    
    // 2. 同時にキャプチャ（MCP response用）
    capturedOutput.Add(obj);
};

// コマンド追加
pipeline.Commands.AddScript(command);

// すべてのストリームをOutputにマージ（重要！）
pipeline.Commands[pipeline.Commands.Count-1]
    .MergeMyResults(PipelineResultTypes.All, PipelineResultTypes.Output);

// 実行 - DataReadyイベントがリアルタイムで発火
pipeline.Invoke();

// 実行完了後、capturedOutputに全結果が格納されている
return capturedOutput;
```

**代替実装（PowerShell class + PSDataCollection）:**
```csharp
var outputCollection = new PSDataCollection<PSObject>();
outputCollection.DataAdded += (sender, e) => {
    var item = ((PSDataCollection<PSObject>)sender)[e.Index];
    
    // リアルタイム表示
    Console.WriteLine(item);
    
    // キャプチャは自動（outputCollectionに格納される）
};

using var powerShell = System.Management.Automation.PowerShell.Create();
powerShell.Runspace = runspace;
powerShell.AddScript(command);
powerShell.Commands.Commands[0].MergeMyResults(
    PipelineResultTypes.All, 
    PipelineResultTypes.Output
);

// outputCollectionに出力しながら実行
powerShell.Invoke(null, outputCollection);

return outputCollection.ToList();
```

**採用方針:**
- **Pipeline API（第1案）を採用** - より明確で制御しやすい
- DataReadyイベントで即座にコンソール表示
- 同時にキャプチャしてMCP responseに含める
- 色付きコンソール出力でユーザー体験向上

**利点:**
- ✅ リアルタイムでコンソール出力（ストリーミング）
- ✅ すべてのストリームをコンソール出力順で統合
- ✅ 同時に完全なキャプチャ
- ✅ 型情報で各ストリームを識別・色分け可能
- ✅ PowerShell SDK標準機能（公式API）
- ✅ PSReadLineとの統合問題なし
- ✅ カスタムPSHost実装不要

**参考資料:**
- Stack Overflow: "Capturing all streams in correct sequence with PowerShell SDK"
- Stack Overflow: "Capturing Powershell output in C# after Pipeline.Invoke throws" (MergeMyResults + DataReady)
- PowerShell GitHub Issue #7477: ストリーム順序の課題

#### Invoke-Expression実装調査結果

**ファイル:** `C:\MyProj\PowerShell\src\Microsoft.PowerShell.Commands.Utility\commands\utility\InvokeExpressionCommand.cs`

**重要なコード:**
```csharp
ScriptBlock myScriptBlock = InvokeCommand.NewScriptBlock(Command);
myScriptBlock.InvokeUsingCmdlet(
    contextCmdlet: this,
    useLocalScope: false,  // グローバルスコープで実行
    errorHandlingBehavior: ScriptBlock.ErrorHandlingBehavior.WriteToCurrentErrorPipe,
    dollarUnder: AutomationNull.Value,
    input: emptyArray,
    scriptThis: AutomationNull.Value,
    args: emptyArray
);
```

**学習ポイント:**
- `InvokeCommand.NewScriptBlock()` でScriptBlockを作成
- `InvokeUsingCmdlet()` でcmdletコンテキストで実行
- `useLocalScope: false` により、呼び出し元と同じスコープで実行
- エラーは自動的に現在のエラーパイプに書き込まれる


---

## 🏗️ 設計フェーズ

### 目的
調査結果を基に、実装アーキテクチャを確定する

### 設計項目

#### 1. CommandExecutor.cs アーキテクチャ設計
**設計決定項目:**
- クラス構造
- public API 設計
- 内部実装方式
- エラーハンドリング戦略
- パフォーマンス最適化ポイント

#### 2. ストリーム統合戦略
**実装パターン選択:**
以下のいずれかを選択：

**パターンA: カスタム PSHost**
```
利点: 完全なコンソール出力制御
欠点: 実装が複雑、PSReadLine との統合が困難
```

**パターンB: タイムスタンプベース統合**
```
利点: 実装が比較的シンプル
欠点: 精度の問題、同時発生イベントの順序が不定
```

**パターンC: メモリベーストランスクリプト**
```
利点: Start-Transcript の動作を模倣、信頼性高い
欠点: 内部 API 使用の可能性、将来の互換性リスク
```
```

**パターンD: MergeMyResults パターン（推奨）** ✅
```
利点: PowerShell SDK標準機能、実装がシンプル、順序保証あり
方法: command.MergeMyResults(PipelineResultTypes.All, PipelineResultTypes.Output)
型情報: ErrorRecord, WarningRecord等で各ストリームを識別可能
信頼性: 公式API、将来の互換性リスクなし
```

**決定: パターンDを採用**
理由:
- PowerShell SDK標準機能であり、最も信頼性が高い
- 実装が最もシンプル
- すべてのストリームがコンソール出力順で統合される
- 型情報により各ストリームを正確に識別可能
- カスタムPSHostの複雑な実装が不要
- PSReadLineとの統合問題なし
#### 3. MCPPollingEngine.ps1 簡略化設計
**残す処理:**
- タイマーベースのポーリング（100ms）
- insertCommand ハンドラ（PSReadLine 操作）
- c# に新規作成した c# method 呼び出し

**削除する処理:**
- executeCommand ハンドラ → C# static method 側で実装
- executeCommandSilent ハンドラ → C# static 側で実装

- Invoke-CommandWithStreaming 関数
- すべてのストリームキャプチャ処理
- プロンプト表示処理
- 結果フォーマット処理

---

### 📊 設計結果（2025-10-25完了）

#### CommandExecutor.cs 詳細設計

**完了した設計:**
- ExecutionResult クラス再設計
- Execute / ExecuteSilent メソッド設計
- DisplayToConsole メソッド設計
- MCPPollingEngine.ps1 統合インターフェース

**主要設計決定:**

**主要設計決定:**

**1. ExecutionResult構造（シンプル設計）**
```csharp
public class ExecutionResult
{
    // コンソール出力順のテキスト（シンプル）
    public List<string> Output { get; set; } = new List<string>();
    
    // メタ情報
    public double DurationSeconds { get; set; }
    public bool HadErrors { get; set; }
}
```

**設計の利点:**
- **シンプル** - 型情報やタイムスタンプなし（AIにとってノイズを排除）
- コンソール出力順が完全に保持される（string配列として）
- MCPクライアントは純粋なテキスト出力を取得
- 内部処理（色分け表示）では型判別を行うが、結果には含めない

**設計方針の変更:**
- ❌ OutputItemクラス削除 - 型情報とタイムスタンプは不要
- ✅ シンプルなstring配列 - AIが読みやすい

**2. Execute メソッド**
```csharp
public static ExecutionResult Execute(
    string command, 
    Runspace runspace, 
    bool displayToConsole = true)
```
**実装パターン:**
```csharp
Pipeline pipeline = runspace.CreatePipeline();

pipeline.Output.DataReady += (sender, eventArgs) => {
    var reader = (PipelineReader<PSObject>)sender;
    while (reader.Count > 0)
    {
        PSObject obj = reader.Read();
        
        // 1. 即座にコンソール表示（色付き）- 型判別
        if (displayToConsole)
            DisplayToConsole(obj);
        
        // 2. プレーンテキストをキャプチャ
        result.Output.Add(obj.ToString());
    }
};

pipeline.Commands.AddScript(command);
pipeline.Commands[pipeline.Commands.Count-1]
    .MergeMyResults(PipelineResultTypes.All, PipelineResultTypes.Output);

pipeline.Invoke();
```


**3. DisplayToConsole 設計**
- Error: 赤色
- Warning: 黄色
- Verbose: シアン
- Debug: グレー
- Information/Output: デフォルト

**4. MCPPollingEngine.ps1 統合（シンプル）**
```powershell
$result = [CommandExecutor]::Execute($command, [runspace]::DefaultRunspace)

# MCP response - 非常にシンプル
$response = @{
    output = $result.Output           # string配列（プレーンテキスト）
    duration = $result.DurationSeconds
    hadErrors = $result.HadErrors
}
```

**設計の利点:**
- プレーンテキストのみ - AIが読みやすい
- 型情報・タイムスタンプなし - ノイズを排除
- コンソール出力順が完全に保持される
- DisplayToConsole内部でのみ型判別（色付き表示用）

**設計ドキュメント:**
- 詳細: `/home/claude/CommandExecutor_Design.md`

**実装優先順位:**
1. Phase 1: 基本Execute実装（Pipeline + MergeMyResults + DataReady）
2. Phase 2: ExecutionResult完成（OutputItem構造）
3. Phase 3: DisplayToConsole完成（色付き出力）
4. Phase 4: ExecuteSilent実装
5. Phase 5: エラーハンドリング


---

## 🔨 実装フェーズ

### 実装順序

#### Phase 1: コアストリームキャプチャ実装
1. 基本的な CommandExecutor クラス実装
2. 単純なストリームキャプチャ（分離状態でも可）
3. 動作確認

#### Phase 2: ストリーム統合実装
1. 選択したパターンの実装
2. コンソール出力順の統合
3. 詳細な動作テスト

#### Phase 3: MCPPollingEngine.ps1 簡略化
1. C# static method の呼び出しコード実装
2. 既存処理の削除
3. 動作確認

#### Phase 4: 統合とテスト
1. 全機能の統合テスト
2. パフォーマンス測定
3. バグ修正

---

## ✅ 検証フェーズ

### 検証項目

#### 1. 機能検証
**テストケース:**

**TC1: 基本的なコマンド実行**
```powershell
Get-Process | Select-Object -First 5
```
期待: Success ストリームの正確なキャプチャ

**TC2: エラーキャプチャ**
```powershell
Get-Item C:\NonExistent.txt
```
期待: 
- コンソールに赤色でエラー表示
- MCP response にエラー情報

**TC3: 複数ストリーム混在**
```powershell
Write-Host "Output"
Write-Warning "Warning"
Write-Verbose "Verbose" -Verbose
Write-Debug "Debug" -Debug
Write-Error "Error"
```
期待: すべてのストリームがコンソール出力順で記録される

**TC4: 長時間実行**
```powershell
1..10 | ForEach-Object { 
    Write-Host "Item $_"
    Start-Sleep -Milliseconds 100
}
```
期待: リアルタイムでコンソール表示、最後に統合結果

**TC5: Pester テスト実行**
```powershell
Invoke-Pester .\Tests -Output Detailed
```
期待: テスト結果の完全なキャプチャ

#### 2. パフォーマンス検証
**測定項目:**
- コマンド実行オーバーヘッド
- メモリ使用量
- Start-Transcript 版との比較

**目標:**
- Start-Transcript 版より 20% 以上高速
- メモリ使用量: 合理的な範囲内

#### 3. 互換性検証
**確認項目:**
- 既存の MCP クライアントとの互換性
- 既存のツール呼び出しとの互換性
- PowerShell バージョン互換性（5.1, 7.x）

---

## 🚨 リスクと対策

### 技術的リスク

#### リスク1: ストリーム統合が技術的に不可能
**可能性:** 中
**影響:** 高
**対策:** 
- 早期にプロトタイプ実装で検証
- 不可能な場合は「次善の策」を検討
  - タイムスタンプベースの近似的統合
  - ストリーム分離を許容（セクション分け）

#### リスク2: パフォーマンス目標未達
**可能性:** 低
**影響:** 中
**対策:**
- プロファイリングツールで bottleneck 特定
- 段階的な最適化
- 目標を再設定（正確性を優先）

#### リスク3: PSReadLine との干渉
**可能性:** 中
**影響:** 高
**対策:**
- カスタム PSHost 実装時は慎重に設計
- 既存の PSReadLine 動作を壊さない
- 代替案の準備

#### リスク4: 内部 API への依存
**可能性:** 低
**影響:** 中
**対策:**
- 可能な限り公開 API のみ使用
- 内部 API 使用時は将来の互換性を考慮
- 代替実装の準備

---

## 📝 参考資料

### PowerShell リポジトリ
- GitHub: https://github.com/PowerShell/PowerShell
    -> 下記に fork 済み。ただしほかの PR 作業中であるため更新してはいけない。
       C:\MyProj\PowerShell
- Invoke-Expression: "C:\MyProj\PowerShell\src\Microsoft.PowerShell.Commands.Utility\commands\utility\InvokeExpressionCommand.cs"
- ConsoleHost: "C:\MyProj\PowerShell\src\Microsoft.PowerShell.ConsoleHost\host\msh\ConsoleHost.cs"

### ドキュメント
- PowerShell SDK Documentation
- System.Management.Automation namespace
- Runspace API

---

## 📊 成功の定義

プロジェクトは以下の条件を満たした時に成功とする：

1. ✅ ファイルIOなしで実装完了
2. ✅ 全ストリームをコンソール出力順でキャプチャ
3. ✅ 既存機能との完全互換性
4. ✅ Start-Transcript 版より高速
5. ✅ すべてのテストケースが合格
6. ✅ コードレビュー完了

---

最終更新: 2025-10-25
作成者: Claude (Anthropic) with よしふみ