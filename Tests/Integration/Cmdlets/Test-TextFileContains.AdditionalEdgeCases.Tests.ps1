Describe "Test-TextFileContains - Additional Edge Cases" {
    BeforeAll {
        $script:testDir = Join-Path ([System.IO.Path]::GetTempPath()) "PSMCPTests_$(Get-Random)"
        New-Item -Path $script:testDir -ItemType Directory -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context "Boolean 戻り値の確認" {
        It "マッチする場合に True を返す" {
            $testFile = Join-Path $script:testDir "match-true.txt"
            Set-Content -Path $testFile -Value "This contains target word"
            
            $result = Test-TextFileContains -Path $testFile -Contains "target"
            
            $result | Should -BeOfType [bool]
            $result | Should -Be $true
        }

        It "マッチしない場合に False を返す" {
            $testFile = Join-Path $script:testDir "match-false.txt"
            Set-Content -Path $testFile -Value "This does not contain it"
            
            $result = Test-TextFileContains -Path $testFile -Contains "nonexistent"
            
            $result | Should -BeOfType [bool]
            $result | Should -Be $false
        }
    }

    Context "空ファイルでの検索" {
        It "空ファイルは常に False を返す" {
            $testFile = Join-Path $script:testDir "empty.txt"
            New-Item -Path $testFile -ItemType File -Force | Out-Null
            
            $result = Test-TextFileContains -Path $testFile -Contains "anything"
            
            $result | Should -Be $false
        }
    }

    Context "LineRange を使った範囲指定検索" {
        It "範囲内にマッチがある場合に True" {
            $testFile = Join-Path $script:testDir "range-match.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Target is here", "Line 3", "Line 4", "Line 5")
            
            $result = Test-TextFileContains -Path $testFile -LineRange 1,3 -Contains "Target"
            
            $result | Should -Be $true
        }

        It "範囲外にマッチがある場合に False" {
            $testFile = Join-Path $script:testDir "range-no-match.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3", "Target is here", "Line 5")
            
            $result = Test-TextFileContains -Path $testFile -LineRange 1,3 -Contains "Target"
            
            $result | Should -Be $false
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルでエラー" {
            $nonExistentFile = Join-Path $script:testDir "nonexistent.txt"
            
            { Test-TextFileContains -Path $nonExistentFile -Contains "test" -ErrorAction Stop } |
                Should -Throw
        }
    }
}
