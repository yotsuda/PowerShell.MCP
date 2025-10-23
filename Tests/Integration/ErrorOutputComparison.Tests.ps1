#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../Shared/TestHelpers.psm1" -Force
}

Describe "エラー出力比較テスト" {
    Context "Should -Throw を使用（従来の方法）" {
        It "Test 1" {
            { throw "Error 1" } | Should -Throw
        }
        It "Test 2" {
            { throw "Error 2" } | Should -Throw
        }
        It "Test 3" {
            { throw "Error 3" } | Should -Throw
        }
    }
    
    Context "Test-ThrowsQuietly を使用（新しい方法）" {
        It "Test 1" {
            Test-ThrowsQuietly { throw "Error 1" }
        }
        It "Test 2" {
            Test-ThrowsQuietly { throw "Error 2" }
        }
        It "Test 3" {
            Test-ThrowsQuietly { throw "Error 3" }
        }
    }
}
