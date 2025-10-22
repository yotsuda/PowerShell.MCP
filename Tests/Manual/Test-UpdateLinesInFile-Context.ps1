# Update-LinesInFile コンテキスト表示機能のテスト
Write-Host "=== Update-LinesInFile コンテキスト表示テスト ===" -ForegroundColor Cyan

# テスト1: 1行更新
Write-Host "`n【テスト1】1行更新 - 行5を置換" -ForegroundColor Yellow
$content1 = @"
Line 1
Line 2
Line 3
Line 4
Line 5: original
Line 6
Line 7
Line 8
Line 9
Line 10
"@
Set-Content -Path "test-update1.txt" -Value $content1
Update-LinesInFile -Path "test-update1.txt" -LineRange 5,5 -Content "Line 5: REPLACED"
Remove-Item "test-update1.txt"

# テスト2: 複数行更新（同じ行数）
Write-Host "`n【テスト2】複数行更新 - 行5-7を3行で置換" -ForegroundColor Yellow
$content2 = @"
Line 1
Line 2
Line 3
Line 4
Line 5: original
Line 6: original
Line 7: original
Line 8
Line 9
Line 10
"@
Set-Content -Path "test-update2.txt" -Value $content2
Update-LinesInFile -Path "test-update2.txt" -LineRange 5,7 -Content @("Line 5: NEW", "Line 6: NEW", "Line 7: NEW")
Remove-Item "test-update2.txt"

# テスト3: 範囲縮小（3行→1行）
Write-Host "`n【テスト3】範囲縮小 - 行5-7を1行に置換" -ForegroundColor Yellow
$content3 = @"
Line 1
Line 2
Line 3
Line 4
Line 5: original
Line 6: original
Line 7: original
Line 8
Line 9
Line 10
"@
Set-Content -Path "test-update3.txt" -Value $content3
Update-LinesInFile -Path "test-update3.txt" -LineRange 5,7 -Content "Line 5: MERGED"
Remove-Item "test-update3.txt"

# テスト4: 範囲拡大（1行→3行）
Write-Host "`n【テスト4】範囲拡大 - 行5を3行に置換" -ForegroundColor Yellow
$content4 = @"
Line 1
Line 2
Line 3
Line 4
Line 5: original
Line 6
Line 7
Line 8
Line 9
Line 10
"@
Set-Content -Path "test-update4.txt" -Value $content4
Update-LinesInFile -Path "test-update4.txt" -LineRange 5,5 -Content @("Line 5: NEW 1", "Line 6: NEW 2", "Line 7: NEW 3")
Remove-Item "test-update4.txt"

# テスト5: 行削除（Content省略）
Write-Host "`n【テスト5】行削除 - 行5-7を削除" -ForegroundColor Yellow
$content5 = @"
Line 1
Line 2
Line 3
Line 4
Line 5: to be deleted
Line 6: to be deleted
Line 7: to be deleted
Line 8
Line 9
Line 10
"@
Set-Content -Path "test-update5.txt" -Value $content5
Update-LinesInFile -Path "test-update5.txt" -LineRange 5,7
Remove-Item "test-update5.txt"

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
