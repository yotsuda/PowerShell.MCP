# Add-LinesToFile コンテキスト表示機能のテスト
Write-Host "=== Add-LinesToFile コンテキスト表示テスト ===" -ForegroundColor Cyan

# テスト1: 1行挿入
Write-Host "`n【テスト1】1行挿入 - 行番号5に挿入" -ForegroundColor Yellow
$content1 = @"
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
Set-Content -Path "test-add1.txt" -Value $content1
Add-LinesToFile -Path "test-add1.txt" -LineNumber 5 -Content "NEW LINE INSERTED"
Remove-Item "test-add1.txt"

# テスト2: 複数行挿入
Write-Host "`n【テスト2】複数行挿入 - 行番号6に3行挿入" -ForegroundColor Yellow
$content2 = @"
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
Set-Content -Path "test-add2.txt" -Value $content2
Add-LinesToFile -Path "test-add2.txt" -LineNumber 6 -Content @("INSERT 1", "INSERT 2", "INSERT 3")
Remove-Item "test-add2.txt"

# テスト3: 末尾追加
Write-Host "`n【テスト3】末尾追加 - LineNumber 省略" -ForegroundColor Yellow
$content3 = @"
Line 1
Line 2
Line 3
Line 4
Line 5
"@
Set-Content -Path "test-add3.txt" -Value $content3
Add-LinesToFile -Path "test-add3.txt" -Content "APPENDED LINE"
Remove-Item "test-add3.txt"

# テスト4: 複数の挿入位置が近い場合（ギャップマージ確認）
Write-Host "`n【テスト4】ギャップ2行以下でマージ確認" -ForegroundColor Yellow
$content4 = @"
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
Line 11
Line 12
Line 13
Line 14
Line 15
"@
Set-Content -Path "test-add4.txt" -Value $content4
# 行5に挿入（表示範囲: 3-7）
Add-LinesToFile -Path "test-add4.txt" -LineNumber 5 -Content "FIRST INSERT"
# 行10に挿入（表示範囲: 8-12、前回の7との差が1→マージされるはず）
Add-LinesToFile -Path "test-add4.txt" -LineNumber 11 -Content "SECOND INSERT"
Remove-Item "test-add4.txt"

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
