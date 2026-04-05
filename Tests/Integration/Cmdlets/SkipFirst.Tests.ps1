# SkipFirst.Tests.ps1
# -Skip/-First compatibility tests for all cmdlets that support -LineRange

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Show-TextFiles -Skip/-First" {
    BeforeAll {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:lines = 1..20 | ForEach-Object { "Line $_" }
        Set-Content -Path $script:testFile -Value $script:lines -Encoding UTF8
    }

    AfterAll {
        if (Test-Path $script:testFile) { Remove-Item $script:testFile -Force }
    }

    Context "Compatibility mapping" {
        It "-First 5 returns first 5 lines" {
            $result = Show-TextFiles -Path $script:testFile -First 5
            $result.Count | Should -Be 6  # header + 5 lines
            $result[1] | Should -BeLike "*1:*Line 1*"
            $result[5] | Should -BeLike "*5:*Line 5*"
        }

        It "-Skip 10 -First 5 returns lines 11-15" {
            $result = Show-TextFiles -Path $script:testFile -Skip 10 -First 5
            $result.Count | Should -Be 6
            $result[1] | Should -BeLike "* 11:*Line 11*"
            $result[5] | Should -BeLike "* 15:*Line 15*"
        }

        It "-Skip 0 -First 3 is equivalent to -LineRange 1-3" {
            $result = Show-TextFiles -Path $script:testFile -Skip 0 -First 3
            $result.Count | Should -Be 4
            $result[1] | Should -BeLike "*1:*Line 1*"
            $result[3] | Should -BeLike "*3:*Line 3*"
        }

        It "-Skip 5 without -First returns lines 6-20" {
            $result = Show-TextFiles -Path $script:testFile -Skip 5
            $result.Count | Should -Be 16  # header + 15 lines
            $result[1] | Should -BeLike "*6:*Line 6*"
            $result[-1] | Should -BeLike "* 20:*Line 20*"
        }

        It "-Skip 0 without -First returns all lines" {
            $result = Show-TextFiles -Path $script:testFile -Skip 0
            $result.Count | Should -Be 21  # header + 20 lines
            $result[1] | Should -BeLike "*1:*Line 1*"
        }
    }

    Context "Error cases" {
        It "-Skip/-First with -LineRange throws" {
            { Show-TextFiles -Path $script:testFile -Skip 10 -First 5 -LineRange "1-10" } | Should -Throw "*Cannot use -Skip/-First together with -LineRange*"
        }

        It "-First 0 throws" {
            { Show-TextFiles -Path $script:testFile -First 0 } | Should -Throw "*-First must be a positive integer*"
        }

        It "-First negative throws" {
            { Show-TextFiles -Path $script:testFile -First -1 } | Should -Throw "*-First must be a positive integer*"
        }
    }
}

Describe "Remove-LinesFromFile -Skip/-First" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        1..10 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) { Remove-Item $script:testFile -Force }
    }

    Context "Compatibility mapping" {
        It "-First 3 removes lines 1-3" {
            Remove-LinesFromFile -Path $script:testFile -First 3
            $content = Get-Content $script:testFile
            $content.Count | Should -Be 7
            $content[0] | Should -Be "Line 4"
        }

        It "-Skip 2 -First 3 removes lines 3-5" {
            Remove-LinesFromFile -Path $script:testFile -Skip 2 -First 3
            $content = Get-Content $script:testFile
            $content.Count | Should -Be 7
            $content[0] | Should -Be "Line 1"
            $content[1] | Should -Be "Line 2"
            $content[2] | Should -Be "Line 6"
        }

        It "-Skip 5 without -First removes lines 6-10" {
            Remove-LinesFromFile -Path $script:testFile -Skip 5
            $content = Get-Content $script:testFile
            $content.Count | Should -Be 5
            $content[0] | Should -Be "Line 1"
            $content[4] | Should -Be "Line 5"
        }
    }

    Context "Error cases" {
        It "-Skip/-First with -LineRange throws" {
            { Remove-LinesFromFile -Path $script:testFile -Skip 0 -First 3 -LineRange "1-3" } | Should -Throw "*Cannot use -Skip/-First together with -LineRange*"
        }
    }
}

Describe "Update-LinesInFile -Skip/-First" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        1..10 | ForEach-Object { "Line $_" } | Set-Content -Path $script:testFile -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) { Remove-Item $script:testFile -Force }
    }

    Context "Compatibility mapping" {
        It "-First 2 -Content replaces lines 1-2" {
            Update-LinesInFile -Path $script:testFile -First 2 -Content "New 1", "New 2"
            $content = Get-Content $script:testFile
            $content[0] | Should -Be "New 1"
            $content[1] | Should -Be "New 2"
            $content[2] | Should -Be "Line 3"
        }

        It "-Skip 3 -First 2 -Content replaces lines 4-5" {
            Update-LinesInFile -Path $script:testFile -Skip 3 -First 2 -Content "A", "B"
            $content = Get-Content $script:testFile
            $content[2] | Should -Be "Line 3"
            $content[3] | Should -Be "A"
            $content[4] | Should -Be "B"
            $content[5] | Should -Be "Line 6"
        }

        It "-Skip 8 without -First replaces lines 9-10" {
            Update-LinesInFile -Path $script:testFile -Skip 8 -Content "New 9", "New 10"
            $content = Get-Content $script:testFile
            $content[7] | Should -Be "Line 8"
            $content[8] | Should -Be "New 9"
            $content[9] | Should -Be "New 10"
        }
    }

    Context "Error cases" {
        It "-Skip/-First with -LineRange throws" {
            { Update-LinesInFile -Path $script:testFile -Skip 0 -First 3 -LineRange "1-3" -Content "x" } | Should -Throw "*Cannot use -Skip/-First together with -LineRange*"
        }
    }
}

Describe "Update-MatchInFile -Skip/-First" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        @(
            "Port: 8080"
            "Host: localhost"
            "Port: 9090"
            "Debug: true"
            "Port: 3000"
        ) | Set-Content -Path $script:testFile -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) { Remove-Item $script:testFile -Force }
    }

    Context "Compatibility mapping" {
        It "-First 3 limits replacement to lines 1-3" {
            Update-MatchInFile -Path $script:testFile -OldText "Port" -Replacement "Listen" -First 3
            $content = Get-Content $script:testFile
            $content[0] | Should -Be "Listen: 8080"
            $content[2] | Should -Be "Listen: 9090"
            $content[4] | Should -Be "Port: 3000"  # line 5 - outside range
        }

        It "-Skip 2 -First 2 limits replacement to lines 3-4" {
            Update-MatchInFile -Path $script:testFile -OldText "Port" -Replacement "Listen" -Skip 2 -First 2
            $content = Get-Content $script:testFile
            $content[0] | Should -Be "Port: 8080"   # line 1 - outside range
            $content[2] | Should -Be "Listen: 9090"  # line 3 - in range
            $content[4] | Should -Be "Port: 3000"    # line 5 - outside range
        }

        It "-Skip 2 without -First replaces from line 3 onward" {
            Update-MatchInFile -Path $script:testFile -OldText "Port" -Replacement "Listen" -Skip 2
            $content = Get-Content $script:testFile
            $content[0] | Should -Be "Port: 8080"    # line 1 - outside range
            $content[2] | Should -Be "Listen: 9090"   # line 3 - in range
            $content[4] | Should -Be "Listen: 3000"   # line 5 - in range
        }
    }

    Context "Error cases" {
        It "-Skip/-First with -LineRange throws" {
            { Update-MatchInFile -Path $script:testFile -OldText "x" -Replacement "y" -Skip 0 -First 3 -LineRange "1-3" } | Should -Throw "*Cannot use -Skip/-First together with -LineRange*"
        }
    }
}
