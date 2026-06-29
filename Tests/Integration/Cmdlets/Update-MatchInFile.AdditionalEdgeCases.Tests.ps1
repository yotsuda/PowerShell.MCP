Describe "Update-MatchInFile - Additional Edge Cases" {
    BeforeAll {
        $script:testDir = Join-Path ([System.IO.Path]::GetTempPath()) "PSMCPTests_$(Get-Random)"
        New-Item -Path $script:testDir -ItemType Directory -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context "Behavior when there is no match" {
        It "does not change the file when Pattern does not match" {
            $testFile = Join-Path $script:testDir "no-match.txt"
            $originalContent = @("Line 1", "Line 2", "Line 3")
            Set-Content -Path $testFile -Value $originalContent
            
            Update-MatchInFile -Path $testFile -Pattern "NonExistent" -Replacement "New"
            
            $content = Get-Content $testFile
            $content[0] | Should -Be "Line 1"
            $content[1] | Should -Be "Line 2"
            $content[2] | Should -Be "Line 3"
        }
    }

    Context "Replacement with special values" {
        It "Replacement is an empty string (deletes the matched portion)" {
            $testFile = Join-Path $script:testDir "empty-replacement.txt"
            Set-Content -Path $testFile -Value "Remove this word from the sentence"
            
            Update-MatchInFile -Path $testFile -Pattern "this word " -Replacement ""
            
            $content = Get-Content $testFile
            $content | Should -Be "Remove from the sentence"
        }
    }

    Context "Replacement using capture groups" {
        It "swaps order using capture groups" {
            $testFile = Join-Path $script:testDir "capture-group.txt"
            Set-Content -Path $testFile -Value "FirstName LastName"
            
            Update-MatchInFile -Path $testFile -Pattern '(\w+) (\w+)' -Replacement '$2, $1'
            
            $content = Get-Content $testFile
            $content | Should -Be "LastName, FirstName"
        }
    }

    Context "Error handling" {
        It "errors when replacing in a nonexistent file" {
            $nonExistentFile = Join-Path $script:testDir "nonexistent.txt"
            
            { Update-MatchInFile -Path $nonExistentFile -Pattern "test" -Replacement "new" -ErrorAction Stop } |
                Should -Throw
        }
        It "errors when Pattern contains a newline" {
            $testFile = Join-Path $script:testDir "newline-pattern.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            { Update-MatchInFile -Path $testFile -Pattern "Line 1`nLine 2" -Replacement "Test" -ErrorAction Stop } |
                Should -Throw "*cannot contain newline*"
        }

        It "succeeds in multiline mode when OldText contains a newline" {
            $testFile = Join-Path $script:testDir "newline-contains.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            { Update-MatchInFile -Path $testFile -OldText "Line 1`nLine 2" -Replacement "Merged" -ErrorAction Stop } |
                Should -Not -Throw
            $result = Get-Content $testFile
            $result[0] | Should -Be "Merged"
            $result[1] | Should -Be "Line 3"
        }
    }
}
