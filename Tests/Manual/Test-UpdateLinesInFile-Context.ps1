# Test for the Update-LinesInFile context display feature
Write-Host "=== Update-LinesInFile context display test ===" -ForegroundColor Cyan

# Test 1: Update a single line
Write-Host "`n[Test 1] Update a single line - replace line 5" -ForegroundColor Yellow
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

# Test 2: Update multiple lines (same number of lines)
Write-Host "`n[Test 2] Update multiple lines - replace lines 5-7 with 3 lines" -ForegroundColor Yellow
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

# Test 3: Shrink the range (3 lines -> 1 line)
Write-Host "`n[Test 3] Shrink the range - replace lines 5-7 with a single line" -ForegroundColor Yellow
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

# Test 4: Expand the range (1 line -> 3 lines)
Write-Host "`n[Test 4] Expand the range - replace line 5 with 3 lines" -ForegroundColor Yellow
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

# Test 5: Delete lines (Content omitted)
Write-Host "`n[Test 5] Delete lines - delete lines 5-7" -ForegroundColor Yellow
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

Write-Host "`n=== Tests complete ===" -ForegroundColor Green
