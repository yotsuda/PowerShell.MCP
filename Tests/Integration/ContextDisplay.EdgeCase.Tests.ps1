# Additional Context Display Edge Case Tests
# Additional edge-case tests for the context display feature introduced in v1.3.0 and later

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Context Display Edge Case Tests" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "ContextEdgeCaseTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "Context display at file boundaries" {
        It "Displays context for a match on the first line of the file" {
            $testFile = Join-Path $script:testDir "edge1.txt"
            $content = @("Line 1: match", "Line 2", "Line 3")
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # Even on the first line, the 2 following context lines are shown
            $output | Should -Match "Line 2"
            $output | Should -Match "Line 3"
        }

        It "Displays context for a match on the last line of the file" {
            $testFile = Join-Path $script:testDir "edge2.txt"
            $content = @("Line 1", "Line 2", "Line 3: match")
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # Even on the last line, the 2 preceding context lines are shown
            $output | Should -Match "Line 1"
            $output | Should -Match "Line 2"
        }

        It "Match in a single-line file" {
            $testFile = Join-Path $script:testDir "edge3.txt"
            Set-Content -Path $testFile -Value "Single line: match" -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # Context display works even with a single line
            $output | Should -Match "REPLACED"
        }
    }

    Context "Context display for multi-line operations" {
        It "Displays context when inserting multiple lines" {
            $testFile = Join-Path $script:testDir "multi1.txt"
            $content = @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5")
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Add-LinesToFile -Path $testFile -LineNumber 3 -Content @("New A", "New B", "New C") -InformationAction Continue 6>&1

            # Context for a multi-line insertion
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "Displays context when replacing multiple lines" {
            $testFile = Join-Path $script:testDir "multi2.txt"
            $content = 1..10 | ForEach-Object { "Line $_" }
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-LinesInFile -Path $testFile -LineRange 4,7 -Content @("New 4", "New 5", "New 6", "New 7") -InformationAction Continue 6>&1

            # Context for a multi-line replacement
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
            $contextOutput.Count | Should -BeGreaterThan 4
        }
    }

    Context "Context display when LineRange is specified" {
        It "Displays context for a replacement only within LineRange" {
            $testFile = Join-Path $script:testDir "range1.txt"
            $content = 1..10 | ForEach-Object { "Line $_ - test" }
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -LineRange 3,7 -OldText "test" -Replacement "updated" -InformationAction Continue 6>&1 | Out-String

            # Only matches within LineRange are shown
            $output | Should -Match "Line 3"
            $output | Should -Match "Line 7"
            # Lines outside LineRange are not shown
            $output | Should -Not -Match "Line 1:"
            $output | Should -Not -Match "Line 10:"
        }
    }

    Context "Blank line and gap handling" {
        It "Merges when the gap is exactly one line" {
            $testFile = Join-Path $script:testDir "gap1.txt"
            $content = 1..15 | ForEach-Object { "Line $_" }
            $content[2] = "Line 3: match"
            $content[5] = "Line 6: match"
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # The gap is one line, so it is merged
            $output | Should -Match "Line 4"
            $output | Should -Match "Line 5"
            # No separation marker (blank line)
            $output | Should -Not -Match "4-.*\n\s*\n"
        }

        It "Separates when the gap is 2 or more lines" {
            $testFile = Join-Path $script:testDir "gap2.txt"
            $content = 1..20 | ForEach-Object { "Line $_" }
            $content[2] = "Line 3: match"
            $content[11] = "Line 12: match"  # The gap is 6 lines: lines 6, 7, 8, 9, 10, 11
            Set-Content -Path $testFile -Value $content -Encoding UTF8

            $output = Update-MatchInFile -Path $testFile -OldText "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String

            # The gap is large, so it is separated
            # A separation marker (blank line) is included
            $output | Should -Match "5-.*`r?`n\s*`r?`n"
        }
    }

    Context "Context display for replacements containing special characters" {
        It "Displays context even for replacements containing special characters" {
            $testFile = Join-Path $script:testDir "special.txt"
            $content = @(
                "Line 1: normal"
                "Line 2: C:\path\to\file"
                "Line 3: normal"
            )
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -Pattern 'C:\\path\\to\\file' -Replacement 'D:\new\path' -InformationAction Continue 6>&1 | Out-String

            # Context display works even for replacements containing special characters
            $output | Should -Match "Line 2"
            $output | Should -Match '\x1b\[32m'  # green (after replacement) - during normal execution only the replaced text is shown
        }
    }
}