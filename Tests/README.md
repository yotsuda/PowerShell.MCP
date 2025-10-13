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
│       ├── ShowTextFileCmdletTests.cs         # Show-TextFile (4テスト)
│       ├── AddLinesToFileCmdletTests.cs       # Add-LinesToFile (5テスト)
│       ├── UpdateLinesInFileCmdletTests.cs    # Update-LinesInFile (4テスト)
│       ├── RemoveLinesFromFileCmdletTests.cs  # Remove-LinesFromFile (3テスト)
│       ├── UpdateMatchInFileCmdletTests.cs    # Update-MatchInFile (4テスト)
│       └── TestTextFileContainsCmdletTests.cs # Test-TextFileContains (4テスト)
├── Integration/                               # PowerShell統合テスト
│   ├── Cmdlets/                               # Cmdlet統合テスト
│   │   ├── Show-TextFile.Integration.Tests.ps1
│   │   ├── Add-LinesToFile.Integration.Tests.ps1
│   │   ├── Update-LinesInFile.Integration.Tests.ps1
│   │   ├── Remove-LinesFromFile.Integration.Tests.ps1
│   │   ├── Update-MatchInFile.Integration.Tests.ps1
│   │   └── Test-TextFileContains.Integration.Tests.ps1
│   └── Scenarios/                             # エンドツーエンドシナリオ
│       ├── BasicOperations.Tests.ps1          # 基本操作シナリオ
│       └── AdvancedOperations.Tests.ps1       # 高度な操作シナリオ
├── TestData/                                  # テストデータ
│   ├── Encodings/                             # エンコーディングテスト用
│   └── Samples/                               # サンプルファイル
├── Shared/                                    # 共有ヘルパー
│   └── TestHelpers.psm1                       # PowerShellヘルパー関数
├── PowerShell.MCP.Tests.csproj
├── xunit.runner.json
├── README.md                                  # このファイル
├── COVERAGE.md                                # カバレッジレポート
└── Run-AllTests.ps1                           # テスト実行スクリプト
```

## 📊 テスト統計

- **総テスト数**: 65
- **ユニットテスト (C#)**: 33 (Core) + 24 (Cmdlets) = 57
- **統合テスト (PowerShell)**: 約167テスト
- **成功率**: 92.3% (60/65)

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

### C# ユニットテストのみ実行
```powershell
cd Tests
dotnet test --verbosity normal
```

### PowerShell 統合テストのみ実行
```powershell
# Pester 5.0+ が必要
Install-Module -Name Pester -Force -SkipPublisherCheck -MinimumVersion 5.0.0

# 統合テスト実行
Invoke-Pester -Path .\Tests\Integration
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
Import-Module "$PSScriptRoot\..\..\Shared\TestHelpers.psm1"

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
}
```

## 📚 ヘルパー関数

`Shared/TestHelpers.psm1` には以下のヘルパー関数があります:

- `New-TestFile`: テスト用の一時ファイルを作成
- `Remove-TestFile`: テストファイルを安全に削除
- `Get-TestDataPath`: TestDataディレクトリのパスを取得

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