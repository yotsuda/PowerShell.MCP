# Update-MatchInFile コンテキスト表示機能のテスト
Write-Host "=== Update-MatchInFile コンテキスト表示テスト ===" -ForegroundColor Cyan

# テスト1: Contains モード
Write-Host "`n【テスト1】Contains モード - 3箇所の置換" -ForegroundColor Yellow
$content1 = @"
Line 1: This is normal
Line 2: This has old value here
Line 3: Normal line
Line 4: Another old value
Line 5: Normal line
Line 6: Normal line
Line 7: Normal line
Line 8: Normal line
Line 9: Normal line
Line 10: Yet another old value
Line 11: Normal line
Line 12: Normal line
"@
Set-Content -Path "test-replace1.txt" -Value $content1
Update-MatchInFile -Path "test-replace1.txt" -Contains "old value" -Replacement "new value"
Remove-Item "test-replace1.txt"

# テスト2: Pattern モード（正規表現）
Write-Host "`n【テスト2】Pattern モード - 正規表現置換" -ForegroundColor Yellow
$content2 = @"
function foo() {
  var x = 10;
  var y = 20;
  let z = 30;
}
function bar() {
  var a = 5;
}
"@
Set-Content -Path "test-replace2.txt" -Value $content2
Update-MatchInFile -Path "test-replace2.txt" -Pattern "\bvar\b" -Replacement "let"
Remove-Item "test-replace2.txt"

# テスト3: ギャップマージ確認（ギャップ1行）
Write-Host "`n【テスト3】ギャップ1行 - マージされる" -ForegroundColor Yellow
$content3 = @"
Line 1
Line 2
Line 3
Line 4: match
Line 5
Line 6
Line 7
Line 8 (この行が表示されるべき)
Line 9
Line 10
Line 11
Line 12: match
Line 13
Line 14
"@
Set-Content -Path "test-replace3.txt" -Value $content3
Update-MatchInFile -Path "test-replace3.txt" -Contains "match" -Replacement "REPLACED"
Remove-Item "test-replace3.txt"

# テスト4: ギャップ4行（分離）
Write-Host "`n【テスト4】ギャップ4行 - 分離される" -ForegroundColor Yellow
$content4 = @"
Line 1
Line 2
Line 3
Line 4: match
Line 5
Line 6
Line 7
Line 8 (この行は表示されないべき)
Line 9 (この行は表示されないべき)
Line 10 (この行は表示されないべき)
Line 11 (この行は表示されないべき)
Line 12
Line 13
Line 14
Line 15: match
Line 16
Line 17
"@
Set-Content -Path "test-replace4.txt" -Value $content4
Update-MatchInFile -Path "test-replace4.txt" -Contains "match" -Replacement "REPLACED"
Remove-Item "test-replace4.txt"

# テスト5: LineRange指定
Write-Host "`n【テスト5】LineRange指定 - 5-10行目のみ置換" -ForegroundColor Yellow
$content5 = @"
Line 1: old
Line 2: old
Line 3: old
Line 4: old
Line 5: old
Line 6: old
Line 7: old
Line 8: old
Line 9: old
Line 10: old
Line 11: old
Line 12: old
"@
Set-Content -Path "test-replace5.txt" -Value $content5
Update-MatchInFile -Path "test-replace5.txt" -LineRange 5,10 -Contains "old" -Replacement "new"
Remove-Item "test-replace5.txt"

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
