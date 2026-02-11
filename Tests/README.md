# PowerShell.MCP Tests

このディレクトリには、PowerShell.MCP プロジェクトの包括的なテストスイートが含まれています。

## 📁 フォルダ構造

```
Tests/
├── Unit/                                      # C# ユニットテスト
│   ├── Core/                                  # コア機能のテスト
│   │   ├── TextFileUtilityTests.cs            # ユーティリティクラス (20テスト)
│   │   ├── TextFileCmdletBaseTests.cs         # 基底クラス (8テスト)
│   │   └── ValidationAttributesTests.cs       # バリデーション属性 (5テスト)
│   └── Cmdlets/                               # Cmdlet個別テスト
│       ├── ShowTextFilesCmdletTests.cs         # Show-TextFiles (4テスト)
│       ├── AddLinesToFileCmdletTests.cs       # Add-LinesToFile (5テスト)
│       ├── UpdateLinesInFileCmdletTests.cs    # Update-LinesInFile (4テスト)
│       ├── RemoveLinesFromFileCmdletTests.cs  # Remove-LinesFromFile (3テスト)
│       ├── UpdateMatchInFileCmdletTests.cs    # Update-MatchInFile (4テスト)
│       └── TestTextFileContainsCmdletTests.cs # Test-TextFileContains (4テスト)
├── Integration/                               # PowerShell統合テスト
│   ├── BlankLineSeparation.Tests.ps1          # 空行分離テスト
│   ├── ContextDisplay.Tests.ps1               # コンテキスト表示テスト
│   ├── ContextDisplay.EdgeCase.Tests.ps1      # コンテキスト表示エッジケース
│   ├── NetDisplay.Tests.ps1                   # net 変化表示テスト
│   ├── QuietErrorHandling.Tests.ps1           # Test-ThrowsQuietly 実用例
│   ├── TestThrowsQuietly.Tests.ps1            # Test-ThrowsQuietly 関数テスト
│   └── ErrorOutputComparison.Tests.ps1        # エラー出力比較テスト
├── Manual/                                    # 手動テスト
│   └── Show-TextFiles.Manual.Tests.ps1         # Show-TextFiles 手動テスト
├── TestData/                                  # テストデータ
│   ├── Encodings/                             # エンコーディングテスト用
│   └── Samples/                               # サンプルファイル
├── Shared/                                    # 共有ヘルパー
│   └── TestHelpers.psm1                       # PowerShellヘルパー関数
│                                               # - New-TestFile
│                                               # - Remove-TestFile
│                                               # - Test-ThrowsQuietly
├── PowerShell.MCP.Tests.csproj
├── xunit.runner.json
├── README.md                                  # このファイル
├── COVERAGE.md                                # カバレッジレポート
└── Run-AllTests.ps1                           # テスト実行スクリプト
```

## 📊 テスト統計

- **総テスト数**: 約300+
- **ユニットテスト (C#)**: 96
- **統合テスト (PowerShell)**: 281
- **成功率**: 100% ✅

## 🎯 テスト戦略

### ユニットテスト (C#)
- **目的**: 個別メソッド/クラスの振る舞いを検証
- **範囲**: public/internal メソッド
- **モック**: 必要に応じてMoqを使用
- **速度**: 高速 (< 100ms/テスト)

### 統合テスト (PowerShell)
- **目的**: Cmdletの実際の動作を検証
- **範囲**: エンドツーエンドの動作
- **モック**: なし (実ファイルを使用)
- **速度**: 中速 (< 1s/テスト)

## 🚀 テストの実行

### すべてのテストを実行
```powershell
.\Tests\Run-AllTests.ps1
```

### C# ユニットテストのみ実行（簡潔な出力）
```powershell
cd Tests
dotnet test --verbosity quiet --nologo
```

**詳細な出力が必要な場合:**
```powershell
dotnet test --verbosity normal
```

### PowerShell 統合テストのみ実行（簡潔な出力）
```powershell
# Pester 5.0+ が必要
Install-Module -Name Pester -Force -SkipPublisherCheck -MinimumVersion 5.0.0

# 統合テスト実行（最小限の出力）
$config = New-PesterConfiguration
$config.Run.Path = ".\Tests\Integration"
$config.Output.Verbosity = "Minimal"
Invoke-Pester -Configuration $config
```

**詳細な出力が必要な場合:**
```powershell
Invoke-Pester -Path .\Tests\Integration

### 簡潔なエラー出力でテスト実行
```powershell
# エラーメッセージをフィルタリングして読みやすく表示
.\Tests\Invoke-PesterConcise.ps1

# 特定のテストのみ実行
.\Tests\Invoke-PesterConcise.ps1 -Path Integration/Cmdlets/Show-TextFiles.Tests.ps1

# ヘルプを表示
Get-Help .\Tests\Invoke-PesterConcise.ps1 -Examples
```

**フィルタリング内容:**
- 内部例外スタックトレース（`--->`, `--- End of`）
- `System.Management.Automation.*Exception` の詳細行
- 重複する例外メッセージ
- 詳細なスタックトレース行
```

### カバレッジ付きで実行
```powershell
cd Tests
dotnet test --collect:"XPlat Code Coverage"
```

## 🧪 新しいテストの追加

### C# ユニットテストの追加

```csharp
// Tests/Unit/Cmdlets/NewCmdletTests.cs
using Xunit;
using PowerShell.MCP.Cmdlets;

namespace PowerShell.MCP.Tests.Unit.Cmdlets;

public class NewCmdletTests
{
    [Fact]
    public void MethodName_Condition_ExpectedBehavior()
    {
        // Arrange
        var cmdlet = new NewCmdlet();
        
        // Act
        var result = cmdlet.DoSomething();
        
        // Assert
        Assert.NotNull(result);
    }
}
```

### PowerShell 統合テストの追加

```powershell
# Tests/Integration/Cmdlets/New-Cmdlet.Integration.Tests.ps1
#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../../Shared/TestHelpers.psm1" -Force
}

Describe "New-Cmdlet Integration Tests" {
    BeforeAll {
        $testFile = New-TestFile -Content "test"
    }

    AfterAll {
        Remove-TestFile -Path $testFile
    }

    Context "基本機能" {
        It "期待通りに動作する" {
            $result = New-Cmdlet -Path $testFile
            $result | Should -Not -BeNullOrEmpty
        }
    }
    
    Context "エラーハンドリング" {
        It "存在しないファイルでエラー" {
            Test-ThrowsQuietly {
                New-Cmdlet -Path "C:\NonExistent\file.txt"
            } -ExpectedMessage "File not found"
        }
        
        It "無効なパラメータでエラー" {
            Test-ThrowsQuietly {
                New-Cmdlet -Path $testFile -InvalidParam -999
            } -ExpectedMessage "less than the minimum"
        }
    }
}
```

**重要**: エラーケースのテストには必ず `Test-ThrowsQuietly` を使用してください。`Should -Throw` は大量のエラー出力を生成するため推奨されません。

## 📚 ヘルパー関数

`Shared/TestHelpers.psm1` には以下のヘルパー関数があります:

- `New-TestFile`: テスト用の一時ファイルを作成
- `Remove-TestFile`: テストファイルを安全に削除
- `Test-ThrowsQuietly`: 例外を検証しながらエラー出力を完全に抑制（**推奨**）

### Test-ThrowsQuietly の使用方法

**目的**: Pester テストでエラーケースを検証する際、大量のエラーメッセージとスタックトレースの出力を抑制し、トークン消費を大幅に削減します。

**基本的な使用例:**
```powershell
# 従来の方法（大量のエラー出力が発生）
It "Should throw on missing file" {
    { Show-TextFiles -Path "missing.txt" } | Should -Throw
}

# 推奨される方法（エラー出力を抑制）
It "Should throw on missing file" {
    Test-ThrowsQuietly { Show-TextFiles -Path "missing.txt" }
}
```

**メッセージ検証付き:**
```powershell
It "Should throw file not found error" {
    Test-ThrowsQuietly { 
        Show-TextFiles -Path "C:\NonExistent\file.txt" 
    } -ExpectedMessage "File not found"
}
```

**複雑なエラーケース:**
```powershell
It "Should throw on invalid LineRange" {
    $temp = New-TemporaryFile
    "test" | Out-File $temp
    try {
        Test-ThrowsQuietly {
            Show-TextFiles -Path $temp -LineRange @(10, 5)
        } -ExpectedMessage "must be less than or equal to"
    } finally {
        Remove-Item $temp -Force
    }
}
```

**動作の詳細:**
- `$Error.Clear()` を try 前後で2回実行してエラー履歴をクリア
- `*>&1` ですべての出力ストリームをリダイレクト
- `$null = ...` で出力を完全に破棄
- `ErrorActionPreference = 'Stop'` で非終了エラーを例外に変換
- `$PSDefaultParameterValues['*:ErrorAction'] = 'Stop'` ですべてのコマンドに -ErrorAction Stop を自動適用

**適用されるエラータイプ:**
- ✅ 終了エラー（ThrowTerminatingError）
- ✅ パラメータ検証エラー（ValidateRange など）
- ⚠️ 非終了エラー（WriteError）- PowerShell と C# cmdlet の制限により部分的にサポート

## 🎯 エラーハンドリングのベストプラクティス

### エラーテストの書き方

**推奨**: すべてのエラーテストで `Test-ThrowsQuietly` を使用してください。

```powershell
Describe "Error Handling Tests" {
    Context "Invalid parameters" {
        It "Negative LineNumber throws" {
            Test-ThrowsQuietly {
                Add-LinesToFile -Path "test.txt" -LineNumber -5 -Content "test"
            } -ExpectedMessage "less than the minimum"
        }
        
        It "Invalid LineRange throws" {
            Test-ThrowsQuietly {
                Show-TextFiles -Path "test.txt" -LineRange @(10, 5)
            } -ExpectedMessage "less than or equal to"
        }
    }
    
    Context "Missing files" {
        It "File not found throws" {
            Test-ThrowsQuietly {
                Show-TextFiles -Path "C:\NonExistent\file.txt"
            } -ExpectedMessage "File not found"
        }
    }
}
```

### エラー出力の比較

**従来の Should -Throw:**
- 各エラーごとに数百〜数千文字のスタックトレースを出力
- トークン消費が激しい
- テスト結果が読みにくい

**Test-ThrowsQuietly:**
- エラー出力を完全に抑制
- トークン消費を大幅に削減（90%以上削減）
- テスト結果が読みやすい
- 例外の有無とメッセージのみを検証

### 実例

Tests/Integration ディレクトリには以下の実例があります：
- `QuietErrorHandling.Tests.ps1` - Test-ThrowsQuietly の実用例
- `TestThrowsQuietly.Tests.ps1` - Test-ThrowsQuietly 関数自体のテスト
- `ErrorOutputComparison.Tests.ps1` - Should -Throw との比較
## 🔧 必要な環境

- .NET 9.0 SDK
- PowerShell 7.2+
- xUnit (自動インストール)
- Moq (自動インストール)
- Pester 5.0+ (統合テスト用)

## 📖 参考資料

- [xUnit ドキュメント](https://xunit.net/)
- [Moq ドキュメント](https://github.com/moq/moq4)
- [Pester ドキュメント](https://pester.dev/)
- [カバレッジレポート](COVERAGE.md)

## ライセンス

このテストスイートは PowerShell.MCP プロジェクトの一部であり、同じライセンスが適用されます。