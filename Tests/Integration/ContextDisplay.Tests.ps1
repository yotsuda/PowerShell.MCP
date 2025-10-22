# Context Display Feature Tests
# v1.3.0以降のコンテキスト表示機能の統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Context Display Feature Tests" {
    BeforeAll {
        # テスト用の一時ディレクトリ
        $script:testDir = Join-Path $env:TEMP "ContextDisplayTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        # テストディレクトリのクリーンアップ
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "Update-MatchInFile Context Display" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "update-match-test.txt"
            $content = @(
                "Line 1: Normal"
                "Line 2: Target old value here"
                "Line 3: Normal"
                "Line 4: Normal"
                "Line 5: Another old value"
                "Line 6: Normal"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
        }

        It "コンテキスト表示が含まれる（Contains モード）" {
            # 情報ストリームをキャプチャ
            $output = Update-MatchInFile -Path $script:testFile -Contains "old value" -Replacement "new value" -InformationAction Continue 6>&1
            
            # 置換が成功していることを確認
            $result = Get-Content $script:testFile
            $result[1] | Should -Match "new value"
            $result[4] | Should -Match "new value"
            
            # コンテキスト情報が出力されていることを確認
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
            $contextOutput.Count | Should -BeGreaterThan 3
        }

        It "コンテキスト表示が含まれる（Pattern モード）" {
            $output = Update-MatchInFile -Path $script:testFile -Pattern "old\s+value" -Replacement "new value" -InformationAction Continue 6>&1
            
            # 置換が成功していることを確認
            $result = Get-Content $script:testFile
            $result[1] | Should -Match "new value"
            $result[4] | Should -Match "new value"
            
            # コンテキスト情報が出力されていることを確認
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "置換行にマーカーが含まれる（: で表示）" {
            $output = Update-MatchInFile -Path $script:testFile -Contains "old value" -Replacement "new value" -InformationAction Continue 6>&1
            
            # 置換された行に : マーカーが含まれている
            $matchedLines = $output | Where-Object { $_ -match "^\s+\d+:" }
            $matchedLines | Should -Not -BeNullOrEmpty
            $matchedLines.Count | Should -Be 2
        }

        It "コンテキスト行にマーカーが含まれる（- で表示）" {
            $output = Update-MatchInFile -Path $script:testFile -Contains "old value" -Replacement "new value" -InformationAction Continue 6>&1
            
            # コンテキスト行に - マーカーが含まれている
            $contextLines = $output | Where-Object { $_ -match "^\s+\d+-" }
            $contextLines | Should -Not -BeNullOrEmpty
        }

        It "反転表示マーカー（ANSIエスケープシーケンス）が含まれる" {
            $output = Update-MatchInFile -Path $script:testFile -Contains "old value" -Replacement "new value" -InformationAction Continue 6>&1 | Out-String
            
            # ANSIエスケープシーケンス（反転表示 [7m と リセット [0m）が含まれている
            $output | Should -Match '\x1b\[7m'
            $output | Should -Match '\x1b\[0m'
        }

        It "複数マッチのギャップが適切に表示される" {
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3: match"
                "Line 4"
                "Line 5"
                "Line 6: match"
                "Line 7"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $script:testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # 両方のマッチが含まれている
            $output | Should -Match "Line 3"
            $output | Should -Match "Line 6"
        }
    }

    Context "Add-LinesToFile Context Display" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "add-lines-test.txt"
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
                "Line 5"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
        }

        It "挿入時にコンテキストが表示される" {
            $output = Add-LinesToFile -Path $script:testFile -LineNumber 3 -Content "Inserted Line" -InformationAction Continue 6>&1
            
            # 挿入が成功していることを確認
            $result = Get-Content $script:testFile
            $result[2] | Should -Be "Inserted Line"
            
            # コンテキスト情報が出力されていることを確認
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "末尾追加時にコンテキストが表示される" {
            $output = Add-LinesToFile -Path $script:testFile -Content "Appended Line" -InformationAction Continue 6>&1
            
            # 追加が成功していることを確認
            $result = Get-Content $script:testFile
            $result[-1] | Should -Be "Appended Line"
            
            # コンテキスト情報が出力されていることを確認
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "挿入行にマーカー（:）が含まれる" {
            $output = Add-LinesToFile -Path $script:testFile -LineNumber 3 -Content "Inserted Line" -InformationAction Continue 6>&1
            
            # 挿入された行に : マーカーが含まれている
            $matchedLines = $output | Where-Object { $_ -match "^\s+\d+:" }
            $matchedLines | Should -Not -BeNullOrEmpty
        }

        It "反転表示マーカーが含まれる" {
            $output = Add-LinesToFile -Path $script:testFile -LineNumber 3 -Content "Inserted Line" -InformationAction Continue 6>&1 | Out-String
            
            # ANSIエスケープシーケンスが含まれている
            $output | Should -Match '\x1b\[7m'
        }
    }

    Context "Update-LinesInFile Context Display" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "update-lines-test.txt"
            $content = @(
                "Line 1"
                "Line 2"
                "Line 3"
                "Line 4"
                "Line 5"
                "Line 6"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
        }

        It "行置換時にコンテキストが表示される" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,4 -Content @("New Line 3", "New Line 4") -InformationAction Continue 6>&1
            
            # 置換が成功していることを確認
            $result = Get-Content $script:testFile
            $result[2] | Should -Be "New Line 3"
            $result[3] | Should -Be "New Line 4"
            
            # コンテキスト情報が出力されていることを確認
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "行削除時にコンテキストが表示される" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,4 -InformationAction Continue 6>&1
            
            # 削除が成功していることを確認
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            
            # コンテキスト情報が出力されていることを確認
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "更新行にマーカーが含まれる" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,3 -Content "Updated Line" -InformationAction Continue 6>&1
            
            # 更新された行に : マーカーが含まれている
            $matchedLines = $output | Where-Object { $_ -match "^\s+\d+:" }
            $matchedLines | Should -Not -BeNullOrEmpty
        }

        It "反転表示マーカーが含まれる" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,3 -Content "Updated Line" -InformationAction Continue 6>&1 | Out-String
            
            # ANSIエスケープシーケンスが含まれている
            $output | Should -Match '\x1b\[7m'
        }
    }

    Context "Context Display Integration" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "integration-test.txt"
        }

        It "ギャップ1行の範囲が連続して表示される" {
            $content = 1..20 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            # 3行目と6行目にマッチ（ギャップ2行）
            $newContent = $content.Clone()
            $newContent[2] = "Line 3: match"
            $newContent[5] = "Line 6: match"
            Set-Content -Path $script:testFile -Value $newContent -Encoding UTF8
            
            $output = Update-MatchInFile -Path $script:testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # ギャップ1行以下なので、Line 4, 5 も表示される
            $output | Should -Match "Line 4"
            $output | Should -Match "Line 5"
        }

        It "ギャップ2行以上の範囲が分離される（空行で分離）" {
            $content = 1..20 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            # 3行目と10行目にマッチ（ギャップ6行）
            $newContent = $content.Clone()
            $newContent[2] = "Line 3: match"
            $newContent[9] = "Line 10: match"
            Set-Content -Path $script:testFile -Value $newContent -Encoding UTF8
            
            $output = Update-MatchInFile -Path $script:testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # ギャップが空行で分離されている（Line 5 と Line 8 の間に空行がある）
            $output | Should -Match "5-.*\n\s*\n\s*8-"
        }
    }
}
