#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../Shared/TestHelpers.psm1" -Force
}

Describe "Test-ThrowsQuietly Function Tests" -Skip {
    It "Catches an exception successfully" {
        Test-ThrowsQuietly { throw "Test error" }
    }

    It "Verifies the expected message" {
        Test-ThrowsQuietly { throw "File not found: test.txt" } -ExpectedMessage "File not found"
    }

    It "Fails when no exception is thrown" -Skip {
        { Test-ThrowsQuietly { "No error" } } | Should -Throw
    }

    It "Works even with complex exceptions" {
        Test-ThrowsQuietly {
            $dict = @{}
            $null = $dict["nonexistent"].ToString()
        }
    }
}