#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

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
}
