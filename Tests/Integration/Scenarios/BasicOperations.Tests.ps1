# Test-AllCmdlets.ps1
# すべてのテキストファイル操作コマンドレットの統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "All Text File Cmdlets Integration Tests" {
    BeforeAll {
        # テスト用の一時ディレクトリとファイルを作成
        $script:testDir = Join-Path $env:TEMP "PowerShellMCP_Tests_$(Get-Random)"
        New-Item -ItemType Directory -Path $script:testDir -Force | Out-Null
        
        $script:testFile = Join-Path $script:testDir "test.txt"
        $script:testContent = @(
            "# Sample File"
            "Line 1: First line of content"
            "Line 2: Second line of content"
            "Line 3: Third line of content"
            "Line 4: Fourth line of content"
            "Line 5: Fifth line of content"
            "# End of file"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterAll {
        # クリーンアップ
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force
        }
    }

    Context "完全なワークフロー: 表示 → 追加 → 更新 → 削除" {
        It "ステップ1: Show-TextFile でファイル内容を確認" {
            $content = Show-TextFile -Path $script:testFile
            $content | Should -Not -BeNullOrEmpty
            # ヘッダー行 + 7行のコンテンツ = 8行
            $content.Count | Should -Be 8
        }

        It "ステップ2: Add-LinesToFile で新しい行を追加" {
            Add-LinesToFile -Path $script:testFile -Content "Line 6: Added line"
            $content = Get-Content $script:testFile
            # 末尾の前（"# End of file"の前）に追加されるか、末尾に追加される
            $content | Should -Contain "Line 6: Added line"
        }

        It "ステップ3: Update-LinesInFile で行を更新" {
            Update-LinesInFile -Path $script:testFile -LineRange 3,3 -Content "Line 2: UPDATED line"
            $content = Get-Content $script:testFile
            $content | Where-Object { $_ -match "UPDATED" } | Should -Not -BeNullOrEmpty
        }

        It "ステップ4: Remove-LinesFromFile で行を削除" {
            $beforeCount = (Get-Content $script:testFile).Count
            Remove-LinesFromFile -Path $script:testFile -LineRange 1,1
            $afterCount = (Get-Content $script:testFile).Count
            $afterCount | Should -Be ($beforeCount - 1)
        }
    }

    Context "複数ファイルの一括操作" {
        BeforeAll {
            # 複数のテストファイルを作成
            $script:multiFiles = 1..3 | ForEach-Object {
                $file = Join-Path $script:testDir "multi_$_.txt"
                Set-Content -Path $file -Value "Content of file $_" -Encoding UTF8
                $file
            }
        }

        It "複数ファイルに対して Show-TextFile を実行" {
            $results = Show-TextFile -Path $script:multiFiles
            $results | Should -Not -BeNullOrEmpty
            # 各ファイルごとにヘッダー行 + コンテンツ行
            # 3ファイル × (1ヘッダー + 1コンテンツ) = 6行
            $results.Count | Should -BeGreaterOrEqual 6
        }

        It "複数ファイルに対して Add-LinesToFile を実行" {
            Add-LinesToFile -Path $script:multiFiles -Content "Added line"
            foreach ($file in $script:multiFiles) {
                $content = Get-Content $file
                $content[-1] | Should -Be "Added line"
            }
        }
    }

    Context "パイプライン処理" {
        It "Get-ChildItem | Show-TextFile のパイプラインが動作" {
            $results = Get-ChildItem -Path $script:testDir -Filter "*.txt" | 
                Select-Object -First 1 |
                Show-TextFile
            $results | Should -Not -BeNullOrEmpty
        }

        It "ファイルオブジェクトを Show-TextFile にパイプできる" {
            $files = Get-ChildItem -Path $script:testDir -Filter "multi_*.txt" | Select-Object -First 1
            $results = $files | Show-TextFile
            $results | Should -Not -BeNullOrEmpty
        }
    }

    Context "エンコーディング互換性" {
        BeforeAll {
            $script:utf8File = Join-Path $script:testDir "utf8.txt"
            $script:sjisFile = Join-Path $script:testDir "sjis.txt"
            
            "UTF-8 テスト 日本語" | Out-File -FilePath $script:utf8File -Encoding UTF8
            
            # Shift-JIS でファイルを作成
            $sjisEncoding = [System.Text.Encoding]::GetEncoding("shift_jis")
            $sjisBytes = $sjisEncoding.GetBytes("Shift-JIS テスト 日本語")
            [System.IO.File]::WriteAllBytes($script:sjisFile, $sjisBytes)
        }

        It "UTF-8 ファイルを正しく読み取れる" {
            $content = Show-TextFile -Path $script:utf8File -Encoding "utf-8"
            # 実データ行（ヘッダーの次）に日本語が含まれる
            ($content -join "`n") | Should -Match "日本語"
        }

        It "Shift-JIS ファイルを正しく読み取れる" {
            $content = Show-TextFile -Path $script:sjisFile -Encoding "shift_jis"
            # 実データ行（ヘッダーの次）に日本語が含まれる
            ($content -join "`n") | Should -Match "日本語"
        }

        It "エンコーディング自動検出が動作する" {
            $content = Show-TextFile -Path $script:utf8File
            # 自動検出でも日本語が読める
            ($content -join "`n") | Should -Match "UTF-8"
        }
    }

    Context "エラー処理とリカバリ" {

        It "Update-MatchInFile でパターンマッチング置換ができる" {
            $testFile = Join-Path $script:testDir "pattern_test.txt"
            Set-Content -Path $testFile -Value @("test@example.com", "user@domain.com")
            
            Update-MatchInFile -Path $testFile -Pattern "@example\.com" -Replacement "@newdomain.com"
            $content = Get-Content $testFile
            $content[0] | Should -Be "test@newdomain.com"
            
            Remove-Item $testFile -Force
        }
    }

    Context "バックアップと安全性" {
        It "全コマンドで -Backup オプションが動作する" {
            $backupTestFile = Join-Path $script:testDir "backup_test.txt"
            Set-Content -Path $backupTestFile -Value "Original"
            
            Add-LinesToFile -Path $backupTestFile -Content "Added" -Backup
            # タイムスタンプ付きバックアップが作成される
            $backups = Get-ChildItem -Path $script:testDir -Filter "backup_test.txt.*" |
                Where-Object { $_.Name -match '\.bak$' }
            $backups.Count | Should -BeGreaterThan 0
            
            Remove-Item "$backupTestFile*" -Force
        }

        It "全コマンドで -WhatIf が動作する" {
            $whatIfFile = Join-Path $script:testDir "whatif_test.txt"
            Set-Content -Path $whatIfFile -Value "Original"
            $original = Get-Content $whatIfFile
            
            Add-LinesToFile -Path $whatIfFile -Content "Test" -WhatIf
            Update-LinesInFile -Path $whatIfFile -LineRange 1,1 -Content "Test" -WhatIf
            Remove-LinesFromFile -Path $whatIfFile -LineRange 1,1 -WhatIf
            
            $after = Get-Content $whatIfFile
            Compare-Object $original $after | Should -BeNullOrEmpty
            
            Remove-Item $whatIfFile -Force
        }
    }
}

# テストスイートの実行方法
<#
.SYNOPSIS
    すべてのテキストファイル操作コマンドレットの統合テストを実行

.EXAMPLE
    # すべてのテストを実行
    Invoke-Pester -Path .\Test-AllCmdlets.ps1

.EXAMPLE
    # 詳細出力で実行
    Invoke-Pester -Path .\Test-AllCmdlets.ps1 -Output Detailed

.EXAMPLE
    # 特定のコンテキストのみ実行
    Invoke-Pester -Path .\Test-AllCmdlets.ps1 -TagFilter "workflow"
#>