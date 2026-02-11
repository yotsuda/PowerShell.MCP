Describe "Show-TextFiles Contains and Pattern Combination" {
    BeforeAll {
        $script:testDir = Join-Path $env:TEMP "PSMCPTests_ContainsPattern_$([System.Random]::new().Next())"
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

    It "Should match lines with Contains OR Pattern (both specified)" {
        # Create test file
        @"
Line 1: normal text
Line 2: [Error] something went wrong
Line 3: normal text
Line 4: Warning: check this
Line 5: normal text
Line 6: [Error] another error
Line 7: Critical failure
"@ | Set-Content $script:testFile -NoNewline
        
        # Search with Contains "[Error]" OR Pattern "Warning|Critical"
        $result = Show-TextFiles -Path $script:testFile -Contains "[Error]" -Pattern "Warning|Critical"
        $resultText = $result -join "`n"
        
        # Should match lines 2, 4, 6, 7
        $resultText | Should -Match "Line 2.*\[Error\]"
        $resultText | Should -Match "Line 4.*Warning"
        $resultText | Should -Match "Line 6.*\[Error\]"
        $resultText | Should -Match "Line 7.*Critical"
    }

    It "Should work with Contains only (backward compatibility)" {
        @"
Line 1: normal
Line 2: [special] text
Line 3: normal
"@ | Set-Content $script:testFile -NoNewline
        
        $result = Show-TextFiles -Path $script:testFile -Contains "[special]"
        $resultText = $result -join "`n"
        
        $resultText | Should -Match "Line 2.*\[special\]"
    }

    It "Should work with Pattern only (backward compatibility)" {
        @"
Line 1: normal
Line 2: error here
Line 3: normal
"@ | Set-Content $script:testFile -NoNewline
        
        $result = Show-TextFiles -Path $script:testFile -Pattern "error"
        $resultText = $result -join "`n"
        
        $resultText | Should -Match "Line 2.*error"
    }

    It "Should escape Contains properly when combined with Pattern" {
        # Contains with regex metacharacters should be escaped
        @"
Line 1: normal
Line 2: (test)
Line 3: test
Line 4: normal
"@ | Set-Content $script:testFile -NoNewline
        
        # "(test)" should match literally, "test" should match via pattern
        $result = Show-TextFiles -Path $script:testFile -Contains "(test)" -Pattern "^Line 3"
        $resultText = $result -join "`n"
        
        $resultText | Should -Match "Line 2.*\(test\)"
        $resultText | Should -Match "Line 3.*test"
    }
}
