# Edge case test - Update-LinesInFile
Write-Host "=== Update-LinesInFile edge case test ===" -ForegroundColor Cyan

# Test 1: Update the first line of the file
Write-Host "`n[Test 1] Update the first line of the file" -ForegroundColor Yellow
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

# Test 2: Update the last line of the file
Write-Host "`n[Test 2] Update the last line of the file" -ForegroundColor Yellow
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

# Test 3: Update a 1-line file
Write-Host "`n[Test 3] Update a 1-line file" -ForegroundColor Yellow
$content = @"
Only one line
"@
Set-Content -Path "edge-update3.txt" -Value $content
Update-LinesInFile -Path "edge-update3.txt" -LineRange 1,1 -Content "UPDATED LINE"
Remove-Item "edge-update3.txt"

# Test 4: Delete the first line of a 2-line file
Write-Host "`n[Test 4] Delete the first line of a 2-line file" -ForegroundColor Yellow
$content = @"
Line 1: delete me
Line 2
"@
Set-Content -Path "edge-update4.txt" -Value $content
Update-LinesInFile -Path "edge-update4.txt" -LineRange 1,1
Remove-Item "edge-update4.txt"

# Test 5: Delete the first 3 lines of the file
Write-Host "`n[Test 5] Delete the first 3 lines of the file" -ForegroundColor Yellow
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

# Test 6: Delete the last 3 lines of the file
Write-Host "`n[Test 6] Delete the last 3 lines of the file" -ForegroundColor Yellow
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

# Test 7: Delete the entire file (delete all lines)
Write-Host "`n[Test 7] Delete the entire file" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
Line 3
"@
Set-Content -Path "edge-update7.txt" -Value $content
Update-LinesInFile -Path "edge-update7.txt" -LineRange 1,3
Remove-Item "edge-update7.txt"

Write-Host "`n=== Tests complete ===" -ForegroundColor Green
