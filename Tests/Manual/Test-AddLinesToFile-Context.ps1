# Test for the Add-LinesToFile context display feature
Write-Host "=== Add-LinesToFile context display test ===" -ForegroundColor Cyan

# Test 1: Insert a single line
Write-Host "`n[Test 1] Insert a single line - insert at line number 5" -ForegroundColor Yellow
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

# Test 2: Insert multiple lines
Write-Host "`n[Test 2] Insert multiple lines - insert 3 lines at line number 6" -ForegroundColor Yellow
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

# Test 3: Append to the end
Write-Host "`n[Test 3] Append to the end - LineNumber omitted" -ForegroundColor Yellow
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

# Test 4: When multiple insertion points are close together (gap merge check)
Write-Host "`n[Test 4] Verify merge when gap is 2 lines or fewer" -ForegroundColor Yellow
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
# Insert at line 5 (display range: 3-7)
Add-LinesToFile -Path "test-add4.txt" -LineNumber 5 -Content "FIRST INSERT"
# Insert at line 10 (display range: 8-12; difference from previous 7 is 1 -> should be merged)
Add-LinesToFile -Path "test-add4.txt" -LineNumber 11 -Content "SECOND INSERT"
Remove-Item "test-add4.txt"

Write-Host "`n=== Tests complete ===" -ForegroundColor Green
