#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Should -Throw テスト" {
    It "Test 1" {
        { Show-TextFile -Path "C:\NonExistent1.txt" -ErrorAction Stop } | Should -Throw
    }
    It "Test 2" {
        { Show-TextFile -Path "C:\NonExistent2.txt" -ErrorAction Stop } | Should -Throw
    }
    It "Test 3" {
        { Show-TextFile -Path "C:\NonExistent3.txt" -ErrorAction Stop } | Should -Throw
    }
}
