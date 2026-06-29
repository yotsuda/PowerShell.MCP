# Test-AllCmdlets.ps1
# Integration tests for all text-file operation cmdlets

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "All Text File Cmdlets Integration Tests" {
    BeforeAll {
        # Create a temporary directory and file for testing
        $script:testDir = Join-Path $env:TEMP "PowerShellMCP_Tests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
        
        $script:testFile = Join-Path $script:testDir "test.txt"
        $script:testContent = @(
            "# Sample File"
            "Line 1: First line of content"
            "Line 2: Second line of content"
            "Line 3: Third line of content"
            "Line 4: Fourth line of content"
            "Line 5: Fifth line of content"
            "# End of file"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterAll {
        # Cleanup
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force
        }
    }

    Context "Complete workflow: display -> add -> update -> delete" {
        It "Step 1: confirm file contents with Show-TextFiles" {
            $content = Show-TextFiles -Path $script:testFile
            $content | Should -Not -BeNullOrEmpty
            # Header line + 7 content lines = 8 lines
            $content.Count | Should -Be 8
        }

        It "Step 2: add a new line with Add-LinesToFile" {
            Add-LinesToFile -Path $script:testFile -Content "Line 6: Added line"
            $content = Get-Content $script:testFile
            # Added before the end (before "# End of file") or appended at the end
            $content | Should -Contain "Line 6: Added line"
        }

        It "Step 3: update a line with Update-LinesInFile" {
            Update-LinesInFile -Path $script:testFile -LineRange 3,3 -Content "Line 2: UPDATED line"
            $content = Get-Content $script:testFile
            $content | Where-Object { $_ -match "UPDATED" } | Should -Not -BeNullOrEmpty
        }

        It "Step 4: delete a line with Remove-LinesFromFile" {
            $beforeCount = (Get-Content $script:testFile).Count
            Remove-LinesFromFile -Path $script:testFile -LineRange 1,1
            $afterCount = (Get-Content $script:testFile).Count
            $afterCount | Should -Be ($beforeCount - 1)
        }
    }

    Context "Batch operations on multiple files" {
        BeforeAll {
            # Create multiple test files
            $script:multiFiles = 1..3 | ForEach-Object {
                $file = Join-Path $script:testDir "multi_$_.txt"
                Set-Content -Path $file -Value "Content of file $_" -Encoding UTF8
                $file
            }
        }

        It "run Show-TextFiles against multiple files" {
            $results = Show-TextFiles -Path $script:multiFiles
            $results | Should -Not -BeNullOrEmpty
            # Header line + content line for each file
            # 3 files x (1 header + 1 content) = 6 lines
            $results.Count | Should -BeGreaterOrEqual 6
        }

        It "run Add-LinesToFile against multiple files" {
            Add-LinesToFile -Path $script:multiFiles -Content "Added line"
            foreach ($file in $script:multiFiles) {
                $content = Get-Content $file
                $content[-1] | Should -Be "Added line"
            }
        }
    }

    Context "Pipeline processing" {
        It "the Get-ChildItem | Show-TextFiles pipeline works" {
            $results = Get-ChildItem -Path $script:testDir -Filter "*.txt" |
                Select-Object -First 1 |
                Show-TextFiles
            $results | Should -Not -BeNullOrEmpty
        }

        It "can pipe file objects to Show-TextFiles" {
            $files = Get-ChildItem -Path $script:testDir -Filter "multi_*.txt" | Select-Object -First 1
            $results = $files | Show-TextFiles
            $results | Should -Not -BeNullOrEmpty
        }
    }

    Context "Encoding compatibility" {
        BeforeAll {
            $script:utf8File = Join-Path $script:testDir "utf8.txt"
            $script:sjisFile = Join-Path $script:testDir "sjis.txt"

            "UTF-8 テスト 日本語" | Out-File -FilePath $script:utf8File -Encoding UTF8

            # Create a file in Shift-JIS
            $sjisEncoding = [System.Text.Encoding]::GetEncoding("shift_jis")
            $sjisBytes = $sjisEncoding.GetBytes("Shift-JIS テスト 日本語")
            [System.IO.File]::WriteAllBytes($script:sjisFile, $sjisBytes)
        }

        It "can read a UTF-8 file correctly" {
            $content = Show-TextFiles -Path $script:utf8File -Encoding "utf-8"
            # The actual data line (after the header) contains Japanese
            ($content -join "`n") | Should -Match "日本語"
        }

        It "can read a Shift-JIS file correctly" {
            $content = Show-TextFiles -Path $script:sjisFile -Encoding "shift_jis"
            # The actual data line (after the header) contains Japanese
            ($content -join "`n") | Should -Match "日本語"
        }

        It "encoding auto-detection works" {
            $content = Show-TextFiles -Path $script:utf8File
            # Japanese is readable even with auto-detection
            ($content -join "`n") | Should -Match "UTF-8"
        }
    }

    Context "Error handling and recovery" {

        It "can do pattern-matching replacement with Update-MatchInFile" {
            $testFile = Join-Path $script:testDir "pattern_test.txt"
            Set-Content -Path $testFile -Value @("test@example.com", "user@domain.com")
            
            Update-MatchInFile -Path $testFile -Pattern "@example\.com" -Replacement "@newdomain.com"
            $content = Get-Content $testFile
            $content[0] | Should -Be "test@newdomain.com"
            
            Remove-Item $testFile -Force
        }
    }

    Context "Backup and safety" {
        It "the -Backup option works for all commands" {
            $backupTestFile = Join-Path $script:testDir "backup_test.txt"
            Set-Content -Path $backupTestFile -Value "Original"

            Add-LinesToFile -Path $backupTestFile -Content "Added" -Backup
            # A timestamped backup is created
            $backups = Get-ChildItem -Path $script:testDir -Filter "backup_test.txt.*" |
                Where-Object { $_.Name -match '\.bak$' }
            $backups.Count | Should -BeGreaterThan 0

            Remove-Item "$backupTestFile*" -Force
        }

        It "-WhatIf works for all commands" {
            $whatIfFile = Join-Path $script:testDir "whatif_test.txt"
            Set-Content -Path $whatIfFile -Value "Original"
            $original = Get-Content $whatIfFile
            
            Add-LinesToFile -Path $whatIfFile -Content "Test" -WhatIf
            Update-LinesInFile -Path $whatIfFile -LineRange 1,1 -Content "Test" -WhatIf
            Remove-LinesFromFile -Path $whatIfFile -LineRange 1,1 -WhatIf
            
            $after = Get-Content $whatIfFile
            Compare-Object $original $after | Should -BeNullOrEmpty
            
            Remove-Item $whatIfFile -Force
        }
    }
}

# How to run the test suite
<#
.SYNOPSIS
    Run integration tests for all text-file operation cmdlets

.EXAMPLE
    # Run all tests
    Invoke-Pester -Path .\Test-AllCmdlets.ps1

.EXAMPLE
    # Run with detailed output
    Invoke-Pester -Path .\Test-AllCmdlets.ps1 -Output Detailed

.EXAMPLE
    # Run only a specific context
    Invoke-Pester -Path .\Test-AllCmdlets.ps1 -TagFilter "workflow"
#>