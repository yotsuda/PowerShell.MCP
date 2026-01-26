Describe "Update-MatchInFile Newline Normalization" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "PSMCPTests_NewlineNorm_$([System.Random]::new().Next())"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force
        }
    }

    BeforeEach {
        $script:testFile = Join-Path $script:testDir "test_$([System.Random]::new().Next()).txt"
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    It "Should normalize LF in Replacement to CRLF when file uses CRLF" {
        # Create a CRLF file
        [System.IO.File]::WriteAllText($script:testFile, "Line1`r`nLine2`r`nLine3", [System.Text.Encoding]::UTF8)
        
        # Verify original file uses CRLF only
        $originalBytes = [System.IO.File]::ReadAllBytes($script:testFile)
        $originalCrlfCount = 0
        $originalLfOnlyCount = 0
        for ($i = 0; $i -lt $originalBytes.Length; $i++) {
            if ($originalBytes[$i] -eq 10) {
                if ($i -gt 0 -and $originalBytes[$i-1] -eq 13) {
                    $originalCrlfCount++
                } else {
                    $originalLfOnlyCount++
                }
            }
        }
        $originalLfOnlyCount | Should -Be 0 -Because "original file should only have CRLF"
        $originalCrlfCount | Should -Be 2 -Because "original file should have 2 CRLFs"
        
        # Replace with content containing LF (`n in PowerShell)
        Update-MatchInFile -Path $script:testFile -OldText "Line2" -Replacement "Line2`nNewLine"
        
        # Verify result file still uses only CRLF (no mixed line endings)
        $resultBytes = [System.IO.File]::ReadAllBytes($script:testFile)
        $crlfCount = 0
        $lfOnlyCount = 0
        for ($i = 0; $i -lt $resultBytes.Length; $i++) {
            if ($resultBytes[$i] -eq 10) {
                if ($i -gt 0 -and $resultBytes[$i-1] -eq 13) {
                    $crlfCount++
                } else {
                    $lfOnlyCount++
                }
            }
        }
        
        $lfOnlyCount | Should -Be 0 -Because "file should not have mixed line endings (LF only)"
        $crlfCount | Should -Be 3 -Because "file should have 3 CRLFs after replacement"
        
        # Verify content is correct
        $content = [System.IO.File]::ReadAllText($script:testFile)
        $content | Should -Be "Line1`r`nLine2`r`nNewLine`r`nLine3"
    }

    It "Should normalize CRLF in Replacement to LF when file uses LF" {
        # Create a LF-only file
        [System.IO.File]::WriteAllText($script:testFile, "Line1`nLine2`nLine3", [System.Text.Encoding]::UTF8)
        
        # Replace with content containing CRLF
        Update-MatchInFile -Path $script:testFile -OldText "Line2" -Replacement "Line2`r`nNewLine"
        
        # Verify result file still uses only LF
        $resultBytes = [System.IO.File]::ReadAllBytes($script:testFile)
        $crlfCount = 0
        $lfOnlyCount = 0
        for ($i = 0; $i -lt $resultBytes.Length; $i++) {
            if ($resultBytes[$i] -eq 10) {
                if ($i -gt 0 -and $resultBytes[$i-1] -eq 13) {
                    $crlfCount++
                } else {
                    $lfOnlyCount++
                }
            }
        }
        
        $crlfCount | Should -Be 0 -Because "file should not have CRLF when original used LF"
        $lfOnlyCount | Should -Be 3 -Because "file should have 3 LFs after replacement"
        
        # Verify content is correct
        $content = [System.IO.File]::ReadAllText($script:testFile)
        $content | Should -Be "Line1`nLine2`nNewLine`nLine3"
    }

    It "Should normalize mixed newlines in Replacement to match file format" {
        # Create a CRLF file
        [System.IO.File]::WriteAllText($script:testFile, "Line1`r`nLine2`r`nLine3", [System.Text.Encoding]::UTF8)
        
        # Replace with content containing mixed newlines
        Update-MatchInFile -Path $script:testFile -OldText "Line2" -Replacement "A`nB`r`nC`rD"
        
        # Verify result file uses only CRLF
        $resultBytes = [System.IO.File]::ReadAllBytes($script:testFile)
        $crlfCount = 0
        $lfOnlyCount = 0
        $crOnlyCount = 0
        for ($i = 0; $i -lt $resultBytes.Length; $i++) {
            if ($resultBytes[$i] -eq 10) {
                if ($i -gt 0 -and $resultBytes[$i-1] -eq 13) {
                    $crlfCount++
                } else {
                    $lfOnlyCount++
                }
            } elseif ($resultBytes[$i] -eq 13) {
                if ($i + 1 -ge $resultBytes.Length -or $resultBytes[$i+1] -ne 10) {
                    $crOnlyCount++
                }
            }
        }
        
        $lfOnlyCount | Should -Be 0 -Because "file should not have LF-only"
        $crOnlyCount | Should -Be 0 -Because "file should not have CR-only"
        $crlfCount | Should -Be 5 -Because "file should have 5 CRLFs (2 original + 3 from replacement)"
    }
}
