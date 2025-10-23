Import-Module "C:\MyProj\PowerShell.MCP\PowerShell.MCP\bin\Release\net9.0\PowerShell.MCP.dll" -Force

# 簡単な動作確認
$testFile = [System.IO.Path]::GetTempFileName()
Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5") -Encoding UTF8

Write-Host "`n=== Update-LinesInFile の動作確認 ===" -ForegroundColor Cyan
Update-LinesInFile -Path $testFile -LineRange 2,3 -Content @("Updated 2", "Updated 3")

Write-Host "`n=== Remove-LinesFromFile の動作確認 ===" -ForegroundColor Cyan
Remove-LinesFromFile -Path $testFile -LineRange 2,2

Write-Host "`n=== Update-MatchInFile の動作確認 ===" -ForegroundColor Cyan
Update-MatchInFile -Path $testFile -Contains "Updated" -Replacement "Modified"

# クリーンアップ
Remove-Item $testFile -Force
