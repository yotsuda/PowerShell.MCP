# Remove-LinesFromFile.Tests.ps1
# Integration tests for the Remove-LinesFromFile cmdlet

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Remove-LinesFromFile Integration Tests" {
    BeforeEach {
        # Create a new temp file before each test
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "# Header"
            "Line 1: First line"
            "Line 2: Second line"
            "Line 3: Third line"
            "ERROR: Connection timeout"
            "error: invalid input"
            "Line 4: Fourth line"
            "WARNING: This is a warning"
            "Line 5: Fifth line"
            "# Footer"
        )
Set-Content -Path $script:testFile -Value $script:initialContent -Encoding UTF8
    }

    AfterEach {
        # Clean up after each test
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        # Clean up backup files too
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force
    }

    Context "Deletion by line range" {
        It "can delete a single line" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 2
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 9
            $result -notcontains "Line 1: First line" | Should -Be $true
        }

        It "can delete multiple consecutive lines" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 2,4
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 7
            $result -notcontains "Line 1: First line" | Should -Be $true
            $result -notcontains "Line 2: Second line" | Should -Be $true
            $result -notcontains "Line 3: Third line" | Should -Be $true
        }

        It "can delete the first line" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Line 1: First line"
            $result.Count | Should -Be 9
        }

        It "can delete the last line" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 10
            $result = Get-Content $script:testFile
            $result[-1] | Should -Be "Line 5: Fifth line"  # The last line is the original line 9
            $result.Count | Should -Be 9
        }
    }

    Context "Deletion by text match (Contains)" {
        It "can delete lines that contain the specified text" {
            Remove-LinesFromFile -Path $script:testFile -Contains "ERROR"
            $result = Get-Content $script:testFile
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result.Count | Should -Be 9
        }

        It "when multiple lines match, all of them are deleted" {
            Remove-LinesFromFile -Path $script:testFile -Contains "Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 5  # Only Header, ERROR, error, WARNING, Footer remain
        }

        It "is case-sensitive" {
            Remove-LinesFromFile -Path $script:testFile -Contains "error"
            $result = Get-Content $script:testFile
            $result -contains "ERROR: Connection timeout" | Should -Be $true  # Uppercase ERROR remains
            $result -notcontains "error: invalid input" | Should -Be $true    # Lowercase error is deleted
        }

        It "when no line matches, nothing is changed" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Contains "NonExistentText"
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "Deletion by regular expression (Pattern)" {
        It "can delete lines that match a regular expression" {
            Remove-LinesFromFile -Path $script:testFile -Pattern "^ERROR:"
            $result = Get-Content $script:testFile
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result.Count | Should -Be 9
        }

        It "can use a complex regular expression pattern" {
            Remove-LinesFromFile -Path $script:testFile -Pattern "^(ERROR|WARNING):"
            $result = Get-Content $script:testFile
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result -notcontains "WARNING: This is a warning" | Should -Be $true
            $result.Count | Should -Be 8
        }

        It "deletes lines of a specific format using a line-number pattern" {
            Remove-LinesFromFile -Path $script:testFile -Pattern "Line \d+:"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 5  # Only Header, ERROR, error, WARNING, Footer remain
        }
    }

    Context "Conditional deletion within a range" {
        It "can combine LineRange and Contains" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 2,6 -Contains "ERROR"
            $result = Get-Content $script:testFile
            # Within the range of lines 2-6, only line 5 (which contains ERROR) is deleted
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result -contains "WARNING: This is a warning" | Should -Be $true  # Outside the range, so it remains
        }

        It "can combine LineRange and Pattern" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1,5 -Pattern "^Line \d+:"
            $result = Get-Content $script:testFile
            # Lines matching the pattern within the range of lines 1-5 are deleted
            $result -notcontains "Line 1: First line" | Should -Be $true
            $result -notcontains "Line 2: Second line" | Should -Be $true
            $result -notcontains "Line 3: Third line" | Should -Be $true
            $result -contains "Line 4: Fourth line" | Should -Be $true  # Outside the range
            $result -contains "Line 5: Fifth line" | Should -Be $true   # Outside the range
        }
    }

    Context "Encoding" {
        It "can process a UTF-8 file correctly" {
            $content = @("日本語 Line 1", "English Line 2", "日本語 Line 3")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            Remove-LinesFromFile -Path $script:testFile -Contains "English" -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result.Count | Should -Be 2
            $result -contains "日本語 Line 1" | Should -Be $true
        }
    }

    Context "Backup feature" {
        It "creates a backup file when -Backup is specified" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1 -Backup
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak"
            $backupFiles.Count | Should -BeGreaterThan 0
        }

        It "saves the original content in the backup file" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -LineRange 1 -Backup
            $backupFile = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak" | Select-Object -First 1
            $backupContent = Get-Content $backupFile.FullName
            $backupContent | Should -Be $originalContent
        }
    }

    Context "WhatIf and Confirm" {
        It "does not actually change the file when -WhatIf is specified" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Contains "ERROR" -WhatIf
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "Error handling" {
        It "errors on a nonexistent file" {
            { Remove-LinesFromFile -Path "C:\NonExistent\file.txt" -LineRange 1 -ErrorAction Stop } | Should -Throw
        }

        It "warns on an out-of-range line number but continues" {
            $result = Remove-LinesFromFile -Path $script:testFile -LineRange 100 -WarningVariable warnings 3>&1
            $warnings | Should -Not -BeNullOrEmpty
        }

        It "errors on an invalid range specification" {
            { Remove-LinesFromFile -Path $script:testFile -LineRange 9,2 } | Should -Throw
        }

        It "errors on an invalid regular expression" {
            { Remove-LinesFromFile -Path $script:testFile -Pattern "[invalid(" -ErrorAction Stop } | Should -Throw
        }

        It "errors when none of LineRange, Contains, or Pattern is specified" {
            { Remove-LinesFromFile -Path $script:testFile } | Should -Throw
        }
    }

    Context "Pipeline input" {
        It "can process multiple files from the pipeline" {
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value @("ERROR: Error in file2", "Normal line")
            
            try {
                Get-Item @($script:testFile, $file2) | Remove-LinesFromFile -Contains "ERROR"
                $result1 = Get-Content $script:testFile
                $result2 = Get-Content $file2
                $result1 -notcontains "ERROR: Connection timeout" | Should -Be $true
                $result2 -notcontains "ERROR: Error in file2" | Should -Be $true
            }
            finally {
                Remove-Item $file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "Context display" {
        It "displays 2 lines of context before and after when deleting a single line" {
            $output = Remove-LinesFromFile -Path $script:testFile -LineRange 5,5 | Out-String

            # 2 lines of context before (shown with '-', post-deletion line numbers)
            $output | Should -Match '3- Line 2: Second line'
            $output | Should -Match '4- Line 3: Third line'

            # Deletion marker (shown with ':', no line number)
            $output | Should -Match '   :'

            # 2 lines of context after (shown with '-', post-deletion line numbers)
            $output | Should -Match '5- error: invalid input'
            $output | Should -Match '6- Line 4: Fourth line'
        }

        It "displays context without duplication when deleting non-contiguous lines" {
            $output = Remove-LinesFromFile -Path $script:testFile -Contains "ERROR" | Out-String

            # First deletion range (uppercase ERROR, line 5 deleted)
            $output | Should -Match '3- Line 2: Second line'
            $output | Should -Match '4- Line 3: Third line'
            $output | Should -Match '   :'
            $output | Should -Match '5- error: invalid input'
            $output | Should -Match '6- Line 4: Fourth line'

            # Confirm context lines are not duplicated
            $contextLineCount = ([regex]::Matches($output, '6- Line 4: Fourth line')).Count
            $contextLineCount | Should -Be 1
        }

        It "displays context correctly when deleting the first line" {
            $output = Remove-LinesFromFile -Path $script:testFile -LineRange 1,1 | Out-String

            # No 2 preceding lines exist (because it is the first line)
            # Deletion marker (no line number)
            $output | Should -Match '   :'

            # 2 lines after (shown with '-', post-deletion line numbers)
            $output | Should -Match '1- Line 1: First line'
            $output | Should -Match '2- Line 2: Second line'
        }

        It "displays context correctly when deleting the last line" {
            $output = Remove-LinesFromFile -Path $script:testFile -LineRange 9,10 | Out-String

            # 2 preceding lines
            $output | Should -Match '7- Line 4: Fourth line'
            $output | Should -Match '8- WARNING: This is a warning'

            # Deletion marker (no line number)
            $output | Should -Match '   :'
        }

        It "displays only one marker when deleting multiple consecutive lines" {
            $output = Remove-LinesFromFile -Path $script:testFile -LineRange 2,4 | Out-String

            # 2 preceding lines (only line 1 exists)
            $output | Should -Match '1- # Header'

            # Deletion marker (shown only once because the deletion is contiguous)
            $markerCount = ([regex]::Matches($output, '(?m)^\s+:\s*$')).Count
            $markerCount | Should -Be 1

            # 2 lines after (shown with '-', post-deletion line numbers)
            $output | Should -Match '2- ERROR: Connection timeout'
            $output | Should -Match '3- error: invalid input'
        }

        It "displays a marker for each range when there are multiple deletion ranges" {
            # Searching for "error" (lowercase) matches only "error: invalid input"
            $output = Remove-LinesFromFile -Path $script:testFile -Contains "error" | Out-String

            # Only one deletion range (1 line)
            $output | Should -Match '   :'

            # Only one marker
            $markerCount = ([regex]::Matches($output, '(?m)^\s+:\s*$')).Count
            $markerCount | Should -Be 1
        }

        It "displays post-deletion line numbers consecutively and correctly" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 3,5
            $result = Get-Content $script:testFile

            # The file has been deleted correctly
            $result.Count | Should -Be 7
            $result[0] | Should -Be "# Header"
            $result[1] | Should -Be "Line 1: First line"
            $result[2] | Should -Be "error: invalid input"
            $result[3] | Should -Be "Line 4: Fourth line"
        }

        It "uses outputLineNumber to avoid duplication in the 2 preceding lines of context" {
            # A more complex case: when multiple deletion ranges are close together
            Set-Content -Path $script:testFile -Value @(
                "Keep 1"
                "Delete 1"
                "Keep 2"
                "Delete 2"
                "Delete 3"
                "Keep 3"
            ) -Encoding UTF8
            
            $output = Remove-LinesFromFile -Path $script:testFile -Contains "Delete" | Out-String
            
            # "Keep 2" should be displayed only once
            $keep2Count = ([regex]::Matches($output, 'Keep 2')).Count
            $keep2Count | Should -Be 1
        }
    }

    Context "-WhatIf escape sequences" {
        It "highlights only the Contains-matched portion with a yellow background" {
            Set-Content -Path $script:testFile -Value @(
                "Line 1"
                "DELETE this line"
                "Line 3"
            ) -Encoding UTF8
            
            $result = Remove-LinesFromFile -Path $script:testFile -Contains "DELETE" -WhatIf
            $deleteLine = $result | Where-Object { $_ -match "DELETE" }
            
            # The matched portion starts with [31;43m (red text + yellow background)
            $deleteLine | Should -Match '\x1b\[31;43mDELETE'

            # After the match, it resets to [31;49m (red text + default background)
            $deleteLine | Should -Match 'DELETE\x1b\[31;49m'
        }

        It "highlights only the Pattern-matched portion with a yellow background" {
            Set-Content -Path $script:testFile -Value @(
                "Line 1"
                "Error code 123 found"
                "Line 3"
            ) -Encoding UTF8
            
            $result = Remove-LinesFromFile -Path $script:testFile -Pattern "\d+" -WhatIf
            $matchLine = $result | Where-Object { $_ -match "123" }
            
            # The matched portion starts with [31;43m (red text + yellow background)
            $matchLine | Should -Match '\x1b\[31;43m123'

            # After the match, it resets to [31;49m (red text + default background)
            $matchLine | Should -Match '123\x1b\[31;49m'
        }

        It "no yellow background highlight when only LineRange is used" {
            Set-Content -Path $script:testFile -Value @(
                "Line 1"
                "Line 2"
                "Line 3"
            ) -Encoding UTF8
            
            $result = Remove-LinesFromFile -Path $script:testFile -LineRange 2,2 -WhatIf
            $deleteLine = $result | Where-Object { $_ -match "Line 2" }
            
            # The entire line is displayed in red [31m
            $deleteLine | Should -Match '\x1b\[31m'

            # The yellow background [43m is not included
            $deleteLine | Should -Not -Match '\[43m'
        }
    }
}