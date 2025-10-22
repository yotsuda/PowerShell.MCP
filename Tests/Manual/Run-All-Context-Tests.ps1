# PowerShell.MCP コンテキスト表示機能 - 統合テストランナー
Write-Host "=== PowerShell.MCP コンテキスト表示機能 - 全テスト実行 ===" -ForegroundColor Cyan
Write-Host ""

$testFiles = @(
    "Test-AddLinesToFile-Context.ps1",
    "Test-AddLinesToFile-EdgeCases.ps1",
    "Test-UpdateLinesInFile-Context.ps1", 
    "Test-UpdateLinesInFile-EdgeCases.ps1",
    "Test-UpdateMatchInFile-Context.ps1",
    "Test-ShowTextFile-GapMerge.ps1",
    "Test-Match-EdgeCases.ps1",
    "Test-Omit-Display.ps1"
)

$testPath = "C:\MyProj\PowerShell.MCP\Tests\Manual"
$passed = 0
$failed = 0

foreach ($testFile in $testFiles) {
    $fullPath = Join-Path $testPath $testFile
    
    if (Test-Path $fullPath) {
        Write-Host "Running: $testFile" -ForegroundColor Yellow
        try {
            & $fullPath
            $passed++
            Write-Host "✓ PASSED: $testFile" -ForegroundColor Green
        }
        catch {
            $failed++
            Write-Host "✗ FAILED: $testFile" -ForegroundColor Red
            Write-Host "Error: $_" -ForegroundColor Red
        }
        Write-Host ""
    }
    else {
        Write-Host "⚠ NOT FOUND: $testFile" -ForegroundColor Yellow
        Write-Host ""
    }
}

Write-Host "=== テスト結果サマリー ===" -ForegroundColor Cyan
Write-Host "  Total:  $($passed + $failed)"
Write-Host "  Passed: $passed" -ForegroundColor Green
Write-Host "  Failed: $failed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })

if ($failed -eq 0) {
    Write-Host "`n✓ 全テスト成功！" -ForegroundColor Green
}
else {
    Write-Host "`n✗ 一部テスト失敗" -ForegroundColor Red
}
