Describe "Remove-LinesFromFile Multi-Line Support" {

    Context "Multi-line literal text removal" {
        It "Should remove multi-line text block with -Contains" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1: Start
Line 2: First Block
Line 3: Middle Block
Line 4: End Block
Line 5: Final
"@ | Set-Content $testFile

                $searchText = @"
Line 2: First Block
Line 3: Middle Block
Line 4: End Block
"@

                Remove-LinesFromFile -Path $testFile -Contains $searchText

                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 1: Start"
                $result | Should -Match "Line 5: Final"
                $result | Should -Not -Match "First Block"
                $result | Should -Not -Match "Middle Block"
                $result | Should -Not -Match "End Block"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should remove multiple occurrences" {
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

                $searchText = @"
Block A
Block B
"@

                Remove-LinesFromFile -Path $testFile -Contains $searchText

                $result = Get-Content $testFile -Raw
                $result | Should -Match "Separator"
                $result | Should -Match "End"
                $result | Should -Not -Match "Block A"
                $result | Should -Not -Match "Block B"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should normalize newlines when searching" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                # Create file with LF newlines
                "Line 1`nLine 2`nLine 3`nLine 4" | Set-Content $testFile -NoNewline

                # Search with CRLF in pattern (should be normalized to LF)
                $searchText = "Line 2`r`nLine 3"

                Remove-LinesFromFile -Path $testFile -Contains $searchText

                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 1"
                $result | Should -Match "Line 4"
                $result | Should -Not -Match "Line 2"
                $result | Should -Not -Match "Line 3"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Multi-line regex pattern removal" {
        It "Should error when -Pattern contains newlines" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Start
Line A: Data
Line B: More
Line C: End
Final
"@ | Set-Content $testFile

                $pattern = "Line A:.*`nLine B:.*"

                { Remove-LinesFromFile -Path $testFile -Pattern $pattern -ErrorAction Stop } | Should -Throw "*Pattern cannot contain newline*"
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

                $searchText = @"
Line 2
Line 3
"@

                $output = Remove-LinesFromFile -Path $testFile -Contains $searchText -WhatIf 2>&1 | Out-String

                $output | Should -Match "What if:"

                # File should not be modified
                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 2"
                $result | Should -Match "Line 3"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should show context lines in diff preview" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Context Before
Remove Me
Remove Me Too
Context After
"@ | Set-Content $testFile

                $searchText = @"
Remove Me
Remove Me Too
"@

                $output = Remove-LinesFromFile -Path $testFile -Contains $searchText -WhatIf 2>&1 | Out-String

                $output | Should -Match "Context Before"
                $output | Should -Match "Context After"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "LineRange filtering" {
        It "Should respect LineRange with multi-line removal" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1
Match A
Match B
Line 4
Match A
Match B
Line 7
"@ | Set-Content $testFile

                $searchText = @"
Match A
Match B
"@

                # Should only remove first occurrence (within lines 1-4)
                Remove-LinesFromFile -Path $testFile -Contains $searchText -LineRange 1,4

                $result = Get-Content $testFile -Raw
                $result | Should -Match "Line 1"
                $result | Should -Match "Line 4"
                $result | Should -Match "Line 7"
                # Second occurrence should still be there
                $lines = $result -split "`n"
                ($lines | Where-Object { $_ -match "Match A" }).Count | Should -Be 1
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Backup support" {
        It "Should create backup when -Backup specified with multiline" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1
Remove Me
Remove Also
Line 4
"@ | Set-Content $testFile

                $searchText = @"
Remove Me
Remove Also
"@

                Remove-LinesFromFile -Path $testFile -Contains $searchText -Backup

                # Backup file should exist (timestamped: *.YYYYMMDDHHMMSS.bak)
                $backupFiles = Get-ChildItem -Path "$testFile.*.bak" -ErrorAction SilentlyContinue
                $backupFiles | Should -Not -BeNullOrEmpty

                # Original content should be in backup
                $backupContent = Get-Content $backupFiles[0].FullName -Raw
                $backupContent | Should -Match "Remove Me"

                # Modified file should not have removed text
                $result = Get-Content $testFile -Raw
                $result | Should -Not -Match "Remove Me"

                $backupFiles | Remove-Item -Force
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Edge cases" {
        It "Should handle no matches found" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Line 1
Line 2
Line 3
"@ | Set-Content $testFile

                $searchText = @"
Not Found
Also Not Found
"@

                $output = Remove-LinesFromFile -Path $testFile -Contains $searchText 3>&1 | Out-String

                $output | Should -Match "No matches found"

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

        It "Should not support tail removal with multi-line Contains" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                "Line 1`nLine 2`nLine 3" | Set-Content $testFile

                $searchText = "Line 2`nLine 3"

                { Remove-LinesFromFile -Path $testFile -Contains $searchText -LineRange -2 -ErrorAction Stop } | Should -Throw
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }
}
