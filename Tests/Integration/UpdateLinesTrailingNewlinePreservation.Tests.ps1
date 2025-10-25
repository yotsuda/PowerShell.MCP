# Trailing Newline Preservation Tests for Update-LinesInFile
# Update-LinesInFile が元のファイルの末尾改行を保持することを確認する統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-LinesInFile Trailing Newline Preservation" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "UpdateLinesTrailingNewlineTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "When file has trailing newline" {
        It "Should preserve trailing newline when replacing single line" {
            # Arrange
            $testFile = Join-Path $script:testDir "test1.txt"
            [System.IO.File]::WriteAllText($testFile, "Line1`r`nLine2`r`nLine3`r`n", [System.Text.Encoding]::UTF8)
            
            $bytesBeforeUpdate = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeUpdate[-2] | Should -Be 0x0D
            $bytesBeforeUpdate[-1] | Should -Be 0x0A
            
            # Act
            Update-LinesInFile -Path $testFile -LineRange 2,2 -Content "UpdatedLine2"
            
            # Assert
            $bytesAfterUpdate = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterUpdate[-2] | Should -Be 0x0D -Because "末尾にCRが保持されるべき"
            $bytesAfterUpdate[-1] | Should -Be 0x0A -Because "末尾にLFが保持されるべき"
        }

        It "Should preserve trailing newline when replacing multiple lines" {
            # Arrange
            $testFile = Join-Path $script:testDir "test2.txt"
            [System.IO.File]::WriteAllText($testFile, "First`r`nSecond`r`nThird`r`nFourth`r`n", [System.Text.Encoding]::UTF8)
            
            # Act
            Update-LinesInFile -Path $testFile -LineRange 2,3 -Content @("New2", "New3")
            
            # Assert
            $bytesAfterUpdate = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterUpdate[-2] | Should -Be 0x0D -Because "複数行置換でも末尾にCRが保持されるべき"
            $bytesAfterUpdate[-1] | Should -Be 0x0A -Because "複数行置換でも末尾にLFが保持されるべき"
        }

        It "Should preserve trailing newline when replacing entire file" {
            # Arrange
            $testFile = Join-Path $script:testDir "test3.txt"
            [System.IO.File]::WriteAllText($testFile, "Old1`r`nOld2`r`n", [System.Text.Encoding]::UTF8)
            
            # Act (LineRange指定なし = ファイル全体置換)
            Update-LinesInFile -Path $testFile -Content @("New1", "New2", "New3")
            
            # Assert
            $bytesAfterUpdate = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterUpdate[-2] | Should -Be 0x0D -Because "ファイル全体置換でも末尾にCRが保持されるべき"
            $bytesAfterUpdate[-1] | Should -Be 0x0A -Because "ファイル全体置換でも末尾にLFが保持されるべき"
        }
    }

    Context "When file has no trailing newline" {
        It "Should preserve no trailing newline when replacing single line" {
            # Arrange
            $testFile = Join-Path $script:testDir "test4.txt"
            [System.IO.File]::WriteAllText($testFile, "Line1`r`nLine2`r`nLine3", [System.Text.Encoding]::UTF8)
            
            $bytesBeforeUpdate = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeUpdate[-1] | Should -Not -Be 0x0A -Because "元のファイルは末尾改行なし"
            
            # Act
            Update-LinesInFile -Path $testFile -LineRange 2,2 -Content "UpdatedLine2"
            
            # Assert
            $bytesAfterUpdate = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterUpdate[-1] | Should -Not -Be 0x0A -Because "元のファイルに改行がないので、更新後も改行なしのはず"
        }

        It "Should preserve no trailing newline when replacing multiple lines" {
            # Arrange
            $testFile = Join-Path $script:testDir "test5.txt"
            [System.IO.File]::WriteAllText($testFile, "Line1`r`nLine2`r`nLine3", [System.Text.Encoding]::UTF8)
            
            # Act
            Update-LinesInFile -Path $testFile -LineRange 1,2 -Content @("New1", "New2")
            
            # Assert
            $bytesAfterUpdate = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterUpdate[-1] | Should -Not -Be 0x0A -Because "元のファイルに改行がないので、複数行置換でも末尾改行なし"
        }
    }
}
