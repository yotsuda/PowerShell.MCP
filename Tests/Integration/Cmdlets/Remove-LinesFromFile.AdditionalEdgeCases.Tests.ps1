Describe "Remove-LinesFromFile - Additional Edge Cases" {
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
        It "does not change the file when Contains does not match" {
            $testFile = Join-Path $script:testDir "no-match.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")
            
            Remove-LinesFromFile -Path $testFile -Contains "NonExistent"
            
            $content = Get-Content $testFile
            $content.Count | Should -Be 3
        }
    }

    Context "Deleting all lines" {
        It "becomes an empty file when all lines match and are deleted" {
            $testFile = Join-Path $script:testDir "delete-all.txt"
            Set-Content -Path $testFile -Value @("Delete 1", "Delete 2", "Delete 3")
            
            Remove-LinesFromFile -Path $testFile -Contains "Delete"
            
            Test-Path $testFile | Should -Be $true
            $content = Get-Content $testFile -ErrorAction SilentlyContinue
            if ($content) {
                $content.Count | Should -Be 0
            } else {
                $content | Should -BeNullOrEmpty
            }
        }
    }

    Context "Special patterns" {
        It "deletes empty lines" {
            $testFile = Join-Path $script:testDir "empty-lines.txt"
            Set-Content -Path $testFile -Value @("Line 1", "", "Line 3", "", "Line 5")
            
            Remove-LinesFromFile -Path $testFile -Pattern "^$"
            
            $content = Get-Content $testFile
            $content.Count | Should -Be 3
            $content | Should -Not -Contain ""
        }
    }

    Context "Error handling" {
        It "errors when deleting from a nonexistent file" {
            $nonExistentFile = Join-Path $script:testDir "nonexistent.txt"
            
            { Remove-LinesFromFile -Path $nonExistentFile -Contains "test" -ErrorAction Stop } | Should -Throw
        }

        It "errors when Pattern contains a newline" {
            $testFile = Join-Path $script:testDir "newline-pattern.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            { Remove-LinesFromFile -Path $testFile -Pattern "Line 1`nLine 2" -ErrorAction Stop } |
                Should -Throw "*cannot contain newline*"
        }

        It "operates in multiline mode when Contains contains a newline" {
            $testFile = Join-Path $script:testDir "newline-contains.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            # Multiline Contains is now supported (no error, removes matching text)
            Remove-LinesFromFile -Path $testFile -Contains "Line 1`r`nLine 2"
            $result = Get-Content $testFile -Raw
            $result | Should -Not -Match "Line 1"
            $result | Should -Match "Line 3"
        }
    }
}