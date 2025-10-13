# Update-LinesInFile.Tests.ps1
# Update-LinesInFile コマンドレットの統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-LinesInFile Integration Tests" {
    BeforeEach {
        # 各テストの前に新しい一時ファイルを作成
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "Line 1: First line"
            "Line 2: Second line"
            "Line 3: Third line"
            "Line 4: Fourth line"
            "Line 5: Fifth line"
        )
        Set-Content -Path $script:testFile -Value $script:initialContent -Encoding UTF8
    }

    AfterEach {
        # 各テスト後にクリーンアップ
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        # バックアップファイルもクリーンアップ
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force
    }

    Context "単一行の更新" {
        It "1行を新しい内容に置き換えられる" {
            Update-LinesInFile -Path $script:testFile -LineRange 2 -Content "Updated Line 2"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Updated Line 2"
            $result.Count | Should -Be 5
        }

        It "最初の行を更新できる" {
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "New First Line"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "New First Line"
        }

        It "最後の行を更新できる" {
            Update-LinesInFile -Path $script:testFile -LineRange 5 -Content "New Last Line"
            $result = Get-Content $script:testFile
            $result[-1] | Should -Be "New Last Line"
        }
    }

    Context "複数行の更新" {
        It "連続する複数行を置き換えられる" {
            $newContent = @("New Line 2", "New Line 3")
            Update-LinesInFile -Path $script:testFile -LineRange 2,3 -Content $newContent
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "New Line 2"
            $result[2] | Should -Be "New Line 3"
            $result.Count | Should -Be 5
        }

        It "複数行を1行に置き換えられる（行数減少）" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content "Single Replacement"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 3
            $result[1] | Should -Be "Single Replacement"
        }

        It "1行を複数行に置き換えられる（行数増加）" {
            $newContent = @("Expanded Line A", "Expanded Line B", "Expanded Line C")
            Update-LinesInFile -Path $script:testFile -LineRange 3 -Content $newContent
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 7
            $result[2..4] | Should -Be $newContent
        }
    }

    Context "行の削除（Content省略）" {
        It "Contentを指定しない場合、指定行が削除される" {
            Update-LinesInFile -Path $script:testFile -LineRange 3
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result -notcontains "Line 3: Third line" | Should -Be $true
        }

        It "複数行を削除できる" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,4
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 2
            $result[0] | Should -Be "Line 1: First line"
            $result[1] | Should -Be "Line 5: Fifth line"
        }
    }

    Context "エンコーディング" {
        It "UTF-8ファイルを正しく更新できる" {
            $content = "日本語テキスト 🎌"
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content $content -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result[0] | Should -Be $content
        }
    }

    Context "バックアップ機能" {
        It "-Backup を指定するとバックアップファイルが作成される" {
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "Updated" -Backup
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak"
            $backupFiles.Count | Should -BeGreaterThan 0
        }

        It "バックアップファイルに元の内容が保存される" {
            $originalContent = Get-Content $script:testFile
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "Updated" -Backup
            $backupFile = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak" | Select-Object -First 1
            $backupContent = Get-Content $backupFile.FullName
            $backupContent | Should -Be $originalContent
        }
    }

    Context "WhatIf と Confirm" {
        It "-WhatIf を指定すると実際には変更しない" {
            $originalContent = Get-Content $script:testFile
            Update-LinesInFile -Path $script:testFile -LineRange 1 -Content "Updated" -WhatIf
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルでエラーになる" {
            { Update-LinesInFile -Path "C:\NonExistent\file.txt" -LineRange 1 -Content "Test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "範囲外の行番号でエラーになる" {
            { Update-LinesInFile -Path $script:testFile -LineRange 100 -Content "Test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "無効な範囲指定でエラーになる" {
            { Update-LinesInFile -Path $script:testFile -LineRange 5,2 -Content "Test" -ErrorAction Stop } | 
                Should -Throw
        }
    }

    Context "パイプライン入力" {
        It "パイプラインから複数のファイルを処理できる" {
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value @("File2 Line1", "File2 Line2")
            
            try {
                @($script:testFile, $file2) | Update-LinesInFile -LineRange 1 -Content "Updated"
                (Get-Content $script:testFile)[0] | Should -Be "Updated"
                (Get-Content $file2)[0] | Should -Be "Updated"
            }
            finally {
                Remove-Item $file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
