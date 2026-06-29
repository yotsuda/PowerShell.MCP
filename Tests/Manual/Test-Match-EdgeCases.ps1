# Edge case test - Update-MatchInFile & Show-TextFiles
Write-Host "=== Update-MatchInFile & Show-TextFiles edge case test ===" -ForegroundColor Cyan

# Test 1: Match at the beginning of the file
Write-Host "`n[Test 1] Match at the beginning of the file" -ForegroundColor Yellow
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

# Test 2: Match at the end of the file
Write-Host "`n[Test 2] Match at the end of the file" -ForegroundColor Yellow
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

# Test 3: Match at both the beginning and end of the file
Write-Host "`n[Test 3] Match at both the beginning and end of the file" -ForegroundColor Yellow
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

# Test 4: Match on the middle line of a 3-line file
Write-Host "`n[Test 4] Match on the middle line of a 3-line file" -ForegroundColor Yellow
$content = @"
Line 1
Line 2: match
Line 3
"@
Set-Content -Path "edge-match4.txt" -Value $content
Update-MatchInFile -Path "edge-match4.txt" -OldText "match" -Replacement "REPLACED"
Remove-Item "edge-match4.txt"

# Test 5: Show-TextFiles - match at the beginning of the file
Write-Host "`n[Test 5] Show-TextFiles - match at the beginning of the file" -ForegroundColor Yellow
$content = @"
Line 1: match
Line 2
Line 3
Line 4
Line 5
"@
Set-Content -Path "edge-show1.txt" -Value $content
Show-TextFiles -Path "edge-show1.txt" -Contains "match"
Remove-Item "edge-show1.txt"

# Test 6: Show-TextFiles - match at the end of the file
Write-Host "`n[Test 6] Show-TextFiles - match at the end of the file" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
Line 4
Line 5: match
"@
Set-Content -Path "edge-show2.txt" -Value $content
Show-TextFiles -Path "edge-show2.txt" -Contains "match"
Remove-Item "edge-show2.txt"

Write-Host "`n=== Tests complete ===" -ForegroundColor Green
