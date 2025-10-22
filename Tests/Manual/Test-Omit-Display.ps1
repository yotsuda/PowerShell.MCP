# Add-LinesToFile / Update-LinesInFile の省略表示機能テスト
Write-Host "=== 省略表示機能テスト ===" -ForegroundColor Cyan

$baseContent = @"
Line 1
Line 2
Line 3
Line 4
Line 5
Line 6
Line 7
Line 8
Line 9
Line 10
"@

# ===== Add-LinesToFile テスト =====
Write-Host "`n### Add-LinesToFile ###" -ForegroundColor Magenta

Write-Host "`n【テスト1】1行追加 - 全表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-add1.txt" -Value $baseContent
Add-LinesToFile -Path "test-omit-add1.txt" -LineNumber 5 -Content "SINGLE LINE"
Remove-Item "test-omit-add1.txt"

Write-Host "`n【テスト2】2行追加 - 全表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-add2.txt" -Value $baseContent
Add-LinesToFile -Path "test-omit-add2.txt" -LineNumber 5 -Content @("LINE 1", "LINE 2")
Remove-Item "test-omit-add2.txt"

Write-Host "`n【テスト3】3行追加 - 全表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-add3.txt" -Value $baseContent
Add-LinesToFile -Path "test-omit-add3.txt" -LineNumber 5 -Content @("FIRST LINE", "MIDDLE LINE", "LAST LINE")
Remove-Item "test-omit-add3.txt"

Write-Host "`n【テスト4】5行追加 - 先頭と末尾のみ表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-add5.txt" -Value $baseContent
Add-LinesToFile -Path "test-omit-add5.txt" -LineNumber 5 -Content @("LINE 1", "LINE 2", "LINE 3", "LINE 4", "LINE 5")
Remove-Item "test-omit-add5.txt"

Write-Host "`n【テスト5】10行追加 - 先頭と末尾のみ表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-add10.txt" -Value $baseContent
Add-LinesToFile -Path "test-omit-add10.txt" -LineNumber 5 -Content @("L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8", "L9", "L10")
Remove-Item "test-omit-add10.txt"

# ===== Update-LinesInFile テスト =====
Write-Host "`n### Update-LinesInFile ###" -ForegroundColor Magenta

Write-Host "`n【テスト6】1行更新 - 全表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-upd1.txt" -Value $baseContent
Update-LinesInFile -Path "test-omit-upd1.txt" -LineRange 5,5 -Content "UPDATED LINE"
Remove-Item "test-omit-upd1.txt"

Write-Host "`n【テスト7】2行更新 - 全表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-upd2.txt" -Value $baseContent
Update-LinesInFile -Path "test-omit-upd2.txt" -LineRange 5,6 -Content @("UPDATED 1", "UPDATED 2")
Remove-Item "test-omit-upd2.txt"

Write-Host "`n【テスト8】3行更新 - 先頭と末尾のみ表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-upd3.txt" -Value $baseContent
Update-LinesInFile -Path "test-omit-upd3.txt" -LineRange 5,7 -Content @("FIRST", "MIDDLE", "LAST")
Remove-Item "test-omit-upd3.txt"

Write-Host "`n【テスト9】5行更新 - 先頭と末尾のみ表示" -ForegroundColor Yellow
Set-Content -Path "test-omit-upd5.txt" -Value $baseContent
Update-LinesInFile -Path "test-omit-upd5.txt" -LineRange 4,8 -Content @("U1", "U2", "U3", "U4", "U5")
Remove-Item "test-omit-upd5.txt"

Write-Host "`n【テスト10】範囲拡大: 2行→10行更新" -ForegroundColor Yellow
Set-Content -Path "test-omit-upd-expand.txt" -Value $baseContent
Update-LinesInFile -Path "test-omit-upd-expand.txt" -LineRange 5,6 -Content @("N1", "N2", "N3", "N4", "N5", "N6", "N7", "N8", "N9", "N10")
Remove-Item "test-omit-upd-expand.txt"

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
