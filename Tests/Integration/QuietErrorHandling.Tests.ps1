#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

BeforeAll {
    Import-Module "$PSScriptRoot/../Shared/TestHelpers.psm1" -Force
}

Describe "Test-ThrowsQuietly 実用例" {
    Context "ファイル操作エラーの検証" {
        It "存在しないファイルでエラー" {
            Test-ThrowsQuietly {
                Show-TextFile -Path "C:\NonExistent\file.txt"
            } -ExpectedMessage "File not found"
        }
        
        It "無効なパスでエラー" {
            Test-ThrowsQuietly {
                Add-LinesToFile -Path "C:\Invalid::\Path.txt" -LineNumber 1 -Content "test"
            }
        }
        
        It "無効な LineRange でエラー" {
            $temp = New-TemporaryFile
            "test" | Out-File $temp
            try {
                Test-ThrowsQuietly {
                    Show-TextFile -Path $temp -LineRange @(10, 5)
                } -ExpectedMessage "must be less than or equal to"
            } finally {
                Remove-Item $temp -Force
            }
        }
        
        It "負の LineNumber でエラー" {
            Test-ThrowsQuietly {
                Add-LinesToFile -Path "test.txt" -LineNumber -5 -Content "test"
            } -ExpectedMessage "less than the minimum"
        }
    }
}
