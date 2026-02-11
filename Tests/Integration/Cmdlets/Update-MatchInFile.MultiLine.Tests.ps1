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
}