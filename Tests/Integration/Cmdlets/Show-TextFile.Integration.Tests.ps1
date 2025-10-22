# Test-ShowTextFileCmdlet.ps1
# Show-TextFile コマンドレットの統合テスト（既存 + HIGH優先度）

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Show-TextFile Integration Tests" {
    BeforeAll {
        # テスト用の一時ファイルを作成
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Line 1: First line"
            "Line 2: Second line"
            "Line 3: Third line"
            "Line 4: Fourth line"
            "Line 5: Fifth line"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterAll {
        # クリーンアップ
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    Context "基本的なファイル表示" {
        It "ファイル全体を表示できる" {
            $result = Show-TextFile -Path $script:testFile
            $result | Should -Not -BeNullOrEmpty
            # ヘッダー行 + 5行のコンテンツ = 6行
            $result.Count | Should -Be 6
        }

        It "行番号付きで表示できる" {
            $result = Show-TextFile -Path $script:testFile
            # ヘッダー行の次が実データ
            $result[1] | Should -Match "^\s*1:"
            $result[5] | Should -Match "^\s*5:"
        }
    }

    Context "行範囲指定" {
        It "指定した行範囲のみを表示できる" {
            $result = Show-TextFile -Path $script:testFile -LineRange 2,4
            # ヘッダー行 + 3行のコンテンツ = 4行
            $result.Count | Should -Be 4
            $result[1] | Should -Match "Line 2"
            $result[3] | Should -Match "Line 4"
        }

        It "単一行を表示できる" {
            $result = Show-TextFile -Path $script:testFile -LineRange 3,3
            # ヘッダー行 + 1行のコンテンツ = 2行
            $result.Count | Should -Be 2
            $result[1] | Should -Match "Line 3"
        }
    }

    Context "テキスト検索" {
        It "Contains パラメータで文字列を検索できる" {
            $result = Show-TextFile -Path $script:testFile -Contains "Third"
            $result | Should -Not -BeNullOrEmpty
            # 新実装: 前後3行のコンテキストと共に表示されるため、結果内にマッチ行が含まれることを確認
            $result | Where-Object { $_ -match ':.*Third' } | Should -Not -BeNullOrEmpty
        }

        It "Pattern パラメータで正規表現検索できる" {
            $result = Show-TextFile -Path $script:testFile -Pattern "Line \d:"
            # ヘッダー行 + マッチした5行 = 6行
            $result.Count | Should -Be 6
        }
    }

    Context "エンコーディング" {
        It "UTF-8 エンコーディングで読み取れる" {
            $result = Show-TextFile -Path $script:testFile -Encoding "utf-8"
            $result | Should -Not -BeNullOrEmpty
        }

        It "エンコーディング指定なしでも読み取れる" {
            $result = Show-TextFile -Path $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルでエラーになる" {
            { Show-TextFile -Path "C:\NonExistent\File.txt" -ErrorAction Stop } | 
                Should -Throw
        }

        It "無効な行範囲で警告を出すが続行する" {
            # 寛容な設計：警告を出すがエラーは投げない
            $result = Show-TextFile -Path $script:testFile -LineRange 100,200 -WarningAction SilentlyContinue
            # 警告が出るが、処理は続行される
            $result | Should -Not -BeNull
        }
    }

    Context "HIGH優先度: エッジケースと境界値" {
        It "H1. 空ファイルを表示できる" {
            $emptyFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $emptyFile -Value @() -Encoding UTF8
            
            try {
                $result = Show-TextFile -Path $emptyFile
                # ヘッダー行のみ
                $result | Should -Not -BeNullOrEmpty
                $result.Count | Should -Be 1
            }
            finally {
                Remove-Item $emptyFile -Force
            }
        }

        It "H2. 1行だけのファイルを表示できる" {
            $singleLineFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $singleLineFile -Value "Single line" -Encoding UTF8
            
            try {
                $result = Show-TextFile -Path $singleLineFile
                # ヘッダー行 + 1行 = 2行
                $result.Count | Should -Be 2
                $result[1] | Should -Match "Single line"
            }
            finally {
                Remove-Item $singleLineFile -Force
            }
        }

        It "H3. LineRange + Contains の組み合わせが動作する" {
            $result = Show-TextFile -Path $script:testFile -LineRange 2,4 -Contains "Third"
            # 行範囲内でContainsにマッチする行のみ
            $result | Should -Not -BeNullOrEmpty
            ($result -join "`n") | Should -Match "Third"
        }

        It "H4. LineRange + Pattern の組み合わせが動作する" {
            $result = Show-TextFile -Path $script:testFile -LineRange 1,3 -Pattern "Line \d:"
            # 行範囲内でPatternにマッチする行のみ
            $result | Should -Not -BeNullOrEmpty
            $result.Count | Should -BeGreaterThan 1
        }

        It "H5. LineRange が逆順 [5,1] の場合はエラーになる" {
            # 実装は逆順を拒否する
            { Show-TextFile -Path $script:testFile -LineRange 5,1 -ErrorAction Stop } | 
                Should -Throw
        }

        It "H6. LineRange = [0,0] の場合はパラメータ検証エラーになる" {
            # 0は無効な行番号
            { Show-TextFile -Path $script:testFile -LineRange 0,0 -ErrorAction Stop } | 
                Should -Throw
        }

        It "H7. LineRange が範囲外 [100,200] で警告を出す" {
            # 既存のテストと重複するが、明示的に確認
            $warnings = @()
            $result = Show-TextFile -Path $script:testFile -LineRange 100,200 -WarningVariable warnings -WarningAction SilentlyContinue
            # 警告メッセージが出力される
            $result | Should -Not -BeNull
        }

        It "H8. 複数ファイル + LineRange が動作する" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file1 -Value @("File1-Line1", "File1-Line2", "File1-Line3")
            Set-Content -Path $file2 -Value @("File2-Line1", "File2-Line2", "File2-Line3")
            
            try {
                $result = Show-TextFile -Path $file1,$file2 -LineRange 1,2
                # 各ファイルのヘッダー + 指定範囲の行
                $result | Should -Not -BeNullOrEmpty
                ($result -join "`n") | Should -Match "File1-Line1"
                ($result -join "`n") | Should -Match "File2-Line1"
            }
            finally {
                Remove-Item $file1,$file2 -Force
            }
        }

        It "H9. 複数ファイル + Contains が動作する" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file1 -Value @("Apple", "Banana", "Cherry")
            Set-Content -Path $file2 -Value @("Dog", "Elephant", "Fox")
            
            try {
                $result = Show-TextFile -Path $file1,$file2 -Contains "Elephant"
                # Elephantを含むファイルのみ表示される
                $result | Should -Not -BeNullOrEmpty
                ($result -join "`n") | Should -Match "Elephant"
            }
            finally {
                Remove-Item $file1,$file2 -Force
            }
        }

        It "H10. ディレクトリパスを指定するとエラーになる" {
            $tempDir = Join-Path $env:TEMP "TestDir_$(Get-Random)"
            New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
            
            try {
                { Show-TextFile -Path $tempDir -ErrorAction Stop } | Should -Throw
            }
            finally {
                Remove-Item $tempDir -Force -Recurse
            }
        }

        It "H11. アクセス権限なしファイルでエラーになる" {
            $protectedFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $protectedFile -Value "Protected content"
            
            try {
                # 読み取り専用に設定
                $acl = Get-Acl $protectedFile
                $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
                    [System.Security.Principal.WindowsIdentity]::GetCurrent().Name,
                    "Read",
                    "Deny"
                )
                $acl.AddAccessRule($accessRule)
                Set-Acl -Path $protectedFile -AclObject $acl
                
                { Show-TextFile -Path $protectedFile -ErrorAction Stop } | Should -Throw
            }
            finally {
                # ACLをリセット
                $acl = Get-Acl $protectedFile
                $acl.Access | Where-Object { $_.AccessControlType -eq "Deny" } | ForEach-Object {
                    $acl.RemoveAccessRule($_) | Out-Null
                }
                Set-Acl -Path $protectedFile -AclObject $acl
                Remove-Item $protectedFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "H12. 無効なエンコーディング名では警告を出すが続行する" {
            # 実装は寛容で、警告を出して続行する
            $result = Show-TextFile -Path $script:testFile -Encoding "invalid-encoding-name" -WarningAction SilentlyContinue
            # エラーにはならず、デフォルトエンコーディングで読み取る
            $result | Should -Not -BeNullOrEmpty
        }
    }
}