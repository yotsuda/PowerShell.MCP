# ASCII エンコーディング自動変換テストの網羅性レポート

## テストケースの網羅性分析結果

### Add-LinesToFile

#### ✅ 実装済みテストケース
1. ASCII ファイルに日本語を末尾追加 → UTF-8 にアップグレード

#### ⚠️ 不足しているテストケース (優先度: 高)
1. **Encoding明示指定時のアップグレード抑制** - 実装の重要な動作
2. **ASCII範囲のみの内容でアップグレードしない** - 不要な変換を避ける確認

#### 📋 不足しているテストケース (優先度: 中)
3. LineNumber指定(先頭/中間)での挿入
4. 複数ファイル一括処理
5. Backup指定時の動作
6. 新規ファイル作成時の動作

### Update-LinesInFile

#### ✅ 実装済みテストケース
1. ASCII ファイルの単一行を日本語で更新 → UTF-8 にアップグレード

#### ⚠️ 不足しているテストケース (優先度: 高)
1. **Encoding明示指定時のアップグレード抑制** - 実装の重要な動作
2. **ASCII範囲のみの内容でアップグレードしない** - 不要な変換を避ける確認

#### 📋 不足しているテストケース (優先度: 中)
3. 複数行更新、先頭/末尾行更新
4. 複数ファイル一括処理
5. Backup指定時の動作

## 推奨事項

### 最優先で追加すべきテスト (Critical)
1. **Encoding明示指定時のアップグレード抑制テスト**
   - 理由: TryUpgradeEncodingIfNeedのncodingExplicitlySpecifiedパラメータの動作検証
   - Add-LinesToFile/Update-LinesInFile 両方に必要

2. **ASCII範囲のみの内容でアップグレードしないテスト**
   - 理由: 不要なエンコーディング変換を避ける動作の確認
   - Add-LinesToFile/Update-LinesInFile 両方に必要

### 推奨される追加テスト (Recommended)
3. LineNumber/LineRange の様々なパターン
4. 複数ファイル処理
5. Backup機能との組み合わせ

## 実装詳細

### エンコーディング検出の動作 (検証済み)
- ASCII範囲のみの内容 → US-ASCII (CodePage 20127) として検出
- 非ASCII文字を含む内容 → UTF-8 (CodePage 65001) として検出

### アップグレード条件 (EncodingHelper.TryUpgradeEncodingIfNeeded)
1. Encoding が明示指定されていない (ncodingExplicitlySpecified == false)
2. 現在のエンコーディングが ASCII (CodePage 20127)
3. 内容に非ASCII文字が含まれる (char > 127)

### 現在のテストの課題
- ポジティブテスト(アップグレードする)のみ実装
- ネガティブテスト(アップグレードしない)が不足
- 様々なシナリオの組み合わせテストが不足
