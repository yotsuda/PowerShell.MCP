# Blank Line Separation Tests
# コンテキスト行とサマリ行の間に空行が挿入されることをテストする

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Blank Line Separation Between Context and Summary" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "BlankLineSeparationTests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item -Path $script:testDir -Recurse -Force
        }
    }

    Context "Add-LinesToFile" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "add-test.txt"
            Set-Content -Path $script:testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5") -Encoding UTF8
        }

        It "3行目への挿入時、コンテキストとサマリの間に空行がある" {
            $output = Add-LinesToFile -Path $script:testFile -LineNumber 3 -Content "Inserted" 6>&1 | Out-String
            
            $lines = $output -split "`r?`n"
            
            $lastContextIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^\s+\d+[-:]') {
                    $lastContextIndex = $i
                }
            }
            
            $summaryIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^.*Added \d+ line\(s\)') {
                    $summaryIndex = $i
                    break
                }
            }
            
            $lastContextIndex | Should -BeGreaterThan -1
            $summaryIndex | Should -BeGreaterThan -1
            $summaryIndex | Should -BeGreaterThan $lastContextIndex
            $lines[$lastContextIndex + 1] | Should -Match '^\s*$'
        }
    }

    Context "Update-LinesInFile" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "update-test.txt"
            Set-Content -Path $script:testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5") -Encoding UTF8
        }

        It "行の更新時、コンテキストとサマリの間に空行がある" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 3,4 -Content @("Updated 3", "Updated 4") 6>&1 | Out-String
            
            $lines = $output -split "`r?`n"
            
            $lastContextIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^\s+\d+[-:]') {
                    $lastContextIndex = $i
                }
            }
            
            $summaryIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^.*Updated .+: Replaced') {
                    $summaryIndex = $i
                    break
                }
            }
            
            $lastContextIndex | Should -BeGreaterThan -1
            $summaryIndex | Should -BeGreaterThan -1
            $lines[$lastContextIndex + 1] | Should -Match '^\s*$'
        }
    }

    Context "Remove-LinesFromFile" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "remove-test.txt"
            Set-Content -Path $script:testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5") -Encoding UTF8
        }

        It "行の削除時、コンテキストとサマリの間に空行がある" {
            $output = Remove-LinesFromFile -Path $script:testFile -LineRange 2,3 6>&1 | Out-String
            
            $lines = $output -split "`r?`n"
            
            $lastContextIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^\s+\d+[-:]') {
                    $lastContextIndex = $i
                }
            }
            
            $summaryIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^.*Removed \d+ line\(s\)') {
                    $summaryIndex = $i
                    break
                }
            }
            
            $lastContextIndex | Should -BeGreaterThan -1
            $summaryIndex | Should -BeGreaterThan -1
            $lines[$lastContextIndex + 1] | Should -Match '^\s*$'
        }
    }

    Context "Update-MatchInFile" {
        BeforeEach {
            $script:testFile = Join-Path $script:testDir "match-test.txt"
            Set-Content -Path $script:testFile -Value @("Line 1", "Line 2: old value", "Line 3", "Line 4: old value", "Line 5") -Encoding UTF8
        }

        It "マッチ置換時、コンテキストとサマリの間に空行がある" {
            $output = Update-MatchInFile -Path $script:testFile -OldText "old value" -Replacement "new value" 6>&1 | Out-String
            
            $lines = $output -split "`r?`n"
            
            $lastContextIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^\s+\d+[-:]') {
                    $lastContextIndex = $i
                }
            }
            
            $summaryIndex = -1
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match '^.*Updated .+: \d+ replacement\(s\) made') {
                    $summaryIndex = $i
                    break
                }
            }
            
            $lastContextIndex | Should -BeGreaterThan -1
            $summaryIndex | Should -BeGreaterThan -1
            $lines[$lastContextIndex + 1] | Should -Match '^\s*$'
        }
    }
}
