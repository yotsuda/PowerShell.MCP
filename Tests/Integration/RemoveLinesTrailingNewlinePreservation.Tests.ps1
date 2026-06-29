# Trailing Newline Preservation Tests
# Integration tests verifying that Remove-LinesFromFile preserves the original file's trailing newline

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Remove-LinesFromFile Trailing Newline Preservation" {
    BeforeAll {
        # Temporary directory for tests
        $script:testDir = Join-Path $env:TEMP "RemoveLinesTrailingNewlineTests_$(Get-Random)"
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
            Remove-LinesFromFile -Path $testFile -LineRange 2,2

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
            Remove-LinesFromFile -Path $testFile -Contains "Second"
            
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
            Remove-LinesFromFile -Path $testFile -Pattern "Middle"
            
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
            Remove-LinesFromFile -Path $testFile -LineRange 2,2
            
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
            Remove-LinesFromFile -Path $testFile -LineRange 2,2

            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-1] | Should -Not -Be 0x0A -Because "since the original file has no newline, there should be none after the change either"
        }

        It "Should preserve no trailing newline when adding multiple lines" {
            # Arrange
            $testFile = Join-Path $script:testDir "test6.txt"
            "NoNewline" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Remove-LinesFromFile -Path $testFile -Contains "NoNewline"
            
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
            Remove-LinesFromFile -Path $testFile -LineRange 2,2

            # Assert: after the bug fix, the trailing newline should be preserved
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "after the fix, the original problem is resolved and the trailing CR should be preserved"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "after the fix, the original problem is resolved and the trailing LF should be preserved"

            # Verify the content too (Line2 should have been removed)
            $content = [System.IO.File]::ReadAllText($testFile)
            $content | Should -Match "(?s)Line1.*Line3"
        }

        It "Should preserve trailing newline when removing the LAST line" {
            # Regression: previously the final-line trailing-newline check was gated on
            # `!shouldRemove` of the LAST processed line. When the last line was the one
            # being deleted, the gate was false → no trailing newline written → the new
            # last kept line lost its trailing CRLF.
            $testFile = Join-Path $script:testDir "test_lastline.txt"
            [System.IO.File]::WriteAllText($testFile, "Line1`r`nLine2`r`nLine3`r`n", [System.Text.Encoding]::UTF8)

            Remove-LinesFromFile -Path $testFile -LineRange 3

            $bytesAfter = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfter[-2] | Should -Be 0x0D -Because "the original CRLF should be preserved even when the last line is removed"
            $bytesAfter[-1] | Should -Be 0x0A
        }

        It "Should preserve trailing newline when removing a tail range" {
            $testFile = Join-Path $script:testDir "test_tailrange.txt"
            [System.IO.File]::WriteAllText($testFile, "Line1`r`nLine2`r`nLine3`r`nLine4`r`n", [System.Text.Encoding]::UTF8)

            Remove-LinesFromFile -Path $testFile -LineRange 3,4

            $bytesAfter = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfter[-2] | Should -Be 0x0D
            $bytesAfter[-1] | Should -Be 0x0A
            (Get-Content $testFile).Count | Should -Be 2
        }
    }

    Context "When file has NO trailing newline" {
        It "Should NOT add a trailing newline when removing the last line" {
            $testFile = Join-Path $script:testDir "test_notrailing.txt"
            [System.IO.File]::WriteAllText($testFile, "Line1`r`nLine2`r`nLine3", [System.Text.Encoding]::UTF8)

            Remove-LinesFromFile -Path $testFile -LineRange 3

            $bytesAfter = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfter[-1] | Should -Not -Be 0x0A -Because "if there was no trailing newline to begin with, one must not be added"
        }
    }
}
