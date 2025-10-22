Describe "Update-MatchInFile - Additional Edge Cases" {
    BeforeAll {
        $script:testDir = Join-Path ([System.IO.Path]::GetTempPath()) "PSMCPTests_$(Get-Random)"
        New-Item -Path $script:testDir -ItemType Directory -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context "マッチしない場合の動作" {
        It "Pattern がマッチしない場合、ファイルは変更されない" {
            $testFile = Join-Path $script:testDir "no-match.txt"
            $originalContent = @("Line 1", "Line 2", "Line 3")
            Set-Content -Path $testFile -Value $originalContent
            
            Update-MatchInFile -Path $testFile -Pattern "NonExistent" -Replacement "New"
            
            $content = Get-Content $testFile
            $content[0] | Should -Be "Line 1"
            $content[1] | Should -Be "Line 2"
            $content[2] | Should -Be "Line 3"
        }
    }

    Context "Replacement が特殊な値" {
        It "Replacement が空文字列（マッチ部分を削除）" {
            $testFile = Join-Path $script:testDir "empty-replacement.txt"
            Set-Content -Path $testFile -Value "Remove this word from the sentence"
            
            Update-MatchInFile -Path $testFile -Pattern "this word " -Replacement ""
            
            $content = Get-Content $testFile
            $content | Should -Be "Remove from the sentence"
        }
    }

    Context "キャプチャグループを使った置換" {
        It "キャプチャグループで順序を入れ替え" {
            $testFile = Join-Path $script:testDir "capture-group.txt"
            Set-Content -Path $testFile -Value "FirstName LastName"
            
            Update-MatchInFile -Path $testFile -Pattern '(\w+) (\w+)' -Replacement '$2, $1'
            
            $content = Get-Content $testFile
            $content | Should -Be "LastName, FirstName"
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルへの置換でエラー" {
            $nonExistentFile = Join-Path $script:testDir "nonexistent.txt"
            
            { Update-MatchInFile -Path $nonExistentFile -Pattern "test" -Replacement "new" -ErrorAction Stop } |
                Should -Throw
        }
    }
}
