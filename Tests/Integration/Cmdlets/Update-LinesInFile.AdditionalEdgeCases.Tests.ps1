Describe "Update-LinesInFile - Additional Edge Cases" {
    BeforeAll {
        $script:testDir = Join-Path ([System.IO.Path]::GetTempPath()) "PSMCPTests_$(Get-Random)"
        New-Item -Path $script:testDir -ItemType Directory -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context "LineRange の境界値テスト" {
        It "LineRange で最終行のみを更新" {
            $testFile = Join-Path $script:testDir "boundary2.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")
            
            Update-LinesInFile -Path $testFile -LineRange 3,3 -Content "Updated Line 3"
            
            $content = Get-Content $testFile
            $content[2] | Should -Be "Updated Line 3"
        }

        It "LineRange が -1（末尾）を使用" {
            $testFile = Join-Path $script:testDir "boundary3.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5")
            
            Update-LinesInFile -Path $testFile -LineRange 3,-1 -Content @("New 3", "New 4", "New 5")
            
            $content = Get-Content $testFile
            $content.Count | Should -Be 5
            $content[2] | Should -Be "New 3"
            $content[4] | Should -Be "New 5"
        }
    }

    Context "行の削除" {
        It "Content 省略時はエラーになる" {
            $testFile = Join-Path $script:testDir "delete-error.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            { Update-LinesInFile -Path $testFile -LineRange 2,2 } | Should -Throw "*Content is required*"
        }

        It "-Content @() で指定範囲の行を削除できる" {
            $testFile = Join-Path $script:testDir "delete1.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5")

            Update-LinesInFile -Path $testFile -LineRange 2,4 -Content @()

            $content = Get-Content $testFile
            $content.Count | Should -Be 2
            $content[0] | Should -Be "Line 1"
            $content[1] | Should -Be "Line 5"
        }
    }

    Context "空文字列での置換" {
        It "-Content `"`" で空行に置換できる" {
            $testFile = Join-Path $script:testDir "empty-string.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")

            Update-LinesInFile -Path $testFile -LineRange 2,2 -Content ""

            $content = Get-Content $testFile
            $content.Count | Should -Be 3
            $content[0] | Should -Be "Line 1"
            $content[1] | Should -Be ""
            $content[2] | Should -Be "Line 3"
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルへの更新でエラー" {
            $nonExistentFile = Join-Path $script:testDir "nonexistent.txt"
            
            { Update-LinesInFile -Path $nonExistentFile -LineRange 1,1 -Content "New" -ErrorAction Stop } |
                Should -Throw
        }
    }
}
