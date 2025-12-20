# エッジケーステスト - Update-MatchInFile & Show-TextFile
Write-Host "=== Update-MatchInFile & Show-TextFile エッジケーステスト ===" -ForegroundColor Cyan

# テスト1: ファイル先頭でのマッチ
Write-Host "`n【テスト1】ファイル先頭でのマッチ" -ForegroundColor Yellow
$content = @"
Line 1: match
Line 2
Line 3
Line 4
Line 5
"@
Set-Content -Path "edge-match1.txt" -Value $content
Update-MatchInFile -Path "edge-match1.txt" -OldText "match" -Replacement "REPLACED"
Remove-Item "edge-match1.txt"

# テスト2: ファイル末尾でのマッチ
Write-Host "`n【テスト2】ファイル末尾でのマッチ" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
Line 4
Line 5: match
"@
Set-Content -Path "edge-match2.txt" -Value $content
Update-MatchInFile -Path "edge-match2.txt" -OldText "match" -Replacement "REPLACED"
Remove-Item "edge-match2.txt"

# テスト3: ファイル先頭と末尾の両方でマッチ
Write-Host "`n【テスト3】ファイル先頭と末尾の両方でマッチ" -ForegroundColor Yellow
$content = @"
Line 1: match
Line 2
Line 3
Line 4
Line 5: match
"@
Set-Content -Path "edge-match3.txt" -Value $content
Update-MatchInFile -Path "edge-match3.txt" -OldText "match" -Replacement "REPLACED"
Remove-Item "edge-match3.txt"

# テスト4: 3行ファイルの中央行マッチ
Write-Host "`n【テスト4】3行ファイルの中央行マッチ" -ForegroundColor Yellow
$content = @"
Line 1
Line 2: match
Line 3
"@
Set-Content -Path "edge-match4.txt" -Value $content
Update-MatchInFile -Path "edge-match4.txt" -OldText "match" -Replacement "REPLACED"
Remove-Item "edge-match4.txt"

# テスト5: Show-TextFile - ファイル先頭でのマッチ
Write-Host "`n【テスト5】Show-TextFile - ファイル先頭でのマッチ" -ForegroundColor Yellow
$content = @"
Line 1: match
Line 2
Line 3
Line 4
Line 5
"@
Set-Content -Path "edge-show1.txt" -Value $content
Show-TextFile -Path "edge-show1.txt" -Contains "match"
Remove-Item "edge-show1.txt"

# テスト6: Show-TextFile - ファイル末尾でのマッチ
Write-Host "`n【テスト6】Show-TextFile - ファイル末尾でのマッチ" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
Line 4
Line 5: match
"@
Set-Content -Path "edge-show2.txt" -Value $content
Show-TextFile -Path "edge-show2.txt" -Contains "match"
Remove-Item "edge-show2.txt"

Write-Host "`n=== テスト完了 ===" -ForegroundColor Green
