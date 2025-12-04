# Additional Context Display Edge Case Tests
# v1.3.0以降のコンテキスト表示機能の追加エッジケーステスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Context Display Edge Case Tests" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "ContextEdgeCaseTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "ファイル境界でのコンテキスト表示" {
        It "ファイル先頭行のマッチでコンテキストが表示される" {
            $testFile = Join-Path $script:testDir "edge1.txt"
            $content = @("Line 1: match", "Line 2", "Line 3")
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # 先頭行でも後続2行のコンテキストが表示される
            $output | Should -Match "Line 2"
            $output | Should -Match "Line 3"
        }

        It "ファイル末尾行のマッチでコンテキストが表示される" {
            $testFile = Join-Path $script:testDir "edge2.txt"
            $content = @("Line 1", "Line 2", "Line 3: match")
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # 末尾行でも前方2行のコンテキストが表示される
            $output | Should -Match "Line 1"
            $output | Should -Match "Line 2"
        }

        It "1行ファイルでのマッチ" {
            $testFile = Join-Path $script:testDir "edge3.txt"
            Set-Content -Path $testFile -Value "Single line: match" -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # 1行でもコンテキスト表示が機能する
            $output | Should -Match "REPLACED"
        }
    }

    Context "複数行操作でのコンテキスト表示" {
        It "複数行の挿入でコンテキストが表示される" {
            $testFile = Join-Path $script:testDir "multi1.txt"
            $content = @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5")
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Add-LinesToFile -Path $testFile -LineNumber 3 -Content @("New A", "New B", "New C") -InformationAction Continue 6>&1
            
            # 複数行挿入時のコンテキスト
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
        }

        It "複数行の置換でコンテキストが表示される" {
            $testFile = Join-Path $script:testDir "multi2.txt"
            $content = 1..10 | ForEach-Object { "Line $_" }
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-LinesInFile -Path $testFile -LineRange 4,7 -Content @("New 4", "New 5", "New 6", "New 7") -InformationAction Continue 6>&1
            
            # 複数行置換時のコンテキスト
            $contextOutput = $output | Where-Object { $_ -match "^\s+\d+[:-]" }
            $contextOutput | Should -Not -BeNullOrEmpty
            $contextOutput.Count | Should -BeGreaterThan 4
        }
    }

    Context "LineRange指定でのコンテキスト表示" {
        It "LineRange内のみの置換でコンテキストが表示される" {
            $testFile = Join-Path $script:testDir "range1.txt"
            $content = 1..10 | ForEach-Object { "Line $_ - test" }
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -LineRange 3,7 -Contains "test" -Replacement "updated" -InformationAction Continue 6>&1 | Out-String
            
            # LineRange内のマッチのみ表示される
            $output | Should -Match "Line 3"
            $output | Should -Match "Line 7"
            # LineRange外は表示されない
            $output | Should -Not -Match "Line 1:"
            $output | Should -Not -Match "Line 10:"
        }
    }

    Context "空行やギャップ処理" {
        It "ギャップがちょうど1行の場合にマージされる" {
            $testFile = Join-Path $script:testDir "gap1.txt"
            $content = 1..15 | ForEach-Object { "Line $_" }
            $content[2] = "Line 3: match"
            $content[5] = "Line 6: match"
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # ギャップ1行なのでマージされる
            $output | Should -Match "Line 4"
            $output | Should -Match "Line 5"
            # 分離マーカー（空行）がない
            $output | Should -Not -Match "4-.*\n\s*\n"
        }

        It "ギャップが2行以上の場合に分離される" {
            $testFile = Join-Path $script:testDir "gap2.txt"
            $content = 1..20 | ForEach-Object { "Line $_" }
            $content[2] = "Line 3: match"
            $content[11] = "Line 12: match"  # ギャップは6,7,8,9,10,11行目の6行
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -Contains "match" -Replacement "REPLACED" -InformationAction Continue 6>&1 | Out-String
            
            # ギャップが大きいので分離される
            # 分離マーカー（空行）が含まれる
            $output | Should -Match "5-.*`r?`n\s*`r?`n"
        }
    }

    Context "特殊文字を含む置換でのコンテキスト表示" {
        It "特殊文字を含む置換でもコンテキスト表示される" {
            $testFile = Join-Path $script:testDir "special.txt"
            $content = @(
                "Line 1: normal"
                "Line 2: C:\path\to\file"
                "Line 3: normal"
            )
            Set-Content -Path $testFile -Value $content -Encoding UTF8
            
            $output = Update-MatchInFile -Path $testFile -Pattern 'C:\\path\\to\\file' -Replacement 'D:\new\path' -InformationAction Continue 6>&1 | Out-String
            
            # 特殊文字を含む置換でもコンテキスト表示が機能する
            $output | Should -Match "Line 2"
            $output | Should -Match '\x1b\[32m'  # 緑（置換後）- 通常実行時は置換テキストのみ表示
        }
    }
}