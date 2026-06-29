Describe "Show-TextFiles - Additional Edge Cases" {
    BeforeAll {
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterAll {
        Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
    }

    Context "Context display in small files" {
        It "displays without context when a match occurs in a single-line file" {
            Set-Content -Path $script:testFile -Value "Target" -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"

            # Header + matched line only
            $result.Count | Should -Be 2
            $result[1] | Should -Match ":.*Target"
        }

        It "match on line 1 of a two-line file" {
            $content = @("Target Line 1", "Line 2")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"

            # Header + matched line + 1 line of following context
            $result.Count | Should -Be 3
        }

        It "match on line 2 of a two-line file" {
            $content = @("Line 1", "Target Line 2")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"

            # Header + 1 line of preceding context + matched line
            $result.Count | Should -Be 3
        }
    }

    Context "Multiple matches within the same line" {
        It "Contains: all matches within the same line are highlighted" {
            Set-Content -Path $script:testFile -Value "Test and Test and Test" -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Test"

            $matchLine = $result | Where-Object { $_ -match ":" }
            # All 3 occurrences of "Test" are highlighted
            $highlightCount = ([regex]::Matches($matchLine, "$([char]27)\[33m")).Count
            $highlightCount | Should -Be 3
        }

        It "Pattern: all matches within the same line are highlighted" {
            Set-Content -Path $script:testFile -Value "abc123def456ghi789" -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Pattern '\d+'

            $matchLine = $result | Where-Object { $_ -match ":" }
            # All 3 number occurrences are highlighted
            $highlightCount = ([regex]::Matches($matchLine, "$([char]27)\[33m")).Count
            $highlightCount | Should -Be 3
        }
    }

    Context "Boundary conditions of range merging" {
        It "merges ranges when matches are exactly 7 lines apart" {
            $content = 1..15 | ForEach-Object { "Line $_" }
            $content[0] = "Match Line 1"
            $content[7] = "Match Line 8"  # 7 lines apart
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Match"

            # Merged, so no blank line
            $contentLines = $result | Select-Object -Skip 1
            $contentLines | Where-Object { $_ -eq "" } | Should -BeNullOrEmpty
        }

        It "separates ranges when matches are 8 or more lines apart" {
            $content = 1..15 | ForEach-Object { "Line $_" }
            $content[0] = "Match Line 1"
            $content[8] = "Match Line 9"  # 8 lines apart
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Match"

            # Separated, so there is a blank line
            $contentLines = $result | Select-Object -Skip 1
            ($contentLines | Where-Object { $_ -eq "" }).Count | Should -BeGreaterThan 0
        }
    }

    Context "Variations of negative LineRange values" {
        It "5,-1 and 5,0 and 5,-99 all display from line 5 to the end" {
            $content = 1..10 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8

            $result1 = Show-TextFiles -Path $script:testFile -LineRange 5,-1
            $result2 = Show-TextFiles -Path $script:testFile -LineRange 5,0
            $result3 = Show-TextFiles -Path $script:testFile -LineRange 5,-99

            # All produce the same result (line 5 to the end = 6 lines + 1 header line = 7 lines)
            $result1.Count | Should -Be 7
            $result2.Count | Should -Be 7
            $result3.Count | Should -Be 7
        }

        It "0 also means the end, like negative values" {
            $content = 1..10 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8

            $result1 = Show-TextFiles -Path $script:testFile -LineRange 5,-1
            $result2 = Show-TextFiles -Path $script:testFile -LineRange 5,0

            $result1.Count | Should -Be $result2.Count
        }
    }

    Context "Validation errors" {
        It "produces a validation error when the first argument is negative" {
            Set-Content -Path $script:testFile -Value "Test" -Encoding UTF8
            { Show-TextFiles -Path $script:testFile -LineRange -1,5 } | Should -Throw
        }

        It "also produces a validation error when the first argument is 0" {
            Set-Content -Path $script:testFile -Value "Test" -Encoding UTF8
            { Show-TextFiles -Path $script:testFile -LineRange 0,5 } | Should -Throw
        }
    }

    Context "Special match cases" {
        It "displays correctly even when a blank line matches" {
            $content = @("Line 1", "", "Line 3")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Pattern '^$'

            # The blank line is displayed as a match
            $result | Where-Object { $_ -match "^\s+2:" } | Should -Not -BeNullOrEmpty
        }

        It "applies highlighting correctly even on a very long line" {
            $longLine = "a" * 1000 + "TARGET" + "b" * 1000
            Set-Content -Path $script:testFile -Value $longLine -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "TARGET"
            
            $matchLine = $result | Where-Object { $_ -match "^\s+\d+:" }
            $matchLine | Should -Match "$([char]27)\[33mTARGET$([char]27)\[0m"
        }
    }

    Context "New features with multiple files" {
        It "context display works with multiple files too" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            
            try {
                @("Line 1", "Target", "Line 3") | Set-Content $file1 -Encoding UTF8
                @("Line A", "Target", "Line C") | Set-Content $file2 -Encoding UTF8
                
                $result = Show-TextFiles -Path $file1,$file2 -Contains "Target"

                # The header and context are displayed for both files
                ($result | Where-Object { $_ -match "==>" }).Count | Should -Be 2
                ($result | Where-Object { $_ -match "^\s+\d+:" }).Count | Should -Be 2
            }
            finally {
                Remove-Item $file1,$file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }
    
    Context "Errors for patterns containing newlines" {
        It "errors when Pattern contains a newline" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                "Line 1`nLine 2" | Set-Content $testFile -Encoding UTF8
                
                { Show-TextFiles -Path $testFile -Pattern "Line`nLine" } | Should -Throw "*newline*"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }
        
        It "operates in multiline mode when Contains includes a newline" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1
Line 2
Line 3
"@ | Set-Content $testFile -Encoding UTF8

                # Multiline Contains is now supported (no error)
                $searchText = @"
Line 1
Line 2
"@
                $output = Show-TextFiles -Path $testFile -Contains $searchText | Out-String
                $output | Should -Match "Line 1"
                $output | Should -Match "Line 2"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
