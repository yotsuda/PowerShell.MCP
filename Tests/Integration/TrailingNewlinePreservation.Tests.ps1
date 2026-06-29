# Trailing Newline Preservation Tests
# Integration tests verifying that Add-LinesToFile preserves the original file's trailing newline

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Add-LinesToFile Trailing Newline Preservation" {
    BeforeAll {
        # Temporary directory for tests
        $script:testDir = Join-Path $env:TEMP "TrailingNewlineTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        # Clean up the test directory
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "When file has trailing newline" {
        It "Should preserve trailing newline when adding single line" {
            # Arrange
            $testFile = Join-Path $script:testDir "test1.txt"
            "Line1`r`nLine2`r`nLine3`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Verify the original file has a trailing newline
            $bytesBeforeAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeAdd[-2] | Should -Be 0x0D  # CR
            $bytesBeforeAdd[-1] | Should -Be 0x0A  # LF

            # Act
            Add-LinesToFile -Path $testFile -Content "Line4"

            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "the trailing CR should be preserved"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "the trailing LF should be preserved"
        }

        It "Should preserve trailing newline when adding multiple lines (3 lines)" {
            # Arrange
            $testFile = Join-Path $script:testDir "test2.txt"
            "First`r`nSecond`r`nThird`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -Content @("NewLine1", "NewLine2", "NewLine3")
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "the trailing CR should be preserved even when adding multiple lines"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "the trailing LF should be preserved even when adding multiple lines"
        }

        It "Should preserve trailing newline when adding more than 6 lines (omission display case)" {
            # Arrange
            $testFile = Join-Path $script:testDir "test3.txt"
            "Start`r`nMiddle`r`nEnd`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -Content @("L1", "L2", "L3", "L4", "L5", "L6", "L7")
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "the trailing CR should be preserved even when adding 7 lines (omission display)"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "the trailing LF should be preserved even when adding 7 lines (omission display)"
        }

        It "Should preserve trailing newline when inserting in the middle" {
            # Arrange
            $testFile = Join-Path $script:testDir "test4.txt"
            "Line1`r`nLine2`r`nLine3`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -LineNumber 2 -Content "InsertedLine"
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "the trailing CR should be preserved even when inserting in the middle"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "the trailing LF should be preserved even when inserting in the middle"
        }
    }

    Context "When file has no trailing newline" {
        It "Should preserve no trailing newline when adding single line" {
            # Arrange
            $testFile = Join-Path $script:testDir "test5.txt"
            "Line1`r`nLine2`r`nLine3" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Verify the original file has no trailing newline
            $bytesBeforeAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeAdd[-1] | Should -Not -Be 0x0A -Because "the original file has no trailing newline"

            # Act
            Add-LinesToFile -Path $testFile -Content "Line4"

            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-1] | Should -Not -Be 0x0A -Because "since the original file has no newline, there should be none after the change either"
        }

        It "Should preserve no trailing newline when adding multiple lines" {
            # Arrange
            $testFile = Join-Path $script:testDir "test6.txt"
            "NoNewline" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -Content @("Line1", "Line2")
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-1] | Should -Not -Be 0x0A -Because "since the original file has no newline, there is no trailing newline even when adding multiple lines"
        }
    }

    Context "Regression test for the original bug" {
        It "Should fix the original issue where trailing newline was lost" {
            # Arrange: case that reproduces the original problem
            $testFile = Join-Path $script:testDir "test_regression.txt"

            # Create the file the same way as the original problem's setup
            $initialContent = @'
Line1
Line2
Line3
'@
            $initialContent | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            "`r`n" | Out-File -FilePath $testFile -Encoding utf8 -Append -NoNewline

            # Verify the original file has a trailing newline
            $bytesBeforeAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeAdd[-2] | Should -Be 0x0D
            $bytesBeforeAdd[-1] | Should -Be 0x0A

            # Act: the original problem's operation
            Add-LinesToFile -Path $testFile -Content "Line4"

            # Assert: after the bug fix, the trailing newline should be preserved
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "after the fix, the original problem is resolved and the trailing CR should be preserved"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "after the fix, the original problem is resolved and the trailing LF should be preserved"

            # Verify the content too
            $content = [System.IO.File]::ReadAllText($testFile)
            $content | Should -Match "(?s)Line1.*Line2.*Line3.*Line4"
        }
    }
}