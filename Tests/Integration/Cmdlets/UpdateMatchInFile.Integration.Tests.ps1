# Integration tests for the Update-MatchInFile cmdlet

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-MatchInFile Integration Tests" {
    BeforeAll {
        # Create a temp file for the tests
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterEach {
        # Clean up the file after each test
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterAll {
        # Final cleanup
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    Context "Replacement with an empty string (deletion)" {
        It "can delete matched text when an empty string is given to the Contains parameter" {
            # Arrange
            Set-Content -Path $script:testFile -Value "Server=localhost:8080" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -OldText ":8080" -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "Server=localhost"
        }

        It "can delete matched text when an empty string is given to the Pattern parameter" {
            # Arrange
            Set-Content -Path $script:testFile -Value "Price: `$99.99 (tax included)" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -Pattern '\$[\d.]+\s*' -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "Price: (tax included)"
        }

        It "can delete all matches in multiple places (Contains)" {
            # Arrange
            Set-Content -Path $script:testFile -Value "test1 DEBUG test2 DEBUG test3" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -OldText "DEBUG " -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "test1 test2 test3"
        }

        It "can delete all matches in multiple places (Pattern)" {
            # Arrange
            Set-Content -Path $script:testFile -Value "abc123def456ghi789" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -Pattern '\d+' -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "abcdefghi"
        }
    }

    Context "Required check for the Replacement parameter" {
        It "errors when Contains is specified but Replacement is omitted" {
            # Arrange
            Set-Content -Path $script:testFile -Value "test content" -Encoding UTF8 -NoNewline
            
            # Act & Assert
            { Update-MatchInFile -Path $script:testFile -OldText "test" } | Should -Throw
        }

        It "errors when Pattern is specified but Replacement is omitted" {
            # Arrange
            Set-Content -Path $script:testFile -Value "test123" -Encoding UTF8 -NoNewline
            
            # Act & Assert
            { Update-MatchInFile -Path $script:testFile -Pattern '\d+' } | Should -Throw
        }

        It "errors when only Replacement is specified" {
            # Arrange
            Set-Content -Path $script:testFile -Value "test content" -Encoding UTF8 -NoNewline
            
            # Act & Assert
            { Update-MatchInFile -Path $script:testFile -Replacement "new" } | Should -Throw
        }
    }

    Context "Encoding handling" {
        It "preserves encoding after empty-string replacement (UTF-8)" {
            # Arrange
            Set-Content -Path $script:testFile -Value "日本語テキスト DEBUG 終了" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -OldText "DEBUG " -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw -Encoding UTF8
            $result | Should -BeExactly "日本語テキスト 終了"
        }

        It "preserves encoding after empty-string replacement (Shift_JIS)" {
            # Arrange
            $content = "日本語テキスト DEBUG 終了"
            [System.IO.File]::WriteAllText($script:testFile, $content, [System.Text.Encoding]::GetEncoding("Shift_JIS"))
            
            # Act
            Update-MatchInFile -Path $script:testFile -OldText "DEBUG " -Replacement ""
            
            # Assert
            $result = [System.IO.File]::ReadAllText($script:testFile, [System.Text.Encoding]::GetEncoding("Shift_JIS"))
            $result | Should -BeExactly "日本語テキスト 終了"
        }
    }

    Context "Preserving newlines" {
        It "when there is a trailing newline, it is preserved after deletion" {
            # Arrange
            # Make 3 lines "Line1", "DEBUG", "Line2", then delete "DEBUG" on line 2
            Set-Content -Path $script:testFile -Value @("Line1", "DEBUG", "Line2") -Encoding UTF8

            # Act
            Update-MatchInFile -Path $script:testFile -OldText "DEBUG" -Replacement ""

            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            # DEBUG is deleted, leaving an empty line. On Windows this becomes CRLF
            $result | Should -BeExactly "Line1`r`n`r`nLine2`r`n"
        }

        It "when there is no trailing newline, it stays without a newline after deletion" {
            # Arrange
            [System.IO.File]::WriteAllText($script:testFile, "Line1`r`nDEBUG`r`nLine2", [System.Text.Encoding]::UTF8)
            
            # Act
            Update-MatchInFile -Path $script:testFile -OldText "DEBUG" -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            # DEBUG is deleted, leaving an empty line
            $result | Should -BeExactly "Line1`r`n`r`nLine2"
        }
    }
}