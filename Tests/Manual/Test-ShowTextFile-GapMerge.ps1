# Show-TextFile ギャップマージ機能の手動テスト
# このスクリプトを実行して、出力を目視確認してください

Write-Host "=== Show-TextFile ギャップマージ機能テスト (前後2行) ===" -ForegroundColor Cyan

# テスト1: ギャップ0行（マージされるべき）
Write-Host "`n【テスト1】ギャップ0行 - マージされる" -ForegroundColor Yellow
$content1 = @"
Line 1: xxx
Line 2: xxx
Line 3: MATCH
Line 4: xxx
Line 5: xxx
Line 6: xxx
Line 7: MATCH
Line 8: xxx
Line 9: xxx
"@
$testFile1 = "test-gap0.txt"
Set-Content -Path $testFile1 -Value $content1
Show-TextFile -Path $testFile1 -Contains "MATCH"
Remove-Item $testFile1

# テスト2: ギャップ1行（マージされるべき）
Write-Host "`n【テスト2】ギャップ1行 - マージされる（7行目が表示される）" -ForegroundColor Yellow
$content2 = @"
Line 1: xxx
Line 2: xxx
Line 3: MATCH
Line 4: xxx
Line 5: xxx
Line 6: xxx
Line 7: xxx (この行が表示されるべき)
Line 8: xxx
Line 9: MATCH
Line 10: xxx
Line 11: xxx
"@
$testFile2 = "test-gap1.txt"
Set-Content -Path $testFile2 -Value $content2
Show-TextFile -Path $testFile2 -Contains "MATCH"
Remove-Item $testFile2

# テスト3: ギャップ2行（マージされるべき）
Write-Host "`n【テスト3】ギャップ2行 - マージされる（7-8行目がギャップ行として表示される）" -ForegroundColor Yellow
$content3 = @"
Line 1: xxx
Line 2: xxx
Line 3: MATCH
Line 4: xxx
Line 5: xxx
Line 6: xxx
Line 7: xxx (この行が表示されるべき)
Line 8: xxx (この行が表示されるべき)
Line 9: xxx
Line 10: MATCH
Line 11: xxx
Line 12: xxx
"@
$testFile3 = "test-gap2.txt"
Set-Content -Path $testFile3 -Value $content3
Show-TextFile -Path $testFile3 -Contains "MATCH"
Remove-Item $testFile3

# テスト4: ギャップ3行（分離されるべき）
Write-Host "`n【テスト4】ギャップ3行 - 分離される（7-9行目は表示されない）" -ForegroundColor Yellow
$content4 = @"
Line 1: xxx
Line 2: xxx
Line 3: MATCH
Line 4: xxx
Line 5: xxx
Line 6: xxx
Line 7: xxx (この行は表示されないべき)
Line 8: xxx (この行は表示されないべき)
Line 9: xxx (この行は表示されるべき)
Line 10: xxx
Line 11: MATCH
Line 12: xxx
Line 13: xxx
"@
$testFile4 = "test-gap3.txt"
Set-Content -Path $testFile4 -Value $content4
Show-TextFile -Path $testFile4 -Contains "MATCH"
Remove-Item $testFile4

# テスト5: ギャップ4行（分離されるべき）
Write-Host "`n【テスト5】ギャップ4行 - 分離される（7-10行目が非表示）" -ForegroundColor Yellow
$content5 = @"
Line 1: xxx
Line 2: xxx
Line 3: MATCH
Line 4: xxx
Line 5: xxx
Line 6: xxx
Line 7: xxx (この行は表示されないべき)
Line 8: xxx (この行は表示されないべき)
Line 9: xxx (この行は表示されるべき)
Line 10: xxx (この行は表示されないべき)
Line 11: xxx
Line 12: MATCH
Line 13: xxx
Line 14: xxx
"@
$testFile5 = "test-gap4.txt"
Set-Content -Path $testFile5 -Value $content5
Show-TextFile -Path $testFile5 -Contains "MATCH"
Remove-Item $testFile5

# テスト6: grep標準フォーマット確認
Write-Host "`n【テスト6】grep標準フォーマット - マッチ行は:、コンテキスト行は-" -ForegroundColor Yellow
$content6 = @"
Context line 1
Context line 2
MATCH line
Context line 4
Context line 5
"@
$testFile6 = "test-format.txt"
Set-Content -Path $testFile6 -Value $content6
Write-Host "期待する出力:" -ForegroundColor Gray
Write-Host "  1- Context line 1" -ForegroundColor Gray
Write-Host "  2- Context line 2" -ForegroundColor Gray
Write-Host "  3: MATCH line" -ForegroundColor Gray
Write-Host "  4- Context line 4" -ForegroundColor Gray
Write-Host "  5- Context line 5" -ForegroundColor Gray
Write-Host "`n実際の出力:" -ForegroundColor Gray
Show-TextFile -Path $testFile6 -Contains "MATCH"
Remove-Item $testFile6

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
