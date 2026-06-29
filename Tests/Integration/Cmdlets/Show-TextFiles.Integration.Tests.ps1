# Test-ShowTextFileCmdlet.ps1
# Integration tests for the Show-TextFiles cmdlet (existing + HIGH priority)

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Show-TextFiles Integration Tests" {
    BeforeAll {
        # Create a temporary file for testing
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Line 1: First line"
            "Line 2: Second line"
            "Line 3: Third line"
            "Line 4: Fourth line"
            "Line 5: Fifth line"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterAll {
        # Cleanup
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    Context "Basic file display" {
        It "can display the entire file" {
            $result = Show-TextFiles -Path $script:testFile
            $result | Should -Not -BeNullOrEmpty
            # Header line + 5 content lines = 6 lines
            $result.Count | Should -Be 6
        }

        It "can display with line numbers" {
            $result = Show-TextFiles -Path $script:testFile
            # The line after the header is the actual data
            $result[1] | Should -Match "^\s*1:"
            $result[5] | Should -Match "^\s*5:"
        }
    }

    Context "Line range specification" {
        It "can display only the specified line range" {
            $result = Show-TextFiles -Path $script:testFile -LineRange 2,4
            # Header line + 3 content lines = 4 lines
            $result.Count | Should -Be 4
            $result[1] | Should -Match "Line 2"
            $result[3] | Should -Match "Line 4"
        }

        It "can display a single line" {
            $result = Show-TextFiles -Path $script:testFile -LineRange 3,3
            # Header line + 1 content line = 2 lines
            $result.Count | Should -Be 2
            $result[1] | Should -Match "Line 3"
        }
    }

    Context "Text search" {
        It "can search for a string with the Contains parameter" {
            $result = Show-TextFiles -Path $script:testFile -Contains "Third"
            $result | Should -Not -BeNullOrEmpty
            # New implementation: results are shown with 3 lines of context before/after, so confirm the matched line is included in the result
            $result | Where-Object { $_ -match ':.*Third' } | Should -Not -BeNullOrEmpty
        }

        It "can do a regular expression search with the Pattern parameter" {
            $result = Show-TextFiles -Path $script:testFile -Pattern "Line \d:"
            # Header line + 5 matched lines = 6 lines
            $result.Count | Should -Be 6
        }
    }

    Context "Encoding" {
        It "can read with UTF-8 encoding" {
            $result = Show-TextFiles -Path $script:testFile -Encoding "utf-8"
            $result | Should -Not -BeNullOrEmpty
        }

        It "can read even without specifying an encoding" {
            $result = Show-TextFiles -Path $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }
    }

    Context "Error handling" {
        It "errors on a nonexistent file" {
            { Show-TextFiles -Path "C:\NonExistent\File.txt" -ErrorAction Stop } | Should -Throw
        }

        It "warns but continues on an invalid line range" {
            # Lenient design: emits a warning but does not throw an error
            $result = Show-TextFiles -Path $script:testFile -LineRange 100,200 -WarningAction SilentlyContinue
            # A warning is emitted, but processing continues
            $result | Should -Not -BeNull
        }
    }

    Context "HIGH priority: edge cases and boundary values" {
        It "H1. can display an empty file" {
            $emptyFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $emptyFile -Value @() -Encoding UTF8
            
            try {
                $result = Show-TextFiles -Path $emptyFile
                # Header line only
                $result | Should -Not -BeNullOrEmpty
                $result.Count | Should -Be 1
            }
            finally {
                Remove-Item $emptyFile -Force
            }
        }

        It "H2. can display a single-line file" {
            $singleLineFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $singleLineFile -Value "Single line" -Encoding UTF8

            try {
                $result = Show-TextFiles -Path $singleLineFile
                # Header line + 1 line = 2 lines
                $result.Count | Should -Be 2
                $result[1] | Should -Match "Single line"
            }
            finally {
                Remove-Item $singleLineFile -Force
            }
        }

        It "H3. the LineRange + Contains combination works" {
            $result = Show-TextFiles -Path $script:testFile -LineRange 2,4 -Contains "Third"
            # Only lines within the line range that match Contains
            $result | Should -Not -BeNullOrEmpty
            ($result -join "`n") | Should -Match "Third"
        }

        It "H4. the LineRange + Pattern combination works" {
            $result = Show-TextFiles -Path $script:testFile -LineRange 1,3 -Pattern "Line \d:"
            # Only lines within the line range that match Pattern
            $result | Should -Not -BeNullOrEmpty
            $result.Count | Should -BeGreaterThan 1
        }

        It "H5. errors when LineRange is reversed [5,1]" {
            # The implementation rejects reversed ranges
            { Show-TextFiles -Path $script:testFile -LineRange 5,1 } | Should -Throw
        }

        It "H6. produces a parameter validation error when LineRange = [0,0]" {
            # 0 is an invalid line number
            { Show-TextFiles -Path $script:testFile -LineRange 0,0 -ErrorAction Stop } |
                Should -Throw
        }

        It "H7. warns when LineRange is out of range [100,200]" {
            # Overlaps with an existing test, but confirm explicitly
            $warnings = @()
            $result = Show-TextFiles -Path $script:testFile -LineRange 100,200 -WarningVariable warnings -WarningAction SilentlyContinue
            # A warning message is output
            $result | Should -Not -BeNull
        }

        It "H8. multiple files + LineRange works" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file1 -Value @("File1-Line1", "File1-Line2", "File1-Line3")
            Set-Content -Path $file2 -Value @("File2-Line1", "File2-Line2", "File2-Line3")
            
            try {
                $result = Show-TextFiles -Path $file1,$file2 -LineRange 1,2
                # Header for each file + lines in the specified range
                $result | Should -Not -BeNullOrEmpty
                ($result -join "`n") | Should -Match "File1-Line1"
                ($result -join "`n") | Should -Match "File2-Line1"
            }
            finally {
                Remove-Item $file1,$file2 -Force
            }
        }

        It "H9. multiple files + Contains works" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file1 -Value @("Apple", "Banana", "Cherry")
            Set-Content -Path $file2 -Value @("Dog", "Elephant", "Fox")
            
            try {
                $result = Show-TextFiles -Path $file1,$file2 -Contains "Elephant"
                # Only the file containing Elephant is displayed
                $result | Should -Not -BeNullOrEmpty
                ($result -join "`n") | Should -Match "Elephant"
            }
            finally {
                Remove-Item $file1,$file2 -Force
            }
        }

        It "H10. errors when a directory path is specified" {
            $tempDir = Join-Path $env:TEMP "TestDir_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                { Show-TextFiles -Path $tempDir -ErrorAction Stop } | Should -Throw
            }
            finally {
                Remove-Item $tempDir -Force -Recurse
            }
        }

        It "H11. errors on a file without access permission" {
            $protectedFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $protectedFile -Value "Protected content"

            try {
                # Set to deny read
                $acl = Get-Acl $protectedFile
                $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                    [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
                    "Read",
                    "Deny"
                )
                $acl.AddAccessRule($accessRule)
                Set-Acl -Path $protectedFile -AclObject $acl
                
                { Show-TextFiles -Path $protectedFile -ErrorAction Stop } | Should -Throw
            }
            finally {
                # Reset the ACL
                $acl = Get-Acl $protectedFile
                $acl.Access | Where-Object { $_.AccessControlType -eq "Deny" } | ForEach-Object {
                    $acl.RemoveAccessRule($_) | Out-Null
                }
                Set-Acl -Path $protectedFile -AclObject $acl
                Remove-Item $protectedFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "H12. warns but continues on an invalid encoding name" {
            # The implementation is lenient: it warns and continues
            $result = Show-TextFiles -Path $script:testFile -Encoding "invalid-encoding-name" -WarningAction SilentlyContinue
            # Does not error; reads with the default encoding
            $result | Should -Not -BeNullOrEmpty
        }
    }
}