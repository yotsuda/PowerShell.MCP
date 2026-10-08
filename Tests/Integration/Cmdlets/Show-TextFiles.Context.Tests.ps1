# Show-TextFiles -Context: how many lines are shown around each match.

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Show-TextFiles -Context" {
    BeforeAll {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $lines = 1..12 | ForEach-Object { if ($_ -in 4, 9) { "line $_ MATCH" } else { "line $_" } }
        Set-Content -Path $script:testFile -Value $lines -Encoding UTF8
        # Strip ANSI so the assertions see plain text.
        function script:Plain([string[]]$Output) { $Output -replace "`e\[[\d;]*m", '' }
    }

    AfterAll {
        if (Test-Path $script:testFile) { Remove-Item $script:testFile -Force }
    }

    It "shows 2 lines of context by default" {
        $out = Plain (Show-TextFiles $script:testFile -Pattern 'MATCH')
        $out | Should -Contain '  2- line 2'
        $out | Should -Contain '  4: line 4 MATCH'
        $out | Should -Contain '  6- line 6'
        $out | Should -Contain ' 11- line 11'
        $out | Should -Not -Contain '  1- line 1'
    }

    It "-Context 0 lists only the matching lines" {
        $out = Plain (Show-TextFiles $script:testFile -Pattern 'MATCH' -Context 0)
        $out | Should -Contain '  4: line 4 MATCH'
        $out | Should -Contain '  9: line 9 MATCH'
        ($out | Where-Object { $_ -match '^\s*\d+- ' }) | Should -BeNullOrEmpty
        # grep-style: no blank separator lines either, the numbers show the gap.
        ($out | Where-Object { $_ -eq '' }).Count | Should -Be 0
    }

    It "-Context 1 shows one line each side" {
        $out = Plain (Show-TextFiles $script:testFile -Pattern 'MATCH' -Context 1)
        $out | Should -Contain '  3- line 3'
        $out | Should -Contain '  5- line 5'
        $out | Should -Not -Contain '  2- line 2'
        $out | Should -Not -Contain '  6- line 6'
    }

    It "applies to -Contains too, including the multi-line form" {
        $out = Plain (Show-TextFiles $script:testFile -Contains 'MATCH' -Context 0)
        ($out | Where-Object { $_ -match '^\s*\d+- ' }) | Should -BeNullOrEmpty
        $multi = Plain (Show-TextFiles $script:testFile -Contains "line 4 MATCH`nline 5" -Context 0)
        $multi | Should -Contain '  4: line 4 MATCH'
        $multi | Should -Contain '  5: line 5'
        ($multi | Where-Object { $_ -match '^\s*\d+- ' }) | Should -BeNullOrEmpty
    }

    It "rejects a negative value" {
        { Show-TextFiles $script:testFile -Pattern 'MATCH' -Context -1 } | Should -Throw
    }
}
