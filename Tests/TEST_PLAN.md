# PowerShell.MCP テスト追加計画

## 作成日時
2025-10-12

## 現状
- **既存ユニットテスト**: 20テスト (TextFileUtilityTests.cs)
- **テストカバレッジ**: TextFileUtility クラスの主要メソッド
- **課題**: Cmdlet クラスのテストが不足

## テスト戦略

### 1. ユニットテスト (Tests/Unit/)
**目的**: 個別のメソッドやクラスの単体テスト
**対象**:
- ✅ TextFileUtility.cs (既存)
- ✅ TextFileCmdletBase.cs (既存)
- ✅ ValidationAttributes.cs (既存)
- ⚠ PowerShellProcessManager.cs (未実装)
- ⚠ PowerShellService.cs (未実装)
- ⚠ JsonRpcModels.cs (未実装)

### 2. 統合テスト (Tests/Integration/)
**目的**: Cmdletの実際の動作確認
**対象**:
- ✅ Test-AllCmdlets.ps1 (既存)
- ✅ Test-AdvancedCmdlets.ps1 (既存)
- ⚠ より包括的なシナリオテスト (追加予定)

### 3. エンドツーエンドテスト
**目的**: MCP プロトコル経由での完全な動作確認
**対象**:
- ⚠️ クライアント-サーバー通信テスト (未実装)

## 追加すべきテスト

### 優先度: 高

#### TextFileUtilityTests.cs への追加
1. **エンコーディング検出の拡張**
   - Shift-JIS, EUC-JP, ISO-8859-1 のテスト
   - BOM付き/なしのUTF-8
   - バイナリファイルの処理

2. **エラーハンドリング**
   - 存在しないファイル
   - アクセス権限がないファイル
   - ロックされたファイル
   - ディスク容量不足

3. **境界値テスト**
   - 空ファイル (既存)
   - 非常に大きなファイル
   - 非常に長い行
   - 特殊文字を含むパス

#### PowerShellProcessManagerTests.cs (新規)
1. プロセスの起動と停止
2. コマンド実行とエラーハンドリング
3. タイムアウト処理
4. 複数の同時実行

#### PowerShellServiceTests.cs (新規)
1. リクエストの処理
2. エラーレスポンスの生成
3. ストリーミング応答

### 優先度: 中

#### ValidationAttributesTests.cs への追加
1. ValidateLineRangeAttribute の境界値テスト
2. カスタムバリデーションメッセージのテスト
3. 複数の属性の組み合わせテスト

#### JsonRpcModelsTests.cs (新規)
1. JSON シリアライゼーション/デシリアライゼーション
2. エラーオブジェクトの作成
3. リクエスト/レスポンスのバリデーション

### 優先度: 低

#### パフォーマンステスト
1. 大規模ファイル処理のベンチマーク
2. メモリ使用量の監視
3. 同時実行パフォーマンス

## テスト実行方法

### ユニットテストの実行 (別セッションで)
```powershell
# 新しいPowerShellセッションを開く
cd C:\MyProj\PowerShell.MCP\Tests\Unit
dotnet test --verbosity normal
```

### 統合テストの実行
```powershell
cd C:\MyProj\PowerShell.MCP\Tests
.\Run-AllTests.ps1
```

## 注意事項

⚠ **重要**: PowerShell.MCP プロジェクトのデプロイは AI が行うことはできない。その必要があるときはユーザーに依頼すること。

## 次のステップ

1. ✅ テスト計画の作成 (このドキュメント)
2. ⬜ TextFileUtilityTests.cs へのテスト追加
3. ⬜ PowerShellProcessManagerTests.cs の作成
4. ⬜ PowerShellServiceTests.cs の作成
5. ⬜ 統合テストの拡張
6. ⬜ CI/CDパイプラインへの組み込み

## 参考資料

- コード品質レポート: `CodeQualityReport_20251012_195425.md`
- テスト状況: `Tests\TEST_STATUS.md`
- xUnit ドキュメント: https://xunit.net/
- PowerShell SDK: https://docs.microsoft.com/powershell/scripting/developer/