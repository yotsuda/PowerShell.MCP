Describe "Show-TextFiles - Context Display and ANSI Highlighting Tests" {
    BeforeAll {
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterAll {
        Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
    }

    Context "Context display (2 lines before/after)" {
        It "displays 2 lines before and after the matched line" {
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
                "Target Line 5"
                "Line 6"
                "Line 7"
                "Line 8"
                "Line 9"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"
            
            # Header line + 2 lines before + matched line + 2 lines after = 6 lines
            $result.Count | Should -Be 6
            # Skip the header line and verify
            $contentLines = $result | Select-Object -Skip 1
            $contentLines[0] | Should -Match "Line 3"
            $contentLines[2] | Should -Match ":\s+.*5.*"  # matched line has the : marker
            $contentLines[4] | Should -Match "Line 7"
        }

        It "for a match at the start of the file, context begins at the start" {
            $content = @(
                "Target Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"
            
            # Skip the header line and verify
            $contentLines = $result | Select-Object -Skip 1
            # Since it is the first line, it starts from line 1 (no preceding context)
            $contentLines[0] | Should -Match "^\s*1:"
            $contentLines[-1] | Should -Match "Line 3"
        }

        It "for a match at the end of the file, context ends at the end" {
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3"
                "Target Line 4"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"
            
            # Skip the header line and verify
            $contentLines = $result | Select-Object -Skip 1
            # Since it is the last line, it displays through the end (no following context)
            $contentLines[0] | Should -Match "Line 2"
            $contentLines[-1] | Should -Match "^\s*4:"
        }

        It "merges adjacent ranges when there are multiple matches" {
            $content = @(
                "Line 1"
                "Target Line 2"
                "Line 3"
                "Target Line 4"  # only 2 lines away from the previous match
                "Line 5"
                "Line 6"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"

            # The two matches are close, so the ranges merge and display contiguously
            # No blank line is inserted (other than the header line)
            $contentLines = $result | Select-Object -Skip 1
            $contentLines | Where-Object { $_ -eq "" } | Should -BeNullOrEmpty
        }

        It "separates ranges when multiple matches are far apart" {
            $content = @(
                "Target Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
                "Line 5"
                "Line 6"
                "Line 7"
                "Line 8"
                "Target Line 9"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"
            
            # The two matches are far apart, so they are separated by a blank line
            $contentLines = $result | Select-Object -Skip 1
            $emptyLines = $contentLines | Where-Object { $_ -eq "" }
            $emptyLines.Count | Should -BeGreaterThan 0
        }
    }

    Context "Highlighting via ANSI escape sequences" {
        It "applies ANSI reverse display to the Contains match portion" {
            Set-Content -Path $script:testFile -Value "This is a Target word" -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"
            
            # Contains the ANSI escape sequences \e[7m (reverse ON) and \e[0m (reset)
            $matchLine = $result | Where-Object { $_ -match "^\s*\d+:" }
            $matchLine | Should -Match "$([char]27)\[33m"  # yellow highlight
            $matchLine | Should -Match "$([char]27)\[0m"  # reset
        }

        It "applies ANSI reverse display to the Pattern match portion" {
            Set-Content -Path $script:testFile -Value "Error: Something went wrong" -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Pattern "Error:"
            
            $matchLine = $result | Where-Object { $_ -match "^\s*\d+:" }
            $matchLine | Should -Match "$([char]27)\[33m"
            $matchLine | Should -Match "$([char]27)\[0m"
        }

        It "does not apply ANSI to context lines" {
            $content = @(
                "Line 1"
                "Target Line 2"
                "Line 3"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -Contains "Target"
            
            # Context lines (no * marker, not the header) do not contain ANSI escapes
            $contextLines = $result | Where-Object {
                $_ -match "^\s*\d+-" -and $_ -ne "" -and $_ -notmatch "^==>"
            }
            foreach ($line in $contextLines) {
                $line | Should -Not -Match "$([char]27)\["
            }
        }
    }

    Context "Combination of LineRange and search" {
        It "search runs only within the LineRange" {
            $content = @(
                "Target Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
                "Target Line 5"
                "Line 6"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            # Search only within the range of lines 3-6
            $result = Show-TextFiles -Path $script:testFile -LineRange 3,6 -Contains "Target"

            # Only Line 5 matches (Line 1 is out of range)
            $result | Where-Object { $_ -match "^\s*\d+:" } | Should -HaveCount 1
            $result | Where-Object { $_ -match "Line 5" } | Should -Not -BeNullOrEmpty
        }
    }
}

Describe "LineRange - Negative Values Support (End of File)" {
    BeforeAll {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:content = @(
            "Line 1"
            "Line 2"
            "Line 3"
            "Line 4"
            "Line 5"
            "Line 6"
            "Line 7"
            "Line 8"
            "Line 9"
            "Line 10"
        )
    }

    AfterAll {
        Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
    }

    Context "Negative LineRange with Show-TextFiles" {
        It "-LineRange 5,-1 displays from line 5 to the end" {
            Set-Content -Path $script:testFile -Value $script:content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -LineRange 5,-1

            # Header line + 6 lines (Line 5-10)
            $result.Count | Should -Be 7
            $contentLines = $result | Select-Object -Skip 1
            $contentLines[0] | Should -Match "Line 5"
            $contentLines[-1] | Should -Match "Line 10"
        }

        It "-LineRange 8,0 displays from line 8 to the end (0 also means the end)" {
            Set-Content -Path $script:testFile -Value $script:content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -LineRange 8,0

            # Header line + 3 lines (Line 8-10)
            $result.Count | Should -Be 4
            $contentLines = $result | Select-Object -Skip 1
            $contentLines[0] | Should -Match "Line 8"
            $contentLines[-1] | Should -Match "Line 10"
        }

        It "-LineRange 1,-1 displays the entire file" {
            Set-Content -Path $script:testFile -Value $script:content -Encoding UTF8
            $result = Show-TextFiles -Path $script:testFile -LineRange 1,-1

            # Header line + 10 lines
            $result.Count | Should -Be 11
        }
    }

    Context "Negative LineRange with Remove-LinesFromFile" {
        It "-LineRange 5,-1 deletes from line 5 to the end" {
            Set-Content -Path $script:testFile -Value $script:content -Encoding UTF8
            Remove-LinesFromFile -Path $script:testFile -LineRange 5,-1
            $result = Get-Content $script:testFile

            $result.Count | Should -Be 4  # only Line 1-4 remain
            $result[-1] | Should -Be "Line 4"
        }

        It "-LineRange 8,0 deletes from line 8 to the end" {
            Set-Content -Path $script:testFile -Value $script:content -Encoding UTF8
            Remove-LinesFromFile -Path $script:testFile -LineRange 8,0
            $result = Get-Content $script:testFile

            $result.Count | Should -Be 7  # only Line 1-7 remain
            $result[-1] | Should -Be "Line 7"
        }
    }

    Context "Negative LineRange with Update-LinesInFile" {
        It "-LineRange 5,-1 replaces from line 5 to the end" {
            Set-Content -Path $script:testFile -Value $script:content -Encoding UTF8
            Update-LinesInFile -Path $script:testFile -LineRange 5,-1 -Content "Replaced"
            $result = Get-Content $script:testFile

            $result.Count | Should -Be 5  # Line 1-4 + Replaced
            $result[0..3] | Should -Be @("Line 1", "Line 2", "Line 3", "Line 4")
            $result[4] | Should -Be "Replaced"
        }
    }


    Context "Negative LineRange with Update-MatchInFile" {
        It "-LineRange 5,-1 replaces within the range from line 5 to the end" {
            Set-Content -Path $script:testFile -Value $script:content -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -LineRange 5,-1 -OldText "Line" -Replacement "Row"
            $result = Get-Content $script:testFile

            # Lines 1-4 are unchanged
            $result[0..3] | Should -Be @("Line 1", "Line 2", "Line 3", "Line 4")
            # Lines 5-10 are replaced
            $result[4..9] | ForEach-Object { $_ | Should -Match "Row" }
        }
    }
}