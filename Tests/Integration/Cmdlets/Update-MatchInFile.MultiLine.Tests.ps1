Describe "Update-MatchInFile Multi-Line Support" {

    Context "Multi-line literal text replacement" {
        It "Should replace multi-line text block with OldText" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1: Start
Line 2: First Block
Line 3: Middle Block
Line 4: End Block
Line 5: Final
"@ | Set-Content $testFile

                $oldText = @"
Line 2: First Block
Line 3: Middle Block
Line 4: End Block
"@
                $newText = "Line 2-4: Replaced"

                Update-MatchInFile -Path $testFile -OldText $oldText -Replacement $newText

                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 1: Start"
                $result | Should -Match "Line 2-4: Replaced"
                $result | Should -Match "Line 5: Final"
                $result | Should -Not -Match "First Block"
                $result | Should -Not -Match "Middle Block"
                $result | Should -Not -Match "End Block"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should handle multiple occurrences" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Block A
Block B
Separator
Block A
Block B
End
"@ | Set-Content $testFile

                $oldText = @"
Block A
Block B
"@
                $newText = "Single Line"

                Update-MatchInFile -Path $testFile -OldText $oldText -Replacement $newText

                $result = Get-Content $testFile -Raw
                ($result -split "Single Line").Count - 1 | Should -Be 2
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should detect and normalize different newline formats" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                # Create file with LF newlines
                "Line 1`nLine 2`nLine 3`nLine 4" | Set-Content $testFile -NoNewline

                # Search with CRLF in pattern (should be normalized to LF)
                $oldText = "Line 2`r`nLine 3"
                $newText = "Replaced"

                Update-MatchInFile -Path $testFile -OldText $oldText -Replacement $newText

                $result = Get-Content $testFile -Raw
                $result | Should -Match "Replaced"
                $result | Should -Not -Match "Line 2"
                $result | Should -Not -Match "Line 3"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Pattern with multiline content" {
        It "Pattern operates line-by-line even in files with many lines" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Start
Line A: Data
Line B: More
Line C: End
Final
"@ | Set-Content $testFile

                # Pattern replaces within each line independently
                Update-MatchInFile -Path $testFile -Pattern "Line [ABC]:" -Replacement "Row:"

                $result = Get-Content $testFile
                $result[1] | Should -Be "Row: Data"
                $result[2] | Should -Be "Row: More"
                $result[3] | Should -Be "Row: End"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "WhatIf preview" {
        It "Should show diff preview with -WhatIf" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1
Line 2
Line 3
Line 4
"@ | Set-Content $testFile

                $oldText = @"
Line 2
Line 3
"@
                $newText = "Replaced"

                $output = Update-MatchInFile -Path $testFile -OldText $oldText -Replacement $newText -WhatIf 2>&1 | Out-String

                $output | Should -Match "What if:"
                $output | Should -Match "1 replacement"
                
                # File should not be modified
                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 2"
                $result | Should -Match "Line 3"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Edge cases" {
        It "Should handle empty replacement" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1
Delete Me
Delete Me Too
Line 4
"@ | Set-Content $testFile

                $oldText = @"
Delete Me
Delete Me Too
"@

                Update-MatchInFile -Path $testFile -OldText $oldText -Replacement ""

                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 1"
                $result | Should -Match "Line 4"
                $result | Should -Not -Match "Delete Me"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should handle no matches found" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1
Line 2
Line 3
"@ | Set-Content $testFile

                $oldText = @"
Not Found
Also Not Found
"@

                $output = Update-MatchInFile -Path $testFile -OldText $oldText -Replacement "X" 2>&1 | Out-String

                $output | Should -Match "0 replacement\(s\) made"
                
                # File should not be modified
                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 1"
                $result | Should -Match "Line 2"
                $result | Should -Match "Line 3"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Display line numbers when the line count changes" {
        BeforeEach {
            $script:testFile = [System.IO.Path]::GetTempFileName()
            Set-Content $script:testFile -Value @('line1', 'line2', 'line3', 'target', 'line5', 'line6', 'line7')
        }
        AfterEach {
            if (Test-Path $script:testFile) { Remove-Item $script:testFile -Force }
        }

        It "Numbers each line of a multi-line replacement and shifts later lines" {
            $output = Update-MatchInFile -Path $script:testFile -OldText 'target' -Replacement "new1`nnew2`nnew3" |
                ForEach-Object { $_ -replace "`e\[[\d;]*m", '' }

            $output | Should -Contain '  4: new1'
            $output | Should -Contain '  5: new2'
            $output | Should -Contain '  6: new3'
            $output | Should -Contain '  7- line5'
            $output | Should -Contain '  8- line6'
            (Get-Content $script:testFile)[7] | Should -Be 'line6'
        }

        It "Keeps the replacement's color on every line it spans" {
            $output = Update-MatchInFile -Path $script:testFile -OldText 'target' -Replacement "new1`nnew2"

            $output | Should -Contain "  4: `e[32mnew1`e[0m"
            $output | Should -Contain "  5: `e[32mnew2`e[0m"
        }

        It "Numbers -Pattern replacements the same way" {
            $output = Update-MatchInFile -Path $script:testFile -Pattern '^(t)arget$' -Replacement "`$1-a`n`$1-b" |
                ForEach-Object { $_ -replace "`e\[[\d;]*m", '' }

            $output | Should -Contain '  4: t-a'
            $output | Should -Contain '  5: t-b'
            $output | Should -Contain '  6- line5'
        }

        It "Numbers the inserted lines of a multi-line OldText replacement, not the removed ones" {
            $output = Update-MatchInFile -Path $script:testFile -OldText "line2`nline3" -Replacement "A`nB`nC" |
                ForEach-Object { $_ -replace "`e\[[\d;]*m", '' }

            $output | Should -Contain '   : line2'
            $output | Should -Contain '   : line3'
            $output | Should -Contain '  2: A'
            $output | Should -Contain '  4: C'
        }

        It "Does not show the line after a whole-line OldText as both removed and inserted" {
            Set-Content $script:testFile -Value @('a', 'b', 'c', 'd')
            $output = Update-MatchInFile -Path $script:testFile -OldText "b`n" -Replacement "X`nY`n" |
                ForEach-Object { $_ -replace "`e\[[\d;]*m", '' }

            $output | Should -Contain '   : b'
            $output | Should -Not -Contain '   : c'
            $output | Should -Contain '  2: X'
            $output | Should -Contain '  3: Y'
            $output | Should -Contain '  4- c'
            $output | Should -Not -Contain '  4: c'
            (Get-Content $script:testFile) | Should -Be @('a', 'X', 'Y', 'c', 'd')
        }

        It "Shows no phantom removed line when whole lines are deleted at the end of the file" {
            Set-Content $script:testFile -Value @('a', 'b')
            $output = Update-MatchInFile -Path $script:testFile -OldText "b`n" -Replacement '' |
                ForEach-Object { $_ -replace "`e\[[\d;]*m", '' }

            $output | Should -Contain '   : b'
            ($output | Where-Object { $_ -eq '   : ' }).Count | Should -Be 0
            $output | Should -Not -Contain '  2: '
            (Get-Content $script:testFile) | Should -Be @('a')
        }

        It "Shows every occurrence of a multi-line OldText, each with its context" {
            Set-Content $script:testFile -Value @('h1', 'h2', 'A', 'B', 'm1', 'm2', 'm3', 'm4', 'm5', 'A', 'B', 't1')
            $output = Update-MatchInFile -Path $script:testFile -OldText "A`nB" -Replacement "X`nY`nZ" |
                ForEach-Object { $_ -replace "`e\[[\d;]*m", '' }

            # First occurrence: lines 3-4 become 3-5, later lines shift by one
            $output | Should -Contain '  1- h1'
            $output | Should -Contain '  2- h2'
            $output | Should -Contain '  5: Z'
            $output | Should -Contain '  6- m1'
            $output | Should -Contain '  7- m2'
            # Second occurrence: lines 10-11 become 11-13
            $output | Should -Contain '  9- m4'
            $output | Should -Contain ' 10- m5'
            $output | Should -Contain ' 11: X'
            $output | Should -Contain ' 13: Z'
            $output | Should -Contain ' 14- t1'
            ($output | Where-Object { $_ -eq '   : A' }).Count | Should -Be 2
            # The untouched middle (m3) is not shown; a blank line marks the gap
            $output | Should -Not -Contain '  8- m3'
            $output | Should -Contain ''
        }

        It "Shows whole lines when the multi-line OldText starts and ends mid-line" {
            Set-Content $script:testFile -Value @('one', 'keep A', 'B keep', 'two')
            $output = Update-MatchInFile -Path $script:testFile -OldText "A`nB" -Replacement 'AB' |
                ForEach-Object { $_ -replace "`e\[[\d;]*m", '' }

            $output | Should -Contain '   : keep A'
            $output | Should -Contain '   : B keep'
            $output | Should -Contain '  2: keep AB keep'
            $output | Should -Contain '  3- two'
        }
    }
}