Describe "Show-TextFile - Additional Edge Cases" {
    BeforeAll {
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterAll {
        Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
    }

    Context "小さいファイルでのコンテキスト表示" {
        It "1行ファイルでマッチした場合、コンテキストなしで表示" {
            Set-Content -Path $script:testFile -Value "Target" -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Contains "Target"
            
            # ヘッダー + マッチ行のみ
            $result.Count | Should -Be 2
            $result[1] | Should -Match ":.*Target"
        }

        It "2行ファイルの1行目マッチ" {
            $content = @("Target Line 1", "Line 2")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Contains "Target"
            
            # ヘッダー + マッチ行 + 後コンテキスト1行
            $result.Count | Should -Be 3
        }

        It "2行ファイルの2行目マッチ" {
            $content = @("Line 1", "Target Line 2")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Contains "Target"
            
            # ヘッダー + 前コンテキスト1行 + マッチ行
            $result.Count | Should -Be 3
        }
    }

    Context "同一行内の複数マッチ" {
        It "Contains: 同一行内の複数マッチがすべてハイライトされる" {
            Set-Content -Path $script:testFile -Value "Test and Test and Test" -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Contains "Test"
            
            $matchLine = $result | Where-Object { $_ -match ":" }
            # 3箇所の "Test" がすべてハイライトされる
            $highlightCount = ([regex]::Matches($matchLine, "$([char]27)\[7m")).Count
            $highlightCount | Should -Be 3
        }

        It "Pattern: 同一行内の複数マッチがすべてハイライトされる" {
            Set-Content -Path $script:testFile -Value "abc123def456ghi789" -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Pattern '\d+'
            
            $matchLine = $result | Where-Object { $_ -match ":" }
            # 3箇所の数字がすべてハイライトされる
            $highlightCount = ([regex]::Matches($matchLine, "$([char]27)\[7m")).Count
            $highlightCount | Should -Be 3
        }
    }

    Context "範囲マージの境界条件" {
        It "マッチが正確に7行離れている場合、範囲がマージされる" {
            $content = 1..15 | ForEach-Object { "Line $_" }
            $content[0] = "Match Line 1"
            $content[7] = "Match Line 8"  # 7行離れている
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Contains "Match"
            
            # マージされるため空行なし
            $contentLines = $result | Select-Object -Skip 1
            $contentLines | Where-Object { $_ -eq "" } | Should -BeNullOrEmpty
        }

        It "マッチが8行以上離れている場合、範囲が分離される" {
            $content = 1..15 | ForEach-Object { "Line $_" }
            $content[0] = "Match Line 1"
            $content[8] = "Match Line 9"  # 8行離れている
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Contains "Match"
            
            # 分離されるため空行あり
            $contentLines = $result | Select-Object -Skip 1
            ($contentLines | Where-Object { $_ -eq "" }).Count | Should -BeGreaterThan 0
        }
    }

    Context "負のLineRange値のバリエーション" {
        It "異なる負の値（-1, -2, -99）はすべて同じ動作をする" {
            $content = 1..10 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $result1 = Show-TextFile -Path $script:testFile -LineRange 5,-1
            $result2 = Show-TextFile -Path $script:testFile -LineRange 5,-2
            $result3 = Show-TextFile -Path $script:testFile -LineRange 5,-99
            
            $result1.Count | Should -Be $result2.Count
            $result2.Count | Should -Be $result3.Count
        }

        It "0も負の値と同様に末尾を意味する" {
            $content = 1..10 | ForEach-Object { "Line $_" }
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            
            $result1 = Show-TextFile -Path $script:testFile -LineRange 5,-1
            $result2 = Show-TextFile -Path $script:testFile -LineRange 5,0
            
            $result1.Count | Should -Be $result2.Count
        }
    }

    Context "バリデーションエラー" $err = $null
            It "第1引数が負の値の場合はバリデーションエラーになる" {
            Set-Content -Path $script:testFile -Value "Test" -Encoding UTF8
            { Show-TextFile -Path $script:testFile -LineRange -1,5 -ErrorVariable err -ErrorAction SilentlyContinue
            $err.Count | Should -BeGreaterThan 0
            $err[0].Exception.Message | Should -BeLike "*Start line must be 1 or greater*"
        }

        It "第1引数が0の場合もバリデーションエラーになる" $err = $null
            Set-Content -Path $script:testFile -Value "Test" -Encoding UTF8
            { Show-TextFile -Path $script:testFile -LineRange 0,5 -ErrorVariable err -ErrorAction SilentlyContinue
            $err.Count | Should -BeGreaterThan 0
            $err[0].Exception.Message | Should -BeLike "*Start line must be 1 or greater*"
        }
    }

    Context "特殊なマッチケース" {
        It "空行がマッチする場合も正しく表示" {
            $content = @("Line 1", "", "Line 3")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Pattern '^$'
            
            # 空行がマッチとして表示される
            $result | Where-Object { $_ -match "^\s+2:" } | Should -Not -BeNullOrEmpty
        }

        It "非常に長い行でもハイライトが正しく適用される" {
            $longLine = "a" * 1000 + "TARGET" + "b" * 1000
            Set-Content -Path $script:testFile -Value $longLine -Encoding UTF8
            $result = Show-TextFile -Path $script:testFile -Contains "TARGET"
            
            $matchLine = $result | Where-Object { $_ -match "^\s+\d+:" }
            $matchLine | Should -Match "$([char]27)\[7mTARGET$([char]27)\[0m"
        }
    }

    Context "複数ファイルでの新機能" {
        It "複数ファイルでもコンテキスト表示が動作する" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            
            try {
                @("Line 1", "Target", "Line 3") | Set-Content $file1 -Encoding UTF8
                @("Line A", "Target", "Line C") | Set-Content $file2 -Encoding UTF8
                
                $result = Show-TextFile -Path $file1,$file2 -Contains "Target"
                
                # 両方のファイルでヘッダーとコンテキストが表示される
                ($result | Where-Object { $_ -match "^==>" }).Count | Should -Be 2
                ($result | Where-Object { $_ -match "^\s+\d+:" }).Count | Should -Be 2
            }
            finally {
                Remove-Item $file1,$file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
