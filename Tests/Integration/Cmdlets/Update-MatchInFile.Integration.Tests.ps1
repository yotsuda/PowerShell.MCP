# Update-MatchInFile.Tests.ps1
# Update-MatchInFile コマンドレットの統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-MatchInFile Integration Tests" {
    BeforeEach {
        # 各テストの前に新しい一時ファイルを作成
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "Server: localhost"
            "Port: 8080"
            "Username: admin"
            "Password: secret123"
            "Debug: true"
            "Timeout: 30"
            "MaxRetries: 3"
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

    Context "テキストマッチによる置換（Contains）" {
        It "指定したテキストを含む部分を置換できる" {
            Update-MatchInFile -Path $script:testFile -Contains "localhost" -Replacement "production.example.com"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Server: production.example.com"
        }

        It "複数行にマッチする場合、すべて置換される" {
            Update-MatchInFile -Path $script:testFile -Contains "true" -Replacement "false"
            $result = Get-Content $script:testFile
            $result[4] | Should -Be "Debug: false"
        }

        It "マッチしない場合、何も変更されない" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -Contains "NonExistentText" -Replacement "NewValue"
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }

        It "大文字小文字を区別する（case-sensitive）" {
            Update-MatchInFile -Path $script:testFile -Contains "LOCALHOST" -Replacement "server.local"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Server: localhost"  # 大文字とマッチしないため変更されない
        }
    }

    Context "正規表現による置換（Pattern）" {
        It "正規表現パターンにマッチする部分を置換できる" {
            Update-MatchInFile -Path $script:testFile -Pattern "\d+" -Replacement "9999"
            $result = Get-Content $script:testFile
            # すべての数字が9999に置換される
            $result[1] | Should -Be "Port: 9999"
            $result[5] | Should -Be "Timeout: 9999"
            $result[6] | Should -Be "MaxRetries: 9999"
        }

        It "キャプチャグループを使用した置換ができる" {
            Update-MatchInFile -Path $script:testFile -Pattern "Port: (\d+)" -Replacement "Port: $1$1"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 80808080"
        }

        It "複雑な正規表現パターンを使用できる" {
            Update-MatchInFile -Path $script:testFile -Pattern "Password: \w+" -Replacement "Password: ********"
            $result = Get-Content $script:testFile
            $result[3] | Should -Be "Password: ********"
        }

        It "行全体を置換できる" {
            Update-MatchInFile -Path $script:testFile -Pattern "^Debug: true$" -Replacement "Debug: false"
            $result = Get-Content $script:testFile
            $result[4] | Should -Be "Debug: false"
        }

        It "複数のマッチをすべて置換" {
            Set-Content -Path $script:testFile -Value "AAA BBB AAA CCC AAA" -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern "AAA" -Replacement "XXX"
            $result = Get-Content $script:testFile
            $result | Should -Be "XXX BBB XXX CCC XXX"
        }
    }

    Context "範囲内での置換" {
        It "LineRange と Contains を組み合わせられる" {
            Update-MatchInFile -Path $script:testFile -LineRange 1,3 -Contains "admin" -Replacement "superuser"
            $result = Get-Content $script:testFile
            $result[2] | Should -Be "Username: superuser"
            # 範囲外は変更されない
        }

        It "LineRange と Pattern を組み合わせられる" {
            Update-MatchInFile -Path $script:testFile -LineRange 2,4 -Pattern "\d+" -Replacement "9999"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 9999"  # 範囲内
            $result[5] | Should -Be "Timeout: 30"  # 範囲外なので変更されない
        }

        It "範囲の最初の行のみ置換" {
            Update-MatchInFile -Path $script:testFile -LineRange 1,1 -Pattern "localhost" -Replacement "newhost"
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Server: newhost"
        }

        It "範囲の最後の行のみ置換" {
            Update-MatchInFile -Path $script:testFile -LineRange 7,7 -Pattern "\d+" -Replacement "10"
            $result = Get-Content $script:testFile
            $result[6] | Should -Be "MaxRetries: 10"
        }
    }

    Context "設定ファイルの更新シナリオ" {
        It "設定値を更新できる" {
            Update-MatchInFile -Path $script:testFile -Pattern "Port: \d+" -Replacement "Port: 3000"
            Update-MatchInFile -Path $script:testFile -Pattern "Debug: \w+" -Replacement "Debug: false"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 3000"
            $result[4] | Should -Be "Debug: false"
        }

        It "複数の設定を一度に更新（パイプライン）" {
            Update-MatchInFile -Path $script:testFile -Pattern "8080" -Replacement "9090"
            Update-MatchInFile -Path $script:testFile -Pattern "30" -Replacement "60"
            $result = Get-Content $script:testFile
            $result[1] | Should -Be "Port: 9090"
            $result[5] | Should -Be "Timeout: 60"
        }
    }

    Context "特殊文字の処理" {
        It "特殊文字を含むテキストを置換できる" {
            Set-Content -Path $script:testFile -Value @("Price: $100", "Discount: 10%") -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Contains '$100' -Replacement '$200'
            $result = Get-Content $script:testFile
            $result[0] | Should -Be "Price: $200"
        }

        It "正規表現の特殊文字をエスケープして使用" {
            Set-Content -Path $script:testFile -Value "Email: user@example.com" -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern "@" -Replacement "[at]"
            $result = Get-Content $script:testFile
            $result | Should -Be "Email: user[at]example.com"
        }
    }

    Context "エンコーディング" {
        It "UTF-8ファイルを正しく処理できる" {
            $content = "設定: 日本語"
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Contains "日本語" -Replacement "English" -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result | Should -Be "設定: English"
        }

        It "日本語の正規表現マッチ" {
            Set-Content -Path $script:testFile -Value @("名前: 太郎", "年齢: 25") -Encoding UTF8
            Update-MatchInFile -Path $script:testFile -Pattern "太郎" -Replacement "花子" -Encoding UTF8
            $result = Get-Content $script:testFile -Encoding UTF8
            $result[0] | Should -Be "名前: 花子"
        }
    }

    Context "バックアップ機能" {
        It "-Backup を指定するとバックアップファイルが作成される" {
            Update-MatchInFile -Path $script:testFile -Contains "localhost" -Replacement "newhost" -Backup
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak"
            $backupFiles.Count | Should -BeGreaterThan 0
        }

        It "バックアップファイルに元の内容が保存される" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -Contains "localhost" -Replacement "newhost" -Backup
            $backupFile = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*.bak" | Select-Object -First 1
            $backupContent = Get-Content $backupFile.FullName
            $backupContent | Should -Be $originalContent
        }
    }

    Context "WhatIf と Confirm" {
        It "-WhatIf を指定すると実際には変更しない" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -Contains "localhost" -Replacement "newhost" -WhatIf
            $result = Get-Content $script:testFile
            $result | Should -Be $originalContent
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルでエラーになる" {
            { Update-MatchInFile -Path "C:\NonExistent\file.txt" -Contains "test" -Replacement "new" -ErrorAction Stop } | 
                Should -Throw
        }

        It "Contains と Pattern を同時に指定するとエラーになる" {
            { Update-MatchInFile -Path $script:testFile -Contains "test" -Pattern "test" -Replacement "new" -ErrorAction Stop } | 
                Should -Throw
        }

        It "Replacement を指定しないとエラーになる" {
            { Update-MatchInFile -Path $script:testFile -Contains "test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "無効な正規表現でエラーになる" {
            { Update-MatchInFile -Path $script:testFile -Pattern "[invalid(" -Replacement "new" -ErrorAction Stop } | 
                Should -Throw
        }
    }

    Context "パイプライン入力" {
        It "パイプラインから複数のファイルを処理できる" {
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value "Server: localhost"
            
            try {
                Get-Item @($script:testFile, $file2) | Update-MatchInFile -Contains "localhost" -Replacement "production"
                $result1 = Get-Content $script:testFile
                $result2 = Get-Content $file2
                $result1[0] | Should -Be "Server: production"
                $result2 | Should -Be "Server: production"
            }
            finally {
                Remove-Item $file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
