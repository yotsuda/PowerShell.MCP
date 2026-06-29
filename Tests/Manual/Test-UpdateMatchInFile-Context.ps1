# Test for the Update-MatchInFile context display feature
Write-Host "=== Update-MatchInFile context display test ===" -ForegroundColor Cyan

# Test 1: Contains mode
Write-Host "`n[Test 1] Contains mode - replace 3 occurrences" -ForegroundColor Yellow
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
Update-MatchInFile -Path "test-replace1.txt" -OldText "old value" -Replacement "new value"
Remove-Item "test-replace1.txt"

# Test 2: Pattern mode (regular expression)
Write-Host "`n[Test 2] Pattern mode - regular expression replacement" -ForegroundColor Yellow
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

# Test 3: Gap merge check (2-line gap)
Write-Host "`n[Test 3] 2-line gap - merged" -ForegroundColor Yellow
$content3 = @"
Line 1
Line 2
Line 3
Line 4: match
Line 5
Line 6
Line 7 (gap)
Line 8 (gap)
Line 9
Line 10: match
Line 11
Line 12
"@
Set-Content -Path "test-replace3.txt" -Value $content3
Update-MatchInFile -Path "test-replace3.txt" -OldText "match" -Replacement "REPLACED"
Remove-Item "test-replace3.txt"

# Test 4: 3-line gap (merge)
Write-Host "`n[Test 4] 3-line gap - merged" -ForegroundColor Yellow
$content4 = @"
Line 1
Line 2
Line 3
Line 4: match
Line 5
Line 6
Line 7 (この行は表示されるべき)
Line 8 (この行は表示されるべき)
Line 9 (この行は表示されるべき)
Line 10
Line 11
Line 12: match
Line 13
Line 14
"@
Set-Content -Path "test-replace4.txt" -Value $content4
Update-MatchInFile -Path "test-replace4.txt" -OldText "match" -Replacement "REPLACED"
Remove-Item "test-replace4.txt"
# Test 5: LineRange specification
Write-Host "`n[Test 5] LineRange specification - replace only lines 5-10" -ForegroundColor Yellow
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
Update-MatchInFile -Path "test-replace5.txt" -LineRange 5,10 -OldText "old" -Replacement "new"
Remove-Item "test-replace5.txt"

Write-Host "`n=== Tests complete ===" -ForegroundColor Green
