# Trailing Newline Preservation Tests
# Add-LinesToFile が元のファイルの末尾改行を保持することを確認する統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Add-LinesToFile Trailing Newline Preservation" {
    BeforeAll {
        # テスト用の一時ディレクトリ
        $script:testDir = Join-Path $env:TEMP "TrailingNewlineTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        # テストディレクトリのクリーンアップ
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "When file has trailing newline" {
        It "Should preserve trailing newline when adding single line" {
            # Arrange
            $testFile = Join-Path $script:testDir "test1.txt"
            "Line1`r`nLine2`r`nLine3`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # 元のファイルに末尾改行があることを確認
            $bytesBeforeAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeAdd[-2] | Should -Be 0x0D  # CR
            $bytesBeforeAdd[-1] | Should -Be 0x0A  # LF
            
            # Act
            Add-LinesToFile -Path $testFile -Content "Line4"
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "末尾にCRが保持されるべき"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "末尾にLFが保持されるべき"
        }

        It "Should preserve trailing newline when adding multiple lines (3 lines)" {
            # Arrange
            $testFile = Join-Path $script:testDir "test2.txt"
            "First`r`nSecond`r`nThird`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -Content @("NewLine1", "NewLine2", "NewLine3")
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "複数行追加でも末尾にCRが保持されるべき"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "複数行追加でも末尾にLFが保持されるべき"
        }

        It "Should preserve trailing newline when adding more than 6 lines (omission display case)" {
            # Arrange
            $testFile = Join-Path $script:testDir "test3.txt"
            "Start`r`nMiddle`r`nEnd`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -Content @("L1", "L2", "L3", "L4", "L5", "L6", "L7")
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "7行追加（省略表示）でも末尾にCRが保持されるべき"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "7行追加（省略表示）でも末尾にLFが保持されるべき"
        }

        It "Should preserve trailing newline when inserting in the middle" {
            # Arrange
            $testFile = Join-Path $script:testDir "test4.txt"
            "Line1`r`nLine2`r`nLine3`r`n" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -LineNumber 2 -Content "InsertedLine"
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "途中挿入でも末尾にCRが保持されるべき"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "途中挿入でも末尾にLFが保持されるべき"
        }
    }

    Context "When file has no trailing newline" {
        It "Should preserve no trailing newline when adding single line" {
            # Arrange
            $testFile = Join-Path $script:testDir "test5.txt"
            "Line1`r`nLine2`r`nLine3" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # 元のファイルに末尾改行がないことを確認
            $bytesBeforeAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeAdd[-1] | Should -Not -Be 0x0A -Because "元のファイルは末尾改行なし"
            
            # Act
            Add-LinesToFile -Path $testFile -Content "Line4"
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-1] | Should -Not -Be 0x0A -Because "元のファイルに改行がないので、追加後も改行なしのはず"
        }

        It "Should preserve no trailing newline when adding multiple lines" {
            # Arrange
            $testFile = Join-Path $script:testDir "test6.txt"
            "NoNewline" | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            
            # Act
            Add-LinesToFile -Path $testFile -Content @("Line1", "Line2")
            
            # Assert
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-1] | Should -Not -Be 0x0A -Because "元のファイルに改行がないので、複数行追加でも末尾改行なし"
        }
    }

    Context "Regression test for the original bug" {
        It "Should fix the original issue where trailing newline was lost" {
            # Arrange: 元の問題を再現するケース
            $testFile = Join-Path $script:testDir "test_regression.txt"
            
            # 元の問題のセットアップと同じ方法でファイルを作成
            $initialContent = @'
Line1
Line2
Line3
'@
            $initialContent | Out-File -FilePath $testFile -Encoding utf8 -NoNewline
            "`r`n" | Out-File -FilePath $testFile -Encoding utf8 -Append -NoNewline
            
            # 元のファイルに末尾改行があることを確認
            $bytesBeforeAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesBeforeAdd[-2] | Should -Be 0x0D
            $bytesBeforeAdd[-1] | Should -Be 0x0A
            
            # Act: 元の問題の操作
            Add-LinesToFile -Path $testFile -Content "Line4"
            
            # Assert: バグ修正後は末尾改行が保持されるはず
            $bytesAfterAdd = [System.IO.File]::ReadAllBytes($testFile)
            $bytesAfterAdd[-2] | Should -Be 0x0D -Because "修正後は元の問題が解決され、末尾にCRが保持されるべき"
            $bytesAfterAdd[-1] | Should -Be 0x0A -Because "修正後は元の問題が解決され、末尾にLFが保持されるべき"
            
            # 内容も確認
            $content = [System.IO.File]::ReadAllText($testFile)
            $content | Should -Match "(?s)Line1.*Line2.*Line3.*Line4"
        }
    }
}