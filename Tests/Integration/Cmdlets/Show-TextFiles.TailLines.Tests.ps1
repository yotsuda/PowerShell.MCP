Describe "Show-TextFiles Tail Lines Feature" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "ShowTextFile_TailTest_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force
        }
    }

    BeforeEach {
        $script:testFile = Join-Path $script:testDir "test_$(Get-Random).txt"
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    Context "Negative LineRange (Tail Lines)" {
        It "-LineRange -5 で末尾5行を表示" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -5
            
            $output | Should -HaveCount 6  # header + 5 lines
            $output[1] | Should -Match "^\s*16:"
            $output[5] | Should -Match "^\s*20:"
        }

        It "-LineRange -10 で末尾10行を表示" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -10
            
            $output | Should -HaveCount 11  # header + 10 lines
            $output[1] | Should -Match "^\s*11:"
            $output[10] | Should -Match "^\s*20:"
        }

        It "-LineRange -10,-1 で末尾10行を表示（明示的）" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -10,-1
            
            $output | Should -HaveCount 11  # header + 10 lines
            $output[1] | Should -Match "^\s*11:"
            $output[10] | Should -Match "^\s*20:"
        }

        It "ファイル行数より多い末尾行数を指定すると全行表示" {
            1..5 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -10
            
            $output | Should -HaveCount 6  # header + 5 lines
            $output[1] | Should -Match "^\s*1:"
            $output[5] | Should -Match "^\s*5:"
        }

        It "-LineRange -1 で最後の1行のみ表示" {
            1..10 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -1
            
            $output | Should -HaveCount 2  # header + 1 line
            $output[1] | Should -Match "^\s*10:.*Line 10"
        }

        It "行番号が正しく表示される" {
            1..100 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -3
            
            $output[1] | Should -Match "^\s*98:"
            $output[2] | Should -Match "^\s*99:"
            $output[3] | Should -Match "100:"
        }
    }

    Context "Positive LineRange with -1 End (To End of File)" {
        It "-LineRange 15,-1 で15行目から末尾まで表示" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 15,-1
            
            $output | Should -HaveCount 7  # header + 6 lines (15-20)
            $output[1] | Should -Match "^\s*15:"
            $output[6] | Should -Match "^\s*20:"
        }

        It "-LineRange 1,-1 で全行表示" {
            1..5 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 1,-1
            
            $output | Should -HaveCount 6  # header + 5 lines
        }

        It "-LineRange 10,0 で10行目から末尾まで表示（0も末尾を意味する）" {
            1..15 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 10,0
            
            $output | Should -HaveCount 7  # header + 6 lines (10-15)
            $output[1] | Should -Match "^\s*10:"
            $output[6] | Should -Match "^\s*15:"
        }
    }

    Context "Normal LineRange Still Works" {
        It "-LineRange 5,10 で5-10行目を表示" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 5,10
            
            $output | Should -HaveCount 7  # header + 6 lines
            $output[1] | Should -Match "^\s*5:"
            $output[6] | Should -Match "^\s*10:"
        }

        It "-LineRange 1,5 で先頭5行を表示" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 1,5
            
            $output | Should -HaveCount 6  # header + 5 lines
            $output[1] | Should -Match "^\s*1:"
            $output[5] | Should -Match "^\s*5:"
        }

        It "LineRange なしで全行表示" {
            1..5 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile
            
            $output | Should -HaveCount 6  # header + 5 lines
        }
    }

    Context "Edge Cases" {
        It "0バイト空ファイルで末尾行を要求すると警告が出る" {
            # 0バイトの空ファイルを作成
            [System.IO.File]::WriteAllBytes($script:testFile, @())
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -5 -WarningVariable warn 3>$null
            
            # 空ファイルの警告が出るはず（ProcessRecord で File is empty）
            $warn | Should -Not -BeNullOrEmpty
        }

        It "1行ファイルで末尾5行を要求" {
            "Only line" | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -5
            
            $output | Should -HaveCount 2  # header + 1 line
            $output[1] | Should -Match "^\s*1:.*Only line"
        }
    }
}
