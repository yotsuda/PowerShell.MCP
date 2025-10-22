Describe "Add-LinesToFile - Additional Edge Cases" {
    BeforeAll {
        $script:testDir = Join-Path ([System.IO.Path]::GetTempPath()) "PSMCPTests_$(Get-Random)"
        New-Item -Path $script:testDir -ItemType Directory -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context "空ファイルへの操作" {
        It "空ファイルに行を追加できる" {
            $testFile = Join-Path $script:testDir "empty.txt"
            New-Item -Path $testFile -ItemType File -Force | Out-Null
            
            Add-LinesToFile -Path $testFile -Content "First line"
            
            $content = Get-Content $testFile -Raw
            $content | Should -Match "First line"
        }

        It "空ファイルへの複数行追加" {
            $testFile = Join-Path $script:testDir "empty2.txt"
            New-Item -Path $testFile -ItemType File -Force | Out-Null
            
            $lines = @("Line 1", "Line 2", "Line 3")
            Add-LinesToFile -Path $testFile -Content $lines
            
            $content = Get-Content $testFile
            $content.Count | Should -Be 3
            $content[0] | Should -Be "Line 1"
            $content[2] | Should -Be "Line 3"
        }
    }

    Context "LineNumber の境界値テスト" {
        It "LineNumber が総行数+1（末尾への追加として処理される）" {
            $testFile = Join-Path $script:testDir "boundary1.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")
            
            Add-LinesToFile -Path $testFile -LineNumber 4 -Content "Line 4"
            
            $content = Get-Content $testFile
            $content.Count | Should -Be 4
            $content[3] | Should -Be "Line 4"
        }

        It "LineNumber 1 で先頭に挿入" {
            $testFile = Join-Path $script:testDir "boundary3.txt"
            Set-Content -Path $testFile -Value @("Line 2", "Line 3")
            
            Add-LinesToFile -Path $testFile -LineNumber 1 -Content "Line 1"
            
            $content = Get-Content $testFile
            $content[0] | Should -Be "Line 1"
            $content[1] | Should -Be "Line 2"
        }
    }

    Context "Content パラメータの特殊ケース" {
        It "空配列を渡すとパラメータバインディングエラー" {
            $testFile = Join-Path $script:testDir "empty-array.txt"
            Set-Content -Path $testFile -Value "Original"
            
            # 空配列はContentパラメータに渡せない
            { Add-LinesToFile -Path $testFile -Content @() -ErrorAction Stop } |
                Should -Throw
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルへの追加でエラー" {
            $nonExistentFile = Join-Path $script:testDir "nonexistent.txt"
            
            # Add-LinesToFile は存在しないファイルに対してエラーを出す
            { Add-LinesToFile -Path $nonExistentFile -Content "Line 1" -ErrorAction Stop } |
                Should -Throw "*File not found*"
        }
    }
}
