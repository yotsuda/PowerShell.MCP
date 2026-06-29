#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../Shared/TestHelpers.psm1" -Force
}

Describe "File operation error validation" {
    Context "File operation error validation" {
        It "Errors on a nonexistent file" {
            { Show-TextFiles -Path "C:\NonExistent\file.txt" -ErrorAction Stop } | Should -Throw -ExpectedMessage "*File not found*"
        }

        It "Errors on an invalid path" {
            { Add-LinesToFile -Path "C:\Invalid::\Path.txt" -LineNumber 1 -Content "test" -ErrorAction Stop } | Should -Throw
        }

        It "Errors on an invalid LineRange" {
            $temp = New-TemporaryFile
            "test" | Out-File $temp
            try {
                { Show-TextFiles -Path $temp -LineRange @(10, 5) } | Should -Throw -ExpectedMessage "*must be less than or equal to*"
            } finally {
                Remove-Item $temp -Force
            }
        }
        
        It "Errors on a negative LineNumber" {
            { Add-LinesToFile -Path "test.txt" -LineNumber -5 -Content "test" } | Should -Throw -ExpectedMessage "*less than the minimum*"
        }
    }
}