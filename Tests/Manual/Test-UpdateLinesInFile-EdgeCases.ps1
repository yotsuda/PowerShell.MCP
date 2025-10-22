# エッジケーステスト - Update-LinesInFile
Write-Host "=== Update-LinesInFile エッジケーステスト ===" -ForegroundColor Cyan

# テスト1: ファイル先頭行の更新
Write-Host "`n【テスト1】ファイル先頭行の更新" -ForegroundColor Yellow
$content = @"
Line 1: original
Line 2
Line 3
Line 4
Line 5
"@
Set-Content -Path "edge-update1.txt" -Value $content
Update-LinesInFile -Path "edge-update1.txt" -LineRange 1,1 -Content "Line 1: UPDATED"
Remove-Item "edge-update1.txt"

# テスト2: ファイル末尾行の更新
Write-Host "`n【テスト2】ファイル末尾行の更新" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
Line 4
Line 5: original
"@
Set-Content -Path "edge-update2.txt" -Value $content
Update-LinesInFile -Path "edge-update2.txt" -LineRange 5,5 -Content "Line 5: UPDATED"
Remove-Item "edge-update2.txt"

# テスト3: 1行ファイルの更新
Write-Host "`n【テスト3】1行ファイルの更新" -ForegroundColor Yellow
$content = @"
Only one line
"@
Set-Content -Path "edge-update3.txt" -Value $content
Update-LinesInFile -Path "edge-update3.txt" -LineRange 1,1 -Content "UPDATED LINE"
Remove-Item "edge-update3.txt"

# テスト4: 2行ファイルの先頭行削除
Write-Host "`n【テスト4】2行ファイルの先頭行削除" -ForegroundColor Yellow
$content = @"
Line 1: delete me
Line 2
"@
Set-Content -Path "edge-update4.txt" -Value $content
Update-LinesInFile -Path "edge-update4.txt" -LineRange 1,1
Remove-Item "edge-update4.txt"

# テスト5: ファイル先頭3行の削除
Write-Host "`n【テスト5】ファイル先頭3行の削除" -ForegroundColor Yellow
$content = @"
Line 1: delete
Line 2: delete
Line 3: delete
Line 4
Line 5
Line 6
"@
Set-Content -Path "edge-update5.txt" -Value $content
Update-LinesInFile -Path "edge-update5.txt" -LineRange 1,3
Remove-Item "edge-update5.txt"

# テスト6: ファイル末尾3行の削除
Write-Host "`n【テスト6】ファイル末尾3行の削除" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
Line 4: delete
Line 5: delete
Line 6: delete
"@
Set-Content -Path "edge-update6.txt" -Value $content
Update-LinesInFile -Path "edge-update6.txt" -LineRange 4,6
Remove-Item "edge-update6.txt"

# テスト7: ファイル全体の削除（全行削除）
Write-Host "`n【テスト7】ファイル全体の削除" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
"@
Set-Content -Path "edge-update7.txt" -Value $content
Update-LinesInFile -Path "edge-update7.txt" -LineRange 1,3
Remove-Item "edge-update7.txt"

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
