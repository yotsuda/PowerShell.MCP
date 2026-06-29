# Manual test for the Show-TextFiles gap merge feature
# Run this script and visually inspect the output

Write-Host "=== Show-TextFiles gap merge feature test (2 lines before and after) ===" -ForegroundColor Cyan

# Test 1: 0-line gap (should be merged)
Write-Host "`n[Test 1] 0-line gap - merged" -ForegroundColor Yellow
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
Show-TextFiles -Path $testFile1 -Contains "MATCH"
Remove-Item $testFile1

# Test 2: 1-line gap (should be merged)
Write-Host "`n[Test 2] 1-line gap - merged (line 7 is displayed)" -ForegroundColor Yellow
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
Show-TextFiles -Path $testFile2 -Contains "MATCH"
Remove-Item $testFile2

# Test 3: 2-line gap (should be merged)
Write-Host "`n[Test 3] 2-line gap - merged (lines 7-8 are displayed as gap lines)" -ForegroundColor Yellow
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
Show-TextFiles -Path $testFile3 -Contains "MATCH"
Remove-Item $testFile3

# Test 4: 3-line gap (should be split)
Write-Host "`n[Test 4] 3-line gap - split (lines 7-9 are not displayed)" -ForegroundColor Yellow
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
Show-TextFiles -Path $testFile4 -Contains "MATCH"
Remove-Item $testFile4

# Test 5: 4-line gap (should be split)
Write-Host "`n[Test 5] 4-line gap - split (lines 7-10 are hidden)" -ForegroundColor Yellow
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
Show-TextFiles -Path $testFile5 -Contains "MATCH"
Remove-Item $testFile5

# Test 6: grep standard format check
Write-Host "`n[Test 6] grep standard format - match lines use :, context lines use -" -ForegroundColor Yellow
$content6 = @"
Context line 1
Context line 2
MATCH line
Context line 4
Context line 5
"@
$testFile6 = "test-format.txt"
Set-Content -Path $testFile6 -Value $content6
Write-Host "Expected output:" -ForegroundColor Gray
Write-Host "  1- Context line 1" -ForegroundColor Gray
Write-Host "  2- Context line 2" -ForegroundColor Gray
Write-Host "  3: MATCH line" -ForegroundColor Gray
Write-Host "  4- Context line 4" -ForegroundColor Gray
Write-Host "  5- Context line 5" -ForegroundColor Gray
Write-Host "`nActual output:" -ForegroundColor Gray
Show-TextFiles -Path $testFile6 -Contains "MATCH"
Remove-Item $testFile6

Write-Host "`n=== Tests complete ===" -ForegroundColor Green
