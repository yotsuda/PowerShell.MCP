# PowerShell.MCP ストリームキャプチャ実装分析

## 要求仕様
- verbose と debug stream を黄色でPS consoleに出力
- それ以外のストリームは -InformationVariable や -ErrorVariable などでキャプチャ
- タイムライン順序の保持（可能であれば）
- コマンドは1回のみ実行（副作用回避）

## 検証した実装手段

| 実装手段 | PS Console表示 | MCPキャプチャ | タイムライン順序 | File I/O | コマンド実行回数 | 総合評価 |
|---------|---------------|--------------|----------------|----------|----------------|----------|
| **1. Tee-Object + ForEach-Object (全ストリーム)** | ❌ 全て白色 | ✅ 完璧 | ✅ 完璧 | ❌ なし | ✅ 1回 | **不可** |
| **2. Tee-Object + ストリーム別処理** | ❌ 全て白色 | ✅ 完璧 | ✅ 完璧 | ❌ なし | ✅ 1回 | **不可** |
| **3. コンソールAPI ($Host.UI.WriteLine)** | ❌ 一部のみ色付き | ✅ 完璧 | ✅ 完璧 | ❌ なし | ✅ 1回 | **不可** |
| **4. 変数キャプチャ (4>&1 5>&1含む)** | ✅ Warning/Verbose/Debug正常 | ✅ 全ストリーム | ⚠️ 部分的 | ❌ なし | ✅ 1回 | **良好** |
| **5. 変数キャプチャ (4>&1 5>&1除外)** | ✅ Warning/Verbose/Debug正常 | ⚠️ Verbose/Debug除外 | ⚠️ 部分的 | ❌ なし | ✅ 1回 | **実用的** |
| **6. Start-Transcript + 2重実行** | ✅ 完璧 | ✅ 完璧 | ✅ 完璧 | ❌ あり | ❌ 2回 | **理想的だが不可** |
| **7. Start-Transcript + ストリームリダイレクト** | ❌ 色情報消失 | ✅ 完璧 | ✅ 完璧 | ❌ あり | ✅ 1回 | **不可** |

## 詳細分析

### PowerShellストリームの動作特性

| ストリーム | 変数キャプチャ使用時のConsole表示 | 自然な色 |
|-----------|--------------------------------|---------|
| **Success (1)** | ✅ 正常表示 | 白色 |
| **Error (2)** | ❌ 表示抑制 (`-ErrorVariable`の副作用) | 赤色 |
| **Warning (3)** | ✅ 正常表示 | 黄色 |
| **Verbose (4)** | ✅ 正常表示 | 青色 |
| **Debug (5)** | ✅ 正常表示 | 黄色 |
| **Information (6)** | ✅ 正常表示 | 白色 |

### 重要な発見

1. **ErrorVariableの特殊性**
   - `-ErrorVariable`使用時のみ、標準Console表示が抑制される
   - 他のストリーム変数（`-WarningVariable`等）では表示が継続される
   - PowerShellの仕様として、Errorストリームはエラーハンドリング用途のため

2. **Tee-Objectの制約**
   - すべてのストリームを`Tee-Object`で処理すると色情報が完全に失われる
   - PowerShellの標準的な色分け処理が置き換えられてしまう

3. **ストリームリダイレクトの影響**
   - `2>&1 3>&1 4>&1 5>&1 6>&1`使用時は色情報が失われる
   - `4>&1 5>&1`使用時はVerbose/DebugのConsole表示が抑制される

## 実装選択肢の評価

### 選択肢A: 変数キャプチャ (4>&1 5>&1除外) - 現在の推奨
```powershell
Invoke-Expression $Command -OutVariable outVar -ErrorVariable errorVar -WarningVariable warningVar -InformationVariable informationVar
# Manual error display
foreach ($err in $errorVar) { Write-Host $err.Exception.Message -ForegroundColor Red }
```

**メリット:**
- Warning/Verbose/Debugが自然な色で表示
- Error/Warning/Information/Successがキャプチャ可能
- File I/Oなし
- 1回実行

**デメリット:**
- Verbose/Debugはキャプチャされない
- Errorの表示タイミングがずれる
- 完全なタイムライン順序ではない

### 選択肢B: Start-Transcript単独
```powershell
Start-Transcript -Path $tempFile
Invoke-Expression $Command
Stop-Transcript
```

**メリット:**
- 完璧な色分け表示
- 正確なタイミング
- 1回実行

**デメリット:**
- ストリームタイプ情報の損失
- File I/Oオーバーヘッド
- MCPでの詳細なストリーム分析不可

### 選択肢C: 技術的限界として現状維持
現在の実装（選択肢A）を維持し、以下を制約として受け入れる：
- Verbose/Debugのキャプチャなし
- Errorの表示タイミングずれ

## 推奨実装

**実用性を重視した現実的な選択: 選択肢A（変数キャプチャ方式）**

理由：
1. ユーザーはPS Consoleで主要な情報（Verbose/Debug含む）を正しい色で確認可能
2. MCPでは主要なストリーム（Error/Warning/Success）を正確にキャプチャ
3. パフォーマンスが良好（File I/Oなし）
4. 副作用なし（1回実行）

**制約として受け入れる点：**
- Verbose/DebugのMCPキャプチャなし → ユーザーにPS Console参照を案内
- Errorの表示タイミングずれ → 機能的には問題なし

## 技術的制約の根本原因

PowerShellの設計思想として：
- **表示系**: リアルタイム、色付き、ユーザーフレンドリー
- **キャプチャ系**: 構造化、プログラム処理用、詳細情報

この2つの要求を同時に満たすことは、PowerShellの内部アーキテクチャ上困難であることが判明。

## 結論

完璧なソリューションは存在しないが、**変数キャプチャ方式（4>&1 5>&1除外）**が最も実用的なバランスを提供する。ユーザーエクスペリエンスを最優先に、技術的制約を明確に理解した上での妥協案として推奨。
