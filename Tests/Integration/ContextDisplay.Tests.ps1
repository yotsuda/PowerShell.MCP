# Context Display Feature Tests
# Integration tests for the context display feature introduced in v1.3.0 and later

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Context Display Feature Tests" {
    BeforeAll {
        # Temporary directory for tests
        $script:testDir = Join-Path $env:TEMP "ContextDisplayTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        # Clean up the test directory
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "Update-MatchInFile Context Display" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "update-match-test.txt"
            $content = @(
                "Line 1: Normal"
                "Line 2: Target old value here"
                "Line 3: Normal"
                "Line 4: Normal"
                "Line 5: Another old value"
                "Line 6: Normal"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
        }

        It "Includes context display (Contains mode)" {
            # Capture the information stream
            $output = Update-MatchInFile -Path $script:testFile -OldText "old value" -Replacement "new value" -InformationAction Continue 6>&1

            # Verify the replacement succeeded
            $result = Get-Content $script:testFile
            $result[1] | Should -Match "new value"
            $result[4] | Should -Match "new value"

            # Verify context information was output
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
            $contextOutput.Count | Should -BeGreaterThan 3
        }

        It "Includes context display (Pattern mode)" {
            $output = Update-MatchInFile -Path $script:testFile -Pattern "old\s+value" -Replacement "new value" -InformationAction Continue 6>&1

            # Verify the replacement succeeded
            $result = Get-Content $script:testFile
            $result[1] | Should -Match "new value"
            $result[4] | Should -Match "new value"

            # Verify context information was output
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "Includes a marker on replaced lines (shown with :)" {
            $output = Update-MatchInFile -Path $script:testFile -OldText "old value" -Replacement "new value" -InformationAction Continue 6>&1

            # Replaced lines include the : marker
            $matchedLines = $output | Where-Object { $_ -match "^\s+\d+:" }
            $matchedLines | Should -Not -BeNullOrEmpty
            $matchedLines.Count | Should -Be 2
        }

        It "Includes a marker on context lines (shown with -)" {
            $output = Update-MatchInFile -Path $script:testFile -OldText "old value" -Replacement "new value" -InformationAction Continue 6>&1

            # Context lines include the - marker
            $contextLines = $output | Where-Object { $_ -match "^\s+\d+-" }
            $contextLines | Should -Not -BeNullOrEmpty
        }

        It "Includes ANSI escape sequences for the replacement text" {
            $output = Update-MatchInFile -Path $script:testFile -OldText "old value" -Replacement "new value" -InformationAction Continue 6>&1 | Out-String

            # During normal execution only the replaced text is shown (green)
            $output | Should -Match '\x1b\[32m'  # green (after replacement)
            $output | Should -Match '\x1b\[0m'   # reset
        }

        It "Displays the gap between multiple matches correctly" {
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3: match"
                "Line 4"
                "Line 5"
                "Line 6: match"
                "Line 7"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $script:testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # Both matches are included
            $output | Should -Match "Line 3"
            $output | Should -Match "Line 6"
        }
    }

    Context "Add-LinesToFile Context Display" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "add-lines-test.txt"
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
                "Line 5"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
        }

        It "Displays context when inserting" {
            $output = Add-LinesToFile -Path $script:testFile -LineNumber 3 -Content "Inserted Line" -InformationAction Continue 6>&1

            # Verify the insertion succeeded
            $result = Get-Content $script:testFile
            $result[2] | Should -Be "Inserted Line"

            # Verify context information was output
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "Displays context when appending to the end" {
            $output = Add-LinesToFile -Path $script:testFile -Content "Appended Line" -InformationAction Continue 6>&1

            # Verify the append succeeded
            $result = Get-Content $script:testFile
            $result[-1] | Should -Be "Appended Line"

            # Verify context information was output
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "Includes a marker (:) on the inserted line" {
            $output = Add-LinesToFile -Path $script:testFile -LineNumber 3 -Content "Inserted Line" -InformationAction Continue 6>&1

            # The inserted line includes the : marker
            $matchedLines = $output | Where-Object { $_ -match "^\s+\d+:" }
            $matchedLines | Should -Not -BeNullOrEmpty
        }

        It "Includes the inverse-display marker" {
            $output = Add-LinesToFile -Path $script:testFile -LineNumber 3 -Content "Inserted Line" -InformationAction Continue 6>&1 | Out-String

            # ANSI escape sequences are included
            $output | Should -Match '\x1b\[32m'
        }
    }

    Context "Update-LinesInFile Context Display" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "update-lines-test.txt"
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
                "Line 5"
                "Line 6"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
        }

        It "Displays context when replacing lines" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,4 -Content @("New Line 3", "New Line 4") -InformationAction Continue 6>&1

            # Verify the replacement succeeded
            $result = Get-Content $script:testFile
            $result[2] | Should -Be "New Line 3"
            $result[3] | Should -Be "New Line 4"

            # Verify context information was output
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "Displays context when deleting lines" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,4 -Content @() -InformationAction Continue 6>&1

            # Verify the deletion succeeded
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4

            # Verify context information was output
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "Includes a marker on updated lines" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,3 -Content "Updated Line" -InformationAction Continue 6>&1

            # The updated line includes the : marker
            $matchedLines = $output | Where-Object { $_ -match "^\s+\d+:" }
            $matchedLines | Should -Not -BeNullOrEmpty
        }

        It "Includes the inverse-display marker" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,3 -Content "Updated Line" -InformationAction Continue 6>&1 | Out-String

            # ANSI escape sequences are included
            $output | Should -Match '\x1b\[32m'
        }
    }

    Context "Context Display Integration" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "integration-test.txt"
        }

        It "Displays a one-line gap range contiguously" {
            $content = 1..20 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8

            # Match lines 3 and 6 (2-line gap)
            $newContent = $content.Clone()
            $newContent[2] = "Line 3: match"
            $newContent[5] = "Line 6: match"
            Set-Content -Path $script:testFile -Value $newContent -Encoding UTF8

            $output = Update-MatchInFile -Path $script:testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # Since the gap is one line or less, Line 4 and 5 are also shown
            $output | Should -Match "Line 4"
            $output | Should -Match "Line 5"
        }

        It "Separates ranges with a gap of 2 or more lines (separated by a blank line)" {
            $content = 1..20 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8

            # Match lines 3 and 10 (6-line gap)
            $newContent = $content.Clone()
            $newContent[2] = "Line 3: match"
            $newContent[9] = "Line 10: match"
            Set-Content -Path $script:testFile -Value $newContent -Encoding UTF8

            $output = Update-MatchInFile -Path $script:testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # The gap is separated by a blank line (there is a blank line between Line 5 and Line 8)
            $output | Should -Match "5-.*\n\s*\n\s*8-"
        }
    }

    Context "Show-TextFiles GapLine Duplicate Fix" {
        # Issue: verifies the fix for a bug where prevLine was output twice after a gapLine for nearby matches
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "gapline-test.txt"
        }

        It "Does not duplicate context lines for nearby matches (1-line gap)" {
            # Actual bug reproduction case: lines 425 and 429 matched, and line 428 was output twice
            $content = @(
                "Line 1: header"        # 1
                "Line 2: normal"        # 2
                "Line 3: if condition"  # 3  context before
                "Line 4: open"          # 4  context before
                "Line 5: MATCH_A"       # 5  match
                "Line 6: close"         # 6  context after
                "Line 7: else"          # 7  context after / gapLine
                "Line 8: open2"         # 8  context before (was duplicated)
                "Line 9: MATCH_B"       # 9  match
                "Line 10: end"          # 10 context after
                "Line 11: footer"       # 11 context after
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -Contains "MATCH_" | Out-String

            # Verify each line is output exactly once (counted by line number)
            # Verify Line 8 is not output twice
            $line8Matches = [regex]::Matches($output, "^\s*8[:-]", [System.Text.RegularExpressions.RegexOptions]::Multiline)
            $line8Matches.Count | Should -Be 1 -Because "Line 8 should appear exactly once (was duplicated before fix)"

            # Both match lines are included (verified by line number)
            $output | Should -Match "5:"
            $output | Should -Match "9:"
        }

        It "Has no duplicates with consecutive context" {
            # Case where trailing context and leading context overlap
            $content = @(
                "Line 1"          # 1
                "Line 2"          # 2  context before
                "Line 3"          # 3  context before
                "Line 4: MATCH_A" # 4  match
                "Line 5"          # 5  context after
                "Line 6"          # 6  context after / context before
                "Line 7: MATCH_B" # 7  match
                "Line 8"          # 8  context after
                "Line 9"          # 9  context after
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -Contains "MATCH_" | Out-String

            # Verify Line 5 and Line 6 are each output exactly once
            $line5Matches = [regex]::Matches($output, "^\s*5[:-]", [System.Text.RegularExpressions.RegexOptions]::Multiline)
            $line6Matches = [regex]::Matches($output, "^\s*6[:-]", [System.Text.RegularExpressions.RegexOptions]::Multiline)
            
            $line5Matches.Count | Should -Be 1 -Because "Line 5 should appear exactly once"
            $line6Matches.Count | Should -Be 1 -Because "Line 6 should appear exactly once"
        }

        It "Outputs gapLine correctly exactly once (1-line gap case)" {
            # Verifies gapLine behavior (the line output as a join when the gap is one line)
            # Output contiguously as trailing context (2 lines) + gapLine (1 line) + leading context (2 lines)
            $content = @(
                "Line 1"           # 1
                "Line 2"           # 2  context before
                "Line 3"           # 3  context before
                "Line 4: MATCH_A"  # 4  match
                "Line 5"           # 5  context after
                "Line 6"           # 6  context after
                "Line 7"           # 7  gapLine (exactly 1 line gap)
                "Line 8"           # 8  context before
                "Line 9"           # 9  context before
                "Line 10: MATCH_B" # 10 match
                "Line 11"          # 11 context after
                "Line 12"          # 12 context after
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -Contains "MATCH_" | Out-String

            # Verify there are no duplicates
            # Line 8 and Line 9 are each output exactly once (not duplicated)
            $line8Matches = [regex]::Matches($output, "^\s*8[:-]", [System.Text.RegularExpressions.RegexOptions]::Multiline)
            $line9Matches = [regex]::Matches($output, "^\s*9[:-]", [System.Text.RegularExpressions.RegexOptions]::Multiline)
            $line8Matches.Count | Should -Be 1 -Because "Line 8 should appear exactly once"
            $line9Matches.Count | Should -Be 1 -Because "Line 9 should appear exactly once"

            # The match lines are output
            $output | Should -Match "4:"
            $output | Should -Match "10:"
        }

        It "Has no duplicates in Pattern mode either" {
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3: TARGET_ONE"
                "Line 4"
                "Line 5"
                "Line 6: TARGET_TWO"
                "Line 7"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -Pattern "TARGET_\w+" | Out-String

            # Each line is output at most once
            foreach ($lineNum in 1..7) {
                $matches = [regex]::Matches($output, "^\s*$lineNum[:-]", [System.Text.RegularExpressions.RegexOptions]::Multiline)
                $matches.Count | Should -BeLessOrEqual 1 -Because "Line $lineNum should appear at most once"
            }
        }
    }
}