# Update-MatchInFile.Tests.ps1
# Integration tests for the Update-MatchInFile cmdlet

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-MatchInFile Integration Tests" {
    BeforeEach {
        # Create a new temp file before each test
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "Server: localhost"
            "Port: 8080"
            "Username: admin"
            "Password: secret123"
            "Debug: true"
            "Timeout: 30"
            "MaxRetries: 3"
        )
        Set-Content -Path $script:testFile -Value $script:initialContent -Encoding UTF8
    }

    AfterEach {
        # Clean up after each test
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        # Clean up backup files too
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force
    }

    Context "Replacement by text match (Contains)" {
        It "can replace the portion containing the specified text" {
            Update-MatchInFile -Path $script:testFile -OldText "localhost" -Replacement "production.example.com"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Server: production.example.com"
        }

        It "when multiple lines match, all of them are replaced" {
            Update-MatchInFile -Path $script:testFile -OldText "true" -Replacement "false"
            $result = Get-Content $script:testFile
            $result[4] | Should -Be "Debug: false"
        }

        It "when there is no match, nothing is changed" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -OldText "NonExistentText" -Replacement "NewValue"
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }

        It "is case-sensitive" {
            Update-MatchInFile -Path $script:testFile -OldText "LOCALHOST" -Replacement "server.local"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Server: localhost"  # Not changed because it does not match the uppercase text
        }
    }

    Context "Replacement by regular expression (Pattern)" {
        It "can replace the portion matching a regular expression pattern" {
            Update-MatchInFile -Path $script:testFile -Pattern "\d+" -Replacement "9999"
            $result = Get-Content $script:testFile
            # All numbers are replaced with 9999
            $result[1] | Should -Be "Port: 9999"
            $result[5] | Should -Be "Timeout: 9999"
            $result[6] | Should -Be "MaxRetries: 9999"
        }

        It "can replace using a capture group" {
            Update-MatchInFile -Path $script:testFile -Pattern 'Port: (\d+)' -Replacement 'Port: $1$1'
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 80808080"
        }

        It "can use a complex regular expression pattern" {
            Update-MatchInFile -Path $script:testFile -Pattern "Password: \w+" -Replacement "Password: ********"
            $result = Get-Content $script:testFile
            $result[3] | Should -Be "Password: ********"
        }

        It "can replace an entire line" {
            Update-MatchInFile -Path $script:testFile -Pattern "^Debug: true$" -Replacement "Debug: false"
            $result = Get-Content $script:testFile
            $result[4] | Should -Be "Debug: false"
        }

        It "replaces all matches" {
            Set-Content -Path $script:testFile -Value "AAA BBB AAA CCC AAA" -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern "AAA" -Replacement "XXX"
            $result = Get-Content $script:testFile
            $result | Should -Be "XXX BBB XXX CCC XXX"
        }
    }

    Context "Replacement within a range" {
        It "can combine LineRange and Contains" {
            Update-MatchInFile -Path $script:testFile -LineRange 1,3 -OldText "admin" -Replacement "superuser"
            $result = Get-Content $script:testFile
            $result[2] | Should -Be "Username: superuser"
            # Outside the range is not changed
        }

        It "can combine LineRange and Pattern" {
            Update-MatchInFile -Path $script:testFile -LineRange 2,4 -Pattern "\d+" -Replacement "9999"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 9999"  # Within the range
            $result[5] | Should -Be "Timeout: 30"  # Outside the range, so not changed
        }

        It "replaces only the first line of the range" {
            Update-MatchInFile -Path $script:testFile -LineRange 1,1 -Pattern "localhost" -Replacement "newhost"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Server: newhost"
        }

        It "replaces only the last line of the range" {
            Update-MatchInFile -Path $script:testFile -LineRange 7,7 -Pattern "\d+" -Replacement "10"
            $result = Get-Content $script:testFile
            $result[6] | Should -Be "MaxRetries: 10"
        }
    }

    Context "Configuration file update scenarios" {
        It "can update a configuration value" {
            Update-MatchInFile -Path $script:testFile -Pattern "Port: \d+" -Replacement "Port: 3000"
            Update-MatchInFile -Path $script:testFile -Pattern "Debug: \w+" -Replacement "Debug: false"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 3000"
            $result[4] | Should -Be "Debug: false"
        }

        It "updates multiple settings at once (pipeline)" {
            Update-MatchInFile -Path $script:testFile -Pattern "8080" -Replacement "9090"
            Update-MatchInFile -Path $script:testFile -Pattern "30" -Replacement "60"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 9090"
            $result[5] | Should -Be "Timeout: 60"
        }
    }

    Context "Handling special characters" {
        It "can replace text containing special characters" {
            Set-Content -Path $script:testFile -Value @("Price: $100", "Discount: 10%") -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -OldText '$100' -Replacement '$200'
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Price: $200"
        }

        It "uses regular expression special characters with escaping" {
            Set-Content -Path $script:testFile -Value "Email: user@example.com" -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern "@" -Replacement "[at]"
            $result = Get-Content $script:testFile
            $result | Should -Be "Email: user[at]example.com"
        }
    }

    Context "Encoding" {
        It "can process a UTF-8 file correctly" {
            $content = "設定: 日本語"
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -OldText "日本語" -Replacement "English" -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result | Should -Be "設定: English"
        }

        It "matches a Japanese regular expression" {
            Set-Content -Path $script:testFile -Value @("名前: 太郎", "年齢: 25") -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern "太郎" -Replacement "花子" -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result[0] | Should -Be "名前: 花子"
        }
    }

    Context "Backup feature" {
        It "creates a backup file when -Backup is specified" {
            Update-MatchInFile -Path $script:testFile -OldText "localhost" -Replacement "newhost" -Backup
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak"
            $backupFiles.Count | Should -BeGreaterThan 0
        }

        It "saves the original content in the backup file" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -OldText "localhost" -Replacement "newhost" -Backup
            $backupFile = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak" | Select-Object -First 1
            $backupContent = Get-Content $backupFile.FullName
            $backupContent | Should -Be $originalContent
        }
    }

    Context "WhatIf and Confirm" {
        It "does not actually change the file when -WhatIf is specified" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -OldText "localhost" -Replacement "newhost" -WhatIf
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "Error handling" {
        It "errors on a nonexistent file" {
            { Update-MatchInFile -Path "C:\NonExistent\file.txt" -OldText "test" -Replacement "new" -ErrorAction Stop } | 
                Should -Throw
        }

        It "errors when Contains and Pattern are specified at the same time" {
            { Update-MatchInFile -Path $script:testFile -OldText "test" -Pattern "test" -Replacement "new" -ErrorAction Stop } | 
                Should -Throw
        }

        It "errors when Replacement is not specified" {
            { Update-MatchInFile -Path $script:testFile -OldText "test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "errors on an invalid regular expression" {
            { Update-MatchInFile -Path $script:testFile -Pattern "[invalid(" -Replacement "new" -ErrorAction Stop } | 
                Should -Throw
        }
    }

    Context "Pipeline input" {
        It "can process multiple files from the pipeline" {
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value "Server: localhost"
            
            try {
                Get-Item @($script:testFile, $file2) | Update-MatchInFile -OldText "localhost" -Replacement "production"
                $result1 = Get-Content $script:testFile
                $result2 = Get-Content $file2
                $result1[0] | Should -Be "Server: production"
                $result2 | Should -Be "Server: production"
            }
            finally {
                Remove-Item $file2 -Force -ErrorAction SilentlyContinue
            }
        }

    Context "Deletion via an empty string (Empty Replacement)" {
        It "can delete matched text with Contains + an empty string" {
            Set-Content -Path $script:testFile -Value "Server=localhost:8080" -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -OldText ":8080" -Replacement ""
            $result = Get-Content $script:testFile -Raw
            $result.Trim() | Should -Be "Server=localhost"
        }

        It "can delete matched text with Pattern + an empty string" {
            Set-Content -Path $script:testFile -Value 'Price: $99.99 (tax included)' -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern '\$[\d.]+\s*' -Replacement ""
            $result = Get-Content $script:testFile -Raw
            $result.Trim() | Should -Be "Price: (tax included)"
        }

        It "can delete a specific pattern from multiple lines" {
            $content = @(
                "Error: Failed to connect"
                "Warning: Timeout occurred"
                "Info: Process completed"
            )
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            # Delete "Error: " and "Warning: "
            Update-MatchInFile -Path $script:testFile -Pattern '^(Error|Warning): ' -Replacement ""
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Failed to connect"
            $result[1] | Should -Be "Timeout occurred"
            $result[2] | Should -Be "Info: Process completed"
        }

        It "can delete the protocol portion from a URL" {
            Set-Content -Path $script:testFile -Value "https://example.com/path" -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern '^https?://' -Replacement ""
            $result = Get-Content $script:testFile -Raw
            $result.Trim() | Should -Be "example.com/path"
        }

        It "preserves encoding after empty-string deletion" {
            $content = "日本語テキスト:削除対象"
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -OldText ":削除対象" -Replacement ""
            $result = Get-Content $script:testFile -Raw -Encoding UTF8
            $result.Trim() | Should -Be "日本語テキスト"
        }
    }

    Context "Validation of the Replacement parameter" {
        It "errors with Contains when Replacement is not specified (null)" {
            { Update-MatchInFile -Path $script:testFile -OldText "test" } | Should -Throw
        }

        It "errors with Pattern when Replacement is not specified (null)" {
            { Update-MatchInFile -Path $script:testFile -Pattern '\d+' } | Should -Throw
        }

        It "does not error when Replacement is an empty string (behaves as deletion)" {
            Set-Content -Path $script:testFile -Value "Test123" -Encoding UTF8
            { Update-MatchInFile -Path $script:testFile -OldText "123" -Replacement "" } | 
                Should -Not -Throw
            $result = Get-Content $script:testFile -Raw
            $result.Trim() | Should -Be "Test"
        }
    }

    Context "Parameter Aliases" {
        It "-NewText alias works for -Replacement" {
            Update-MatchInFile -Path $script:testFile -OldText "localhost" -NewText "production.example.com"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Server: production.example.com"
        }

        It "-NewText alias works with -Pattern" {
            Update-MatchInFile -Path $script:testFile -Pattern "\d+" -NewText "9999" -LineRange 2
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 9999"
        }

        It "-NewText alias works with multiline OldText" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                @"
Block A
Block B
Block C
"@ | Set-Content $testFile
                Update-MatchInFile -Path $testFile -OldText "Block A`nBlock B" -NewText "Merged"
                $result = Get-Content $testFile -Raw
                $result | Should -Match "Merged"
                $result | Should -Not -Match "Block A"
                $result | Should -Not -Match "Block B"
            }
            finally {
                if (Test-Path $testFile) { Remove-Item $testFile -Force }
            }
        }
    }
    }
}
