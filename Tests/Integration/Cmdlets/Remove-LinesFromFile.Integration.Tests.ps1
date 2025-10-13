# Remove-LinesFromFile.Tests.ps1
# Remove-LinesFromFile コマンドレットの統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Remove-LinesFromFile Integration Tests" {
    BeforeEach {
        # 各テストの前に新しい一時ファイルを作成
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "# Header"
            "Line 1: First line"
            "Line 2: Second line"
            "Line 3: Third line"
            "ERROR: Connection timeout"
            "error: invalid input"
            "Line 4: Fourth line"
            "WARNING: This is a warning"
            "Line 5: Fifth line"
            "# Footer"
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

    Context "行範囲による削除" {
        It "単一行を削除できる" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 2
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 9
            $result -notcontains "Line 1: First line" | Should -Be $true
        }

        It "連続する複数行を削除できる" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 2,4
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 7
            $result -notcontains "Line 1: First line" | Should -Be $true
            $result -notcontains "Line 2: Second line" | Should -Be $true
            $result -notcontains "Line 3: Third line" | Should -Be $true
        }

        It "最初の行を削除できる" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Line 1: First line"
            $result.Count | Should -Be 9
        }

        It "最後の行を削除できる" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 10
            $result = Get-Content $script:testFile
            $result[-1] | Should -Be "Line 5: Fifth line"  # 最後の行は元の9行目
            $result.Count | Should -Be 9
        }
    }

    Context "テキストマッチによる削除（Contains）" {
        It "指定したテキストを含む行を削除できる" {
            Remove-LinesFromFile -Path $script:testFile -Contains "ERROR"
            $result = Get-Content $script:testFile
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result.Count | Should -Be 9
        }

        It "複数の行がマッチする場合、すべて削除される" {
            Remove-LinesFromFile -Path $script:testFile -Contains "Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 5  # Header, ERROR, error, WARNING, Footer のみ残る
        }

        It "大文字小文字を区別する（case-sensitive）" {
            Remove-LinesFromFile -Path $script:testFile -Contains "error"
            $result = Get-Content $script:testFile
            $result -contains "ERROR: Connection timeout" | Should -Be $true  # 大文字ERRORは残る
            $result -notcontains "error: invalid input" | Should -Be $true    # 小文字errorは削除される
        }

        It "マッチする行がない場合、何も変更されない" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Contains "NonExistentText"
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "正規表現による削除（Pattern）" {
        It "正規表現にマッチする行を削除できる" {
            Remove-LinesFromFile -Path $script:testFile -Pattern "^ERROR:"
            $result = Get-Content $script:testFile
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result.Count | Should -Be 9
        }

        It "複雑な正規表現パターンを使用できる" {
            Remove-LinesFromFile -Path $script:testFile -Pattern "^(ERROR|WARNING):"
            $result = Get-Content $script:testFile
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result -notcontains "WARNING: This is a warning" | Should -Be $true
            $result.Count | Should -Be 8
        }

        It "行番号パターンで特定形式の行を削除" {
            Remove-LinesFromFile -Path $script:testFile -Pattern "Line \d+:"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 5  # Header, ERROR, error, WARNING, Footer のみ残る
        }
    }

    Context "範囲内での条件削除" {
        It "LineRange と Contains を組み合わせられる" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 2,6 -Contains "ERROR"
            $result = Get-Content $script:testFile
            # 2-6行目の範囲内でERRORを含む5行目だけが削除される
            $result -notcontains "ERROR: Connection timeout" | Should -Be $true
            $result -contains "WARNING: This is a warning" | Should -Be $true  # 範囲外なので残る
        }

        It "LineRange と Pattern を組み合わせられる" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1,5 -Pattern "^Line \d+:"
            $result = Get-Content $script:testFile
            # 1-5行目の範囲内でパターンマッチする行が削除される
            $result -notcontains "Line 1: First line" | Should -Be $true
            $result -notcontains "Line 2: Second line" | Should -Be $true
            $result -notcontains "Line 3: Third line" | Should -Be $true
            $result -contains "Line 4: Fourth line" | Should -Be $true  # 範囲外
            $result -contains "Line 5: Fifth line" | Should -Be $true   # 範囲外
        }
    }

    Context "エンコーディング" {
        It "UTF-8ファイルを正しく処理できる" {
            $content = @("日本語 Line 1", "English Line 2", "日本語 Line 3")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            Remove-LinesFromFile -Path $script:testFile -Contains "English" -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result.Count | Should -Be 2
            $result -contains "日本語 Line 1" | Should -Be $true
        }
    }

    Context "バックアップ機能" {
        It "-Backup を指定するとバックアップファイルが作成される" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1 -Backup
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak"
            $backupFiles.Count | Should -BeGreaterThan 0
        }

        It "バックアップファイルに元の内容が保存される" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -LineRange 1 -Backup
            $backupFile = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak" | Select-Object -First 1
            $backupContent = Get-Content $backupFile.FullName
            $backupContent | Should -Be $originalContent
        }
    }

    Context "WhatIf と Confirm" {
        It "-WhatIf を指定すると実際には変更しない" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Contains "ERROR" -WhatIf
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルでエラーになる" {
            { Remove-LinesFromFile -Path "C:\NonExistent\file.txt" -LineRange 1 -ErrorAction Stop } | 
                Should -Throw
        }

        It "範囲外の行番号で警告を出すが続行する" {
            $result = Remove-LinesFromFile -Path $script:testFile -LineRange 100 -WarningVariable warnings 3>&1
            $warnings | Should -Not -BeNullOrEmpty
        }

        It "無効な範囲指定でエラーになる" {
            { Remove-LinesFromFile -Path $script:testFile -LineRange 9,2 -ErrorAction Stop } | 
                Should -Throw
        }

        It "無効な正規表現でエラーになる" {
            { Remove-LinesFromFile -Path $script:testFile -Pattern "[invalid(" -ErrorAction Stop } | 
                Should -Throw
        }

        It "LineRange、Contains、Pattern のいずれも指定しない場合エラーになる" {
            { Remove-LinesFromFile -Path $script:testFile -ErrorAction Stop } | 
                Should -Throw
        }
    }

    Context "パイプライン入力" {
        It "パイプラインから複数のファイルを処理できる" {
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value @("ERROR: Error in file2", "Normal line")
            
            try {
                Get-Item @($script:testFile, $file2) | Remove-LinesFromFile -Contains "ERROR"
                $result1 = Get-Content $script:testFile
                $result2 = Get-Content $file2
                $result1 -notcontains "ERROR: Connection timeout" | Should -Be $true
                $result2 -notcontains "ERROR: Error in file2" | Should -Be $true
            }
            finally {
                Remove-Item $file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
