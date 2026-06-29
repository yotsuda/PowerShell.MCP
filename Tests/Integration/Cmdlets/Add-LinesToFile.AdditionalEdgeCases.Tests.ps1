Describe "Add-LinesToFile - Additional Edge Cases" {
        BeforeAll {
        Import-Module "$PSScriptRoot/../../Shared/TestHelpers.psm1" -Force
        $script:testDir = Join-Path ([System.IO.Path]::GetTempPath()) "PSMCPTests_$(Get-Random)"
        New-Item -Path $script:testDir -ItemType Directory -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context "Operations on an empty file" {
        It "can add a line to an empty file" {
            $testFile = Join-Path $script:testDir "empty.txt"
            New-Item -Path $testFile -ItemType File -Force | Out-Null

            Add-LinesToFile -Path $testFile -Content "First line"

            $content = Get-Content $testFile -Raw
            $content | Should -Match "First line"
        }

        It "appends multiple lines to an empty file" {
            $testFile = Join-Path $script:testDir "empty2.txt"
            New-Item -Path $testFile -ItemType File -Force | Out-Null

            $lines = @("Line 1", "Line 2", "Line 3")
            Add-LinesToFile -Path $testFile -Content $lines

            $content = Get-Content $testFile
            $content.Count | Should -Be 3
            $content[0] | Should -Be "Line 1"
            $content[2] | Should -Be "Line 3"
        }
    }

    Context "LineNumber boundary value tests" {
        It "LineNumber equal to total line count + 1 (treated as an append to the end)" {
            $testFile = Join-Path $script:testDir "boundary1.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            Add-LinesToFile -Path $testFile -LineNumber 4 -Content "Line 4"

            $content = Get-Content $testFile
            $content.Count | Should -Be 4
            $content[3] | Should -Be "Line 4"
        }

        It "inserts at the beginning with LineNumber 1" {
            $testFile = Join-Path $script:testDir "boundary3.txt"
            Set-Content -Path $testFile -Value @("Line 2", "Line 3")

            Add-LinesToFile -Path $testFile -LineNumber 1 -Content "Line 1"

            $content = Get-Content $testFile
            $content[0] | Should -Be "Line 1"
            $content[1] | Should -Be "Line 2"
        }

        It "when LineNumber exceeds the file line count, the summary shows the actual insertion position" {
            $testFile = Join-Path $script:testDir "boundary-large.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            $output = Add-LinesToFile -Path $testFile -LineNumber 100 -Content "New Line"

            # The content is added at line 4
            $content = Get-Content $testFile
            $content.Count | Should -Be 4
            $content[3] | Should -Be "New Line"

            # The summary line shows the actual insertion position (4), not the specified value of 100
            $summaryLine = $output | Where-Object { $_ -match "Added.*line\(s\)" }
            $summaryLine | Should -Match "at line 4"
            $summaryLine | Should -Not -Match "at line 100"
        }
    }

    Context "Special cases for the Content parameter" {
        It "passing an empty array causes a parameter binding error" {
            $testFile = Join-Path $script:testDir "empty-array.txt"
            Set-Content -Path $testFile -Value "Original"

            # An empty array cannot be passed to the Content parameter
            { Add-LinesToFile -Path $testFile -Content @() } | Should -Throw
        }
    }

    Context "New file creation behavior" {
        It "appending to a nonexistent file (no LineNumber) creates a new file (no warning)" {
            $nonExistentFile = Join-Path $script:testDir "newfile1.txt"

            # Confirm no warning is emitted
            $warnings = @()
            Add-LinesToFile -Path $nonExistentFile -Content "Line 1" -WarningVariable warnings

            $warnings.Count | Should -Be 0
            $nonExistentFile | Should -Exist
            Get-Content $nonExistentFile | Should -Be "Line 1"
        }

        It "appending to a nonexistent file (LineNumber 1) creates a new file (no warning)" {
            $nonExistentFile = Join-Path $script:testDir "newfile2.txt"

            # Confirm no warning is emitted
            $warnings = @()
            Add-LinesToFile -Path $nonExistentFile -LineNumber 1 -Content "Line 1" -WarningVariable warnings

            $warnings.Count | Should -Be 0
            $nonExistentFile | Should -Exist
            Get-Content $nonExistentFile | Should -Be "Line 1"
        }

        It "appending to a nonexistent file (LineNumber > 1) warns and creates a new file" {
            $nonExistentFile = Join-Path $script:testDir "newfile3.txt"

            # Confirm a warning is emitted
            $warnings = @()
            Add-LinesToFile -Path $nonExistentFile -LineNumber 5 -Content "Line 1" -WarningVariable warnings

            $warnings.Count | Should -Be 1
            $warnings[0] | Should -Match "File does not exist.*LineNumber 5 will be treated as line 1"
            $nonExistentFile | Should -Exist
            Get-Content $nonExistentFile | Should -Be "Line 1"
        }
    }
}
