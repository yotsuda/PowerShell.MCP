# Show-TextFile: LineRange with Pattern/Contains should display context lines
# Issue: When using -LineRange with -Pattern or -Contains, context lines are not displayed

Describe "Show-TextFile LineRange Context Display" {
    BeforeAll {
        $script:testFile = Join-Path $env:TEMP "test_linerange_context.txt"
        $content = @(
            "Line 1 no match"
            "Line 2 no match"
            "Line 3 no match"
            "Line 4 MATCH here"
            "Line 5 no match"
            "Line 6 no match"
            "Line 7 no match"
        )
        Set-Content -Path $script:testFile -Value $content -Encoding UTF8
    }

    AfterAll {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    Context "Pattern with LineRange should show context" {
        It "Should display context lines (before and after) when match is in LineRange" {
            # LineRange 4,4 を指定（4行目だけ）
            # 期待：2,3行目（前2行）と5,6行目（後2行）もコンテキストとして表示される
            $result = Show-TextFile -Path $script:testFile -Pattern "MATCH" -LineRange 4,4
            
            # ヘッダーを除外
            $contentLines = $result | Where-Object { $_ -notmatch "^==>" -and $_ -ne "" }
            
            # 結果を表示（デバッグ用）
            Write-Host "Result lines:"
            $contentLines | ForEach-Object { Write-Host "  $_" }
            
            # 期待される行数：2（前）+ 1（マッチ）+ 2（後）= 5行
            $contentLines.Count | Should -Be 5
            
            # コンテキスト行の確認
            $contentLines[0] | Should -Match "2- Line 2"  # 前2行
            $contentLines[1] | Should -Match "3- Line 3"  # 前1行
            $contentLines[2] | Should -Match "4:.*MATCH"  # マッチ行
            $contentLines[3] | Should -Match "5- Line 5"  # 後1行
            $contentLines[4] | Should -Match "6- Line 6"  # 後2行
        }
    }

    Context "Contains with LineRange should show context" {
        It "Should display context lines when match is in LineRange" {
            $result = Show-TextFile -Path $script:testFile -Contains "MATCH" -LineRange 4,4
            
            $contentLines = $result | Where-Object { $_ -notmatch "^==>" -and $_ -ne "" }
            
            # 期待される行数：2（前）+ 1（マッチ）+ 2（後）= 5行
            $contentLines.Count | Should -Be 5
            
            $contentLines[0] | Should -Match "2- Line 2"
            $contentLines[1] | Should -Match "3- Line 3"
            $contentLines[2] | Should -Match "4:.*MATCH"
            $contentLines[3] | Should -Match "5- Line 5"
            $contentLines[4] | Should -Match "6- Line 6"
        }
    }

    Context "Pattern without LineRange should show context (baseline)" {
        It "Should display context lines when LineRange is not specified" {
            $result = Show-TextFile -Path $script:testFile -Pattern "MATCH"
            
            $contentLines = $result | Where-Object { $_ -notmatch "^==>" -and $_ -ne "" }
            
            # LineRange なしではコンテキストが表示される（ベースライン確認）
            $contentLines.Count | Should -BeGreaterThan 1
            $contentLines | Where-Object { $_ -match "MATCH" } | Should -Not -BeNullOrEmpty
        }
    }
}
