# エッジケーステスト - Add-LinesToFile
Write-Host "=== Add-LinesToFile エッジケーステスト ===" -ForegroundColor Cyan

# テスト1: ファイル先頭への挿入
Write-Host "`n【テスト1】ファイル先頭への挿入" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
Line 4
Line 5
"@
Set-Content -Path "edge-add1.txt" -Value $content
Add-LinesToFile -Path "edge-add1.txt" -LineNumber 1 -Content "NEW FIRST LINE"
Remove-Item "edge-add1.txt"

# テスト2: 2行ファイルへの挿入
Write-Host "`n【テスト2】2行ファイルへの挿入（コンテキスト不足）" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
"@
Set-Content -Path "edge-add2.txt" -Value $content
Add-LinesToFile -Path "edge-add2.txt" -LineNumber 2 -Content "INSERTED"
Remove-Item "edge-add2.txt"

# テスト3: 末尾から2行目への挿入
Write-Host "`n【テスト3】末尾から2行目への挿入" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
Line 4
Line 5
"@
Set-Content -Path "edge-add3.txt" -Value $content
Add-LinesToFile -Path "edge-add3.txt" -LineNumber 4 -Content "INSERTED NEAR END"
Remove-Item "edge-add3.txt"

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
