Describe "Show-TextFiles -Contains duplicate detection" {
    BeforeAll {
        $script:testFile = New-TemporaryFile
        "line1
line2
line3
line4
line5" | Set-Content -Path $script:testFile.FullName
    }
    
    AfterAll {
        if (Test-Path $script:testFile.FullName) {
            Remove-Item $script:testFile.FullName -Force
        }
    }
    
    It "no duplicate output for consecutive matched lines" {
        $result = Show-TextFiles -Path $script:testFile.FullName -Pattern "line"
        # Header + 5 matched lines = 6 lines (context lines are not duplicated)
        $result.Count | Should -Be 6

        # Confirm each line is output only once
        ($result | Where-Object { $_ -match "^\s+1:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+2:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+3:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+4:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+5:" }).Count | Should -Be 1
    }
}
