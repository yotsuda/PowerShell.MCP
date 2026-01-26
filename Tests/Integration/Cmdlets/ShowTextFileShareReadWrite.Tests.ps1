Describe "Show-TextFile FileShare ReadWrite" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "PSMCPTests_FileShare_$([System.Random]::new().Next())"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force
        }
    }

    It "Should read a file that is locked by another process for writing" {
        $testFile = Join-Path $script:testDir "locked_file.txt"
        
        # Create file with content
        "Line 1`r`nLine 2`r`nLine 3" | Set-Content $testFile -NoNewline
        
        # Lock the file with exclusive write access (simulating another app writing to it)
        $fs = [System.IO.File]::Open($testFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::Read)
        
        try {
            # Show-TextFile should still be able to read the file
            $result = Show-TextFile -Path $testFile
            $resultText = $result -join "`n"
            
            $resultText | Should -Match "Line 1"
            $resultText | Should -Match "Line 2"
            $resultText | Should -Match "Line 3"
        }
        finally {
            $fs.Close()
            $fs.Dispose()
        }
    }

    It "Should read a file with Pattern search while file is locked" {
        $testFile = Join-Path $script:testDir "locked_pattern.txt"
        
        "Line 1 normal`r`nLine 2 ERROR here`r`nLine 3 normal" | Set-Content $testFile -NoNewline
        
        $fs = [System.IO.File]::Open($testFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::Read)
        
        try {
            $result = Show-TextFile -Path $testFile -Pattern "ERROR"
            $resultText = $result -join "`n"
            
            $resultText | Should -Match "ERROR"
        }
        finally {
            $fs.Close()
            $fs.Dispose()
        }
    }
}
