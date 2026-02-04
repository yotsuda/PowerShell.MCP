Describe "Show-TextFile Multi-Line Support" {
    BeforeAll {
        Import-Module "$PSScriptRoot/../../../PowerShell.MCP/bin/Debug/net9.0/PowerShell.MCP.dll" -Force
    }

    Context "Multi-line literal text search" {
        It "Should find and highlight multi-line text with -Contains" {
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

                $output = Show-TextFile $testFile -Contains $searchText | Out-String

                $output | Should -Match "Line 1: Start"
                $output | Should -Match "Line 2: First Block"
                $output | Should -Match "Line 3: Middle Block"
                $output | Should -Match "Line 4: End Block"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should show context lines around match" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Context Before 1
Context Before 2
Match Line 1
Match Line 2
Context After 1
Context After 2
"@ | Set-Content $testFile

                $searchText = @"
Match Line 1
Match Line 2
"@

                $output = Show-TextFile $testFile -Contains $searchText | Out-String

                $output | Should -Match "Context Before"
                $output | Should -Match "Match Line"
                $output | Should -Match "Context After"
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

                $output = Show-TextFile $testFile -Contains $searchText | Out-String

                $output | Should -Match "Line 2"
                $output | Should -Match "Line 3"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Multi-line regex pattern search" {
        It "Should find multi-line pattern with -Pattern" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Start
Line A: Data
Line B: More
Line C: End
Final
"@ | Set-Content $testFile

                $pattern = "Line A:.*\nLine B:.*\nLine C:"

                $output = Show-TextFile $testFile -Pattern $pattern | Out-String

                $output | Should -Match "Line A: Data"
                $output | Should -Match "Line B: More"
                $output | Should -Match "Line C: End"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Multiple matches" {
        It "Should find all multi-line occurrences" {
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

                $output = Show-TextFile $testFile -Contains $searchText | Out-String

                # Should show both occurrences
                ($output -split "Block A").Count - 1 | Should -BeGreaterOrEqual 2
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "LineRange filtering" {
        It "Should respect LineRange with multi-line search" {
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

                # Should only find first occurrence (lines 1-4)
                $output = Show-TextFile $testFile -Contains $searchText -LineRange 1,4 | Out-String

                $output | Should -Match "Line 1"
                $output | Should -Not -Match "Line 7"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }

    Context "Edge cases" {
        It "Should handle no matches" {
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

                $output = Show-TextFile $testFile -Contains $searchText | Out-String

                $output | Should -BeNullOrEmpty
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }

        It "Should not support -Recurse with multi-line patterns" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                "Line 1`nLine 2" | Set-Content $testFile

                $searchText = "Line 1`nLine 2"

                { Show-TextFile $testFile -Contains $searchText -Recurse -ErrorAction Stop } | Should -Throw
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }
}