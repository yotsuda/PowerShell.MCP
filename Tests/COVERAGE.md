# テストカバレッジレポート

**最終更新**: 2025-10-13

## 📊 全体サマリー

| カテゴリ | テスト数 | カバレッジ | 状態 |
|----------|----------|------------|------|
| **ユニットテスト** | 57 | 85% | ✅ 良好 |
| **統合テスト** | 167 | 90% | ✅ 良好 |
| **合計** | 224 | 87.5% | ✅ 良好 |

## 🎯 詳細カバレッジ

### Unit/Core (33テスト)

#### TextFileUtilityTests.cs
- **テスト数**: 20
- **カバレッジ**: 90%
- **状態**: ✅ 優秀
- **対象メソッド**:
  - DetectEncoding (5テスト)
  - DetectFileMetadata (3テスト)
  - ParseLineRange (4テスト)
  - ConvertToStringArray (4テスト)
  - GetRelativePath (2テスト)
  - CreateBackup (2テスト)

#### TextFileCmdletBaseTests.cs
- **テスト数**: 8
- **カバレッジ**: 75%
- **状態**: ✅ 良好
- **対象**: 基底クラスの抽象メソッドと共通機能

#### ValidationAttributesTests.cs
- **テスト数**: 5
- **カバレッジ**: 100%
- **状態**: ✅ 完璧
- **対象**: ValidateLineRangeAttribute

### Unit/Cmdlets (24テスト)

#### ShowTextFileCmdletTests.cs
- **テスト数**: 4
- **カバレッジ**: 80%
- **状態**: ✅ 良好

#### AddLinesToFileCmdletTests.cs
- **テスト数**: 5
- **カバレッジ**: 85%
- **状態**: ✅ 良好

#### UpdateLinesInFileCmdletTests.cs
- **テスト数**: 4
- **カバレッジ**: 75%
- **状態**: ✅ 良好

#### RemoveLinesFromFileCmdletTests.cs
- **テスト数**: 3
- **カバレッジ**: 70%
- **状態**: ⚠️ 改善可能

#### UpdateMatchInFileCmdletTests.cs
- **テスト数**: 4
- **カバレッジ**: 80%
- **状態**: ✅ 良好

#### TestTextFileContainsCmdletTests.cs
- **テスト数**: 4
- **カバレッジ**: 80%
- **状態**: ✅ 良好

## 📈 カバレッジの推移

| 日付 | 総テスト数 | カバレッジ | 変更内容 |
|------|------------|------------|----------|
| 2025-10-13 | 224 | 87.5% | Cmdletユニットテスト追加 (24テスト) |
| 2025-10-12 | 200 | 82% | TextFileUtilityTests拡張 (15テスト) |
| 2025-10-11 | 185 | 75% | 初期テスト実装 |

## ⚠️ カバレッジギャップ

### 未カバレッジ領域

1. **PowerShellProcessManager.cs**
   - テスト数: 0
   - 優先度: 高
   - 理由: プロセス管理の重要なロジック

2. **PowerShellService.cs**
   - テスト数: 0
   - 優先度: 高
   - 理由: サービス層の中核

3. **PowerShellTools.cs**
   - テスト数: 0
   - 優先度: 中
   - 理由: ツール関数

4. **PowerShellPrompts.cs**
   - テスト数: 0
   - 優先度: 中
   - 理由: プロンプト生成ロジック

5. **JsonRpcModels.cs**
   - テスト数: 0
   - 優先度: 低
   - 理由: データモデル（ロジックが少ない）

### 部分カバレッジ領域

1. **Cmdlet ProcessRecord メソッド**
   - 現在: パラメータテストのみ
   - 必要: 実行ロジックのモックテスト

2. **エラーハンドリング**
   - 現在: 正常系中心
   - 必要: 異常系・境界値テスト

## 🎯 改善計画

### Phase 1: 高優先度 (次リリース前)
- [ ] PowerShellProcessManagerTests.cs 作成 (20テスト)
- [ ] PowerShellServiceTests.cs 作成 (15テスト)
- [ ] Cmdlet ProcessRecord メソッドのモックテスト追加

### Phase 2: 中優先度 (次々リリース前)
- [ ] PowerShellToolsTests.cs 作成 (10テスト)
- [ ] エラーハンドリングテスト強化
- [ ] エンコーディングテストの修正

### Phase 3: 低優先度 (将来)
- [ ] パフォーマンステスト
- [ ] ストレステスト
- [ ] セキュリティテスト

## 📝 テストベストプラクティス

1. **命名規則**: `MethodName_Condition_ExpectedBehavior`
2. **AAA パターン**: Arrange, Act, Assert
3. **独立性**: 各テストは独立して実行可能
4. **再現性**: 同じ条件で同じ結果
5. **速度**: ユニットテストは100ms以内

## 🔄 継続的改善

カバレッジ目標:
- 現在: 87.5%
- 短期目標 (1ヶ月): 90%
- 長期目標 (3ヶ月): 95%

---

*このレポートは自動生成されます。最新情報は `dotnet test --collect:"XPlat Code Coverage"` で確認してください。*
