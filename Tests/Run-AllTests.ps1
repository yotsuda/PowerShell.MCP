# Run-AllTests.ps1
# すべてのテストを実行するヘルパースクリプト

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("Unit", "Integration", "All")]
    [string]$TestType = "All",
    
    [Parameter()]
    [switch]$Detailed,
    
    [Parameter()]
    [switch]$CodeCoverage
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "PowerShell.MCP Test Runner" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Pester のバージョン確認
$pesterModule = Get-Module -ListAvailable -Name Pester | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $pesterModule) {
    Write-Host "⚠ Pester モジュールがインストールされていません。" -ForegroundColor Yellow
    Write-Host "インストールするには: Install-Module -Name Pester -Force -SkipPublisherCheck" -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ Pester バージョン: $($pesterModule.Version)" -ForegroundColor Green

# C# ユニットテストの実行
if ($TestType -in @("Unit", "All")) {
    Write-Host "`n--- C# Unit Tests ---" -ForegroundColor Cyan
    $testProjectPath = $scriptRoot
    
    if (Test-Path (Join-Path $testProjectPath "PowerShell.MCP.Tests.csproj")) {
        Push-Location $testProjectPath
        try {
            Write-Host "dotnet test を実行中..." -ForegroundColor Yellow
            dotnet test --verbosity quiet --nologo
            if ($LASTEXITCODE -ne 0) {
                Write-Host "❌ C# ユニットテストが失敗しました。" -ForegroundColor Red
            } else {
                Write-Host "✓ C# ユニットテストが成功しました。" -ForegroundColor Green
            }
        }
        finally {
            Pop-Location
        }
    } else {
        Write-Host "⚠ Test project not found: PowerShell.MCP.Tests.csproj" -ForegroundColor Yellow
    }
}

# PowerShell 統合テストの実行
if ($TestType -in @("Integration", "All")) {
    Write-Host "`n--- PowerShell Integration Tests ---" -ForegroundColor Cyan
    $integrationTestPath = Join-Path $scriptRoot "Integration"
    
    if (Test-Path $integrationTestPath) {
        $testFiles = Get-ChildItem -Path $integrationTestPath -Filter "*.Tests.ps1"
        
        if ($testFiles.Count -eq 0) {
            Write-Host "⚠ 統合テストファイルが見つかりません。" -ForegroundColor Yellow
        } else {
            Write-Host "Found $($testFiles.Count) test file(s)" -ForegroundColor Yellow
            
            $pesterConfig = @{
                Run = @{
                    Path = $integrationTestPath
                    PassThru = $true
                }
                Output = @{
                    Verbosity = if ($Detailed) { "Detailed" } else { "None" }
                    StackTraceVerbosity = "None"
                    CIFormat = "None"
                }
                Should = @{
                    ErrorAction = 'SilentlyContinue'
                }
            }
            
            if ($CodeCoverage) {
                $pesterConfig.CodeCoverage = @{
                    Enabled = $true
                }
            }
            
            # *.Tests.ps1 ファイルを探すようにPesterに指示
            $config = New-PesterConfiguration -Hashtable $pesterConfig
            $config.Run.Path = Get-ChildItem -Path $integrationTestPath -Filter "*.Tests.ps1" | Select-Object -ExpandProperty FullName
            # テスト実行（Output.Verbosity = "None" で出力を最小限に）
            $result = Invoke-Pester -Configuration $config
            
            Write-Host "`n--- Test Results ---" -ForegroundColor Cyan
            Write-Host "Total: $($result.TotalCount)" -ForegroundColor White
            Write-Host "Passed: $($result.PassedCount)" -ForegroundColor Green
            Write-Host "Failed: $($result.FailedCount)" -ForegroundColor Red
            Write-Host "Skipped: $($result.SkippedCount)" -ForegroundColor Yellow
            
            if ($result.FailedCount -gt 0) {
                Write-Host "`n❌ 統合テストに失敗がありました。" -ForegroundColor Red
                exit 1
            } else {
                Write-Host "`n✓ すべての統合テストが成功しました。" -ForegroundColor Green
            }
        }
    } else {
        Write-Host "⚠ Integration test path not found: $integrationTestPath" -ForegroundColor Yellow
    }
}

Write-Host "`n==================================" -ForegroundColor Cyan
Write-Host "Test execution completed!" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

<#
.SYNOPSIS
    PowerShell.MCP のすべてのテストを実行します。

.DESCRIPTION
    このスクリプトは、C# ユニットテストと PowerShell 統合テストの両方を実行します。

.PARAMETER TestType
    実行するテストのタイプを指定します。"Unit", "Integration", または "All" (デフォルト)。

.PARAMETER Detailed
    詳細な出力を表示します。

.PARAMETER CodeCoverage
    コードカバレッジを有効にします (PowerShell 統合テストのみ)。

.EXAMPLE
    .\Run-AllTests.ps1
    すべてのテストを実行します。

.EXAMPLE
    .\Run-AllTests.ps1 -TestType Unit
    C# ユニットテストのみを実行します。

.EXAMPLE
    .\Run-AllTests.ps1 -TestType Integration -Detailed
    PowerShell 統合テストのみを詳細出力で実行します。
#>