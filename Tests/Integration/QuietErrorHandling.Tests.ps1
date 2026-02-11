#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../Shared/TestHelpers.psm1" -Force
}

Describe "ファイル操作エラーの検証" {
    Context "ファイル操作エラーの検証" {
        It "存在しないファイルでエラー" {
            { Show-TextFiles -Path "C:\NonExistent\file.txt" -ErrorAction Stop } | Should -Throw -ExpectedMessage "*File not found*"
        }
        
        It "無効なパスでエラー" {
            { Add-LinesToFile -Path "C:\Invalid::\Path.txt" -LineNumber 1 -Content "test" -ErrorAction Stop } | Should -Throw
        }
        
        It "無効な LineRange でエラー" {
            $temp = New-TemporaryFile
            "test" | Out-File $temp
            try {
                { Show-TextFiles -Path $temp -LineRange @(10, 5) } | Should -Throw -ExpectedMessage "*must be less than or equal to*"
            } finally {
                Remove-Item $temp -Force
            }
        }
        
        It "負の LineNumber でエラー" {
            { Add-LinesToFile -Path "test.txt" -LineNumber -5 -Content "test" } | Should -Throw -ExpectedMessage "*less than the minimum*"
        }
    }
}