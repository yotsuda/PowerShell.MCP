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
        It "-LineRange -5 displays the last 5 lines" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -5
            
            $output | Should -HaveCount 6  # header + 5 lines
            $output[1] | Should -Match "^\s*16:"
            $output[5] | Should -Match "^\s*20:"
        }

        It "-LineRange -10 displays the last 10 lines" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -10
            
            $output | Should -HaveCount 11  # header + 10 lines
            $output[1] | Should -Match "^\s*11:"
            $output[10] | Should -Match "^\s*20:"
        }

        It "-LineRange -10,-1 displays the last 10 lines (explicit)" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -10,-1
            
            $output | Should -HaveCount 11  # header + 10 lines
            $output[1] | Should -Match "^\s*11:"
            $output[10] | Should -Match "^\s*20:"
        }

        It "displays all lines when the requested tail count exceeds the file's line count" {
            1..5 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -10
            
            $output | Should -HaveCount 6  # header + 5 lines
            $output[1] | Should -Match "^\s*1:"
            $output[5] | Should -Match "^\s*5:"
        }

        It "-LineRange -1 displays only the last line" {
            1..10 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -1
            
            $output | Should -HaveCount 2  # header + 1 line
            $output[1] | Should -Match "^\s*10:.*Line 10"
        }

        It "displays line numbers correctly" {
            1..100 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -3
            
            $output[1] | Should -Match "^\s*98:"
            $output[2] | Should -Match "^\s*99:"
            $output[3] | Should -Match "100:"
        }
    }

    Context "Positive LineRange with -1 End (To End of File)" {
        It "-LineRange 15,-1 displays from line 15 to the end" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 15,-1
            
            $output | Should -HaveCount 7  # header + 6 lines (15-20)
            $output[1] | Should -Match "^\s*15:"
            $output[6] | Should -Match "^\s*20:"
        }

        It "-LineRange 1,-1 displays all lines" {
            1..5 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 1,-1
            
            $output | Should -HaveCount 6  # header + 5 lines
        }

        It "-LineRange 10,0 displays from line 10 to the end (0 also means the end)" {
            1..15 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 10,0
            
            $output | Should -HaveCount 7  # header + 6 lines (10-15)
            $output[1] | Should -Match "^\s*10:"
            $output[6] | Should -Match "^\s*15:"
        }
    }

    Context "Normal LineRange Still Works" {
        It "-LineRange 5,10 displays lines 5-10" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 5,10
            
            $output | Should -HaveCount 7  # header + 6 lines
            $output[1] | Should -Match "^\s*5:"
            $output[6] | Should -Match "^\s*10:"
        }

        It "-LineRange 1,5 displays the first 5 lines" {
            1..20 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange 1,5
            
            $output | Should -HaveCount 6  # header + 5 lines
            $output[1] | Should -Match "^\s*1:"
            $output[5] | Should -Match "^\s*5:"
        }

        It "displays all lines without LineRange" {
            1..5 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile
            
            $output | Should -HaveCount 6  # header + 5 lines
        }
    }

    Context "Edge Cases" {
        It "warns when requesting tail lines from a 0-byte empty file" {
            # Create a 0-byte empty file
            [System.IO.File]::WriteAllBytes($script:testFile, @())

            $output = Show-TextFiles -Path $script:testFile -LineRange -5 -WarningVariable warn 3>$null

            # An empty-file warning should be emitted (File is empty in ProcessRecord)
            $warn | Should -Not -BeNullOrEmpty
        }

        It "requests the last 5 lines from a single-line file" {
            "Only line" | Set-Content -Path $script:testFile -Encoding UTF8
            
            $output = Show-TextFiles -Path $script:testFile -LineRange -5
            
            $output | Should -HaveCount 2  # header + 1 line
            $output[1] | Should -Match "^\s*1:.*Only line"
        }
    }
}
