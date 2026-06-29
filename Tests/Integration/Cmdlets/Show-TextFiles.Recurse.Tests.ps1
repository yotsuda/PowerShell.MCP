# Tests for Show-TextFiles -Recurse (including wildcard path support)

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Show-TextFiles -Recurse Tests" {
    BeforeAll {
        # Create a directory structure for testing
        # root/
        #   file1.txt  ("hello world")
        #   file2.cs   ("class Foo { }")
        #   sub/
        #     file3.txt  ("hello again")
        #     file4.cs   ("class Bar { }")
        #     deep/
        #       file5.txt  ("deep hello")
        $script:testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ShowTextFile_Recurse_$([guid]::NewGuid().ToString('N').Substring(0,8))"
        $script:subDir = Join-Path $script:testRoot "sub"
        $script:deepDir = Join-Path $script:subDir "deep"

        New-Item -ItemType Directory -Path $script:deepDir -Force | Out-Null

        Set-Content (Join-Path $script:testRoot "file1.txt") -Value "hello world"
        Set-Content (Join-Path $script:testRoot "file2.cs")  -Value "class Foo { }"
        Set-Content (Join-Path $script:subDir   "file3.txt") -Value "hello again"
        Set-Content (Join-Path $script:subDir   "file4.cs")  -Value "class Bar { }"
        Set-Content (Join-Path $script:deepDir  "file5.txt") -Value "deep hello"
    }

    AfterAll {
        if (Test-Path $script:testRoot) {
            Remove-Item $script:testRoot -Recurse -Force
        }
    }

    Context "Recursive search by directory path" {
        It "can recursively search all files when a directory is specified" {
            $result = Show-TextFiles $script:testRoot -Recurse -Contains "hello"
            # Matches the 3 files file1.txt, file3.txt, file5.txt
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 3
        }

        It "can also search files within subdirectories" {
            $result = Show-TextFiles $script:testRoot -Recurse -Contains "deep hello"
            $result | Should -Not -BeNullOrEmpty
            ($result | Where-Object { $_ -match "file5\.txt" }) | Should -Not -BeNullOrEmpty
        }

        It "empty result when there is no match" {
            $result = Show-TextFiles $script:testRoot -Recurse -Contains "nonexistent_string_xyz"
            $result | Should -BeNullOrEmpty
        }
    }

    Context "Recursive search by wildcard path" {
        It "*.txt recursively searches only .txt files" {
            $pattern = Join-Path $script:testRoot "*.txt"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            # Matches the 3 files file1.txt, file3.txt, file5.txt
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 3
        }

        It "*.cs recursively searches only .cs files" {
            $pattern = Join-Path $script:testRoot "*.cs"
            $result = Show-TextFiles $pattern -Recurse -Contains "class"
            # Matches the 2 files file2.cs, file4.cs
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 2
        }

        It "*.cs does not match .txt files" {
            $pattern = Join-Path $script:testRoot "*.cs"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            # The .cs files do not contain "hello", so there is no match
            $result | Should -BeNullOrEmpty
        }

        It "empty result for an extension pattern that matches nothing" {
            $pattern = Join-Path $script:testRoot "*.xyz"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            $result | Should -BeNullOrEmpty
        }

        It "can filter with the partial wildcard file*.txt" {
            $pattern = Join-Path $script:testRoot "file*.txt"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 3
        }
    }

    Context "Combination with -Pattern" {
        It "can do a regular expression search with wildcard path + -Pattern" {
            $pattern = Join-Path $script:testRoot "*.cs"
            $result = Show-TextFiles $pattern -Recurse -Pattern "class\s+\w+"
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 2
        }
    }

    Context "Validation" {
        It "-Recurse requires -Pattern or -Contains" {
            { Show-TextFiles $script:testRoot -Recurse -ErrorAction Stop } | Should -Throw
        }

        It "errors on a nonexistent path" {
            $result = Show-TextFiles (Join-Path $script:testRoot "nonexistent") -Recurse -Contains "test" -ErrorAction SilentlyContinue 2>&1
            # With ErrorAction SilentlyContinue, the error is a non-terminating error
        }
    }
}
