#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../Shared/TestHelpers.psm1" -Force
}

Describe "Test-ThrowsQuietly Function Tests" -Skip {
    It "正常に例外をキャッチする" {
        Test-ThrowsQuietly { throw "Test error" }
    }
    
    It "期待されるメッセージを検証する" {
        Test-ThrowsQuietly { throw "File not found: test.txt" } -ExpectedMessage "File not found"
    }
    
    It "例外がスローされない場合は失敗する" -Skip {
        { Test-ThrowsQuietly { "No error" } } | Should -Throw
    }
    
    It "複雑な例外でも動作する" {
        Test-ThrowsQuietly {
            $dict = @{}
            $null = $dict["nonexistent"].ToString()
        }
    }
}