# Test-AdvancedCmdlets.ps1
# HIGH priority tests for Update-LinesInFile, Remove-LinesFromFile, Update-MatchInFile

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-LinesInFile HIGH Priority Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Line 1"
            "Line 2"
            "Line 3"
            "Line 4"
            "Line 5"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Context "HIGH priority: boundary values and edge cases" {
        It "H21. errors when LineRange is reversed" {
            # The implementation does not allow reversed ranges; it throws an exception
            { Update-LinesInFile -Path $script:testFile -LineRange 3,1 -Content "Updated" -ErrorAction Stop } |
                Should -Throw
        }

        It "H22. -Content @() can delete lines" {
            $originalCount = (Get-Content $script:testFile).Count
            Update-LinesInFile -Path $script:testFile -LineRange 2,3 -Content @()
            $newCount = (Get-Content $script:testFile).Count
            # 2 lines are deleted
            $newCount | Should -Be ($originalCount - 2)
        }

        It "H23. can handle Content with more lines than the original" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,2 -Content @("New Line 1", "New Line 2", "New Line 3")
            $result = Get-Content $script:testFile
            # Replace 1 line with 3 lines
            $result | Should -Contain "New Line 1"
            $result | Should -Contain "New Line 2"
            $result | Should -Contain "New Line 3"
        }

        It "H24. can handle Content with fewer lines than the original" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content "Single Replacement"
            $result = Get-Content $script:testFile
            # Replace 3 lines with 1 line
            $result | Should -Contain "Single Replacement"
            $result.Count | Should -BeLessThan 5
        }

        It "H25. throws an exception for an out-of-range LineRange" {
            $threw = $false
            try {
                Update-LinesInFile -Path $script:testFile -LineRange 100,200 -Content "Test" -ErrorAction Stop
            } catch {
                $threw = $true
                $_.Exception.Message | Should -Match "out of bounds"
            }
            $threw | Should -BeTrue
            # The file is unchanged
            $result = Get-Content $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }
    }
}

Describe "Remove-LinesFromFile HIGH Priority Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Apple"
            "Banana"
            "Cherry"
            "Date"
            "Elderberry"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Context "HIGH priority: edge cases and error handling" {
        It "H26. can delete all lines" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1,5
            $result = Get-Content $script:testFile -ErrorAction SilentlyContinue
            # The file becomes empty
            if ($result) {
                $result.Count | Should -Be 0
            } else {
                $result | Should -BeNullOrEmpty
            }
        }

        It "H27. nothing is deleted when Contains has no match" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Contains "NonExistent"
            $newContent = Get-Content $script:testFile
            # No change
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }

        It "H28. nothing is deleted when Pattern has no match" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Pattern "^Z.*"
            $newContent = Get-Content $script:testFile
            # No change
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }
    }
}

Describe "Update-MatchInFile HIGH Priority Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Email: user@example.com"
            "Email: admin@example.com"
            "Email: test@example.com"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Context "HIGH priority: matching and edge cases" {
        It "H29. nothing is changed when Pattern does not match" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -Pattern "nonexistent" -Replacement "test"
            $newContent = Get-Content $script:testFile
            # No change
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }

        It "H30. can replace the matched portion by specifying Pattern and Replacement" {
            # Confirm the correct replacement
            Update-MatchInFile -Path $script:testFile -Pattern "@example\.com" -Replacement "@newdomain.com"
            $result = Get-Content $script:testFile
            # @example.com is replaced with @newdomain.com
            $result[0] | Should -Be "Email: user@newdomain.com"
            $result[1] | Should -Be "Email: admin@newdomain.com"
            $result[2] | Should -Be "Email: test@newdomain.com"
        }
    }
}
