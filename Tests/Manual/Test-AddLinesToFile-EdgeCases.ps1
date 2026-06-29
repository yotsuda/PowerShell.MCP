# Edge case test - Add-LinesToFile
Write-Host "=== Add-LinesToFile edge case test ===" -ForegroundColor Cyan

# Test 1: Insert at the beginning of the file
Write-Host "`n[Test 1] Insert at the beginning of the file" -ForegroundColor Yellow
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

# Test 2: Insert into a 2-line file
Write-Host "`n[Test 2] Insert into a 2-line file (insufficient context)" -ForegroundColor Yellow
$content = @"
Line 1
Line 2
"@
Set-Content -Path "edge-add2.txt" -Value $content
Add-LinesToFile -Path "edge-add2.txt" -LineNumber 2 -Content "INSERTED"
Remove-Item "edge-add2.txt"

# Test 3: Insert at the second-to-last line
Write-Host "`n[Test 3] Insert at the second-to-last line" -ForegroundColor Yellow
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

Write-Host "`n=== Tests complete ===" -ForegroundColor Green
