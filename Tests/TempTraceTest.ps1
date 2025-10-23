Describe "エラートレーステスト" {
    It "Test 1" {
        { throw "UNIQUE_ERROR_MESSAGE_001" } | Should -Throw
    }
}
