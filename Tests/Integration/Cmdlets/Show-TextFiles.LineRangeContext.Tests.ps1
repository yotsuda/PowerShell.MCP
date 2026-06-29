# Show-TextFiles: LineRange with Pattern/Contains should display context lines
# Issue: When using -LineRange with -Pattern or -Contains, context lines are not displayed

Describe "Show-TextFiles LineRange Context Display" {
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
            # Specify LineRange 4,4 (line 4 only)
            # Expected: lines 2,3 (2 lines before) and 5,6 (2 lines after) are also shown as context
            $result = Show-TextFiles -Path $script:testFile -Pattern "MATCH" -LineRange 4,4

            # Exclude the header
            $contentLines = $result | Where-Object { $_ -notmatch "==>" -and $_ -ne "" }

            # Display the result (for debugging)
            Write-Host "Result lines:"
            $contentLines | ForEach-Object { Write-Host "  $_" }

            # Expected line count: 2 (before) + 1 (match) + 2 (after) = 5 lines
            $contentLines.Count | Should -Be 5

            # Verify the context lines
            $contentLines[0] | Should -Match "2- Line 2"  # 2 lines before
            $contentLines[1] | Should -Match "3- Line 3"  # 1 line before
            $contentLines[2] | Should -Match "4:.*MATCH"  # matched line
            $contentLines[3] | Should -Match "5- Line 5"  # 1 line after
            $contentLines[4] | Should -Match "6- Line 6"  # 2 lines after
        }
    }

    Context "Contains with LineRange should show context" {
        It "Should display context lines when match is in LineRange" {
            $result = Show-TextFiles -Path $script:testFile -Contains "MATCH" -LineRange 4,4
            
            $contentLines = $result | Where-Object { $_ -notmatch "==>" -and $_ -ne "" }

            # Expected line count: 2 (before) + 1 (match) + 2 (after) = 5 lines
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
            $result = Show-TextFiles -Path $script:testFile -Pattern "MATCH"
            
            $contentLines = $result | Where-Object { $_ -notmatch "==>" -and $_ -ne "" }

            # Without LineRange, context is displayed (baseline check)
            $contentLines.Count | Should -BeGreaterThan 1
            $contentLines | Where-Object { $_ -match "MATCH" } | Should -Not -BeNullOrEmpty
        }
    }
}
