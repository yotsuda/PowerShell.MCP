# Test-TextFileContains.Tests.ps1
# Test-TextFileContains コマンドレットの統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Test-TextFileContains Integration Tests" {
    BeforeEach {
        # 各テストの前に新しい一時ファイルを作成
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "# Configuration File"
            "Server: localhost"
            "Port: 8080"
            "Username: admin"
            "Password: secret123"
            "Debug: true"
            "ERROR: Connection timeout"
            "WARNING: Low memory"
            "INFO: System started"
        )
        Set-Content -Path $script:testFile -Value $script:initialContent -Encoding UTF8
    }

    AfterEach {
        # 各テスト後にクリーンアップ
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    Context "テキスト検索（Contains）" {
        It "存在するテキストの場合 True を返す" {
            $result = Test-TextFileContains -Path $script:testFile -Contains "localhost"
            $result | Should -Be $true
        }

        It "存在しないテキストの場合 False を返す" {
            $result = Test-TextFileContains -Path $script:testFile -Contains "NonExistentText"
            $result | Should -Be $false
        }

        It "大文字小文字を区別する（case-sensitive）" {
            $result = Test-TextFileContains -Path $script:testFile -Contains "LOCALHOST"
            $result | Should -Be $false  # "localhost"とは異なるため
        }

        It "部分一致で検索される" {
            $result = Test-TextFileContains -Path $script:testFile -Contains "host"
            $result | Should -Be $true
        }

        It "複数の単語を含む検索" {
            $result = Test-TextFileContains -Path $script:testFile -Contains "Connection timeout"
            $result | Should -Be $true
        }
    }

    Context "正規表現検索（Pattern）" {
        It "正規表現にマッチする場合 True を返す" {
            $result = Test-TextFileContains -Path $script:testFile -Pattern "^Server:"
            $result | Should -Be $true
        }

        It "正規表現にマッチしない場合 False を返す" {
            $result = Test-TextFileContains -Path $script:testFile -Pattern "^NonExistent:"
            $result | Should -Be $false
        }

        It "数値パターンの検索" {
            $result = Test-TextFileContains -Path $script:testFile -Pattern "\d+"
            $result | Should -Be $true
        }

        It "複雑な正規表現パターン" {
            $result = Test-TextFileContains -Path $script:testFile -Pattern "^(ERROR|WARNING|INFO):"
            $result | Should -Be $true
        }

        It "メールアドレスパターンの検索（存在しない）" {
            $result = Test-TextFileContains -Path $script:testFile -Pattern "\w+@\w+\.\w+"
            $result | Should -Be $false
        }

        It "行末パターンの検索" {
            $result = Test-TextFileContains -Path $script:testFile -Pattern "true$"
            $result | Should -Be $true
        }
    }

    Context "範囲内検索（LineRange）" {
        It "指定範囲内に存在する場合 True を返す" {
            $result = Test-TextFileContains -Path $script:testFile -LineRange 1,5 -Contains "admin"
            $result | Should -Be $true
        }

        It "指定範囲外の場合 False を返す" {
            $result = Test-TextFileContains -Path $script:testFile -LineRange 1,3 -Contains "ERROR"
            $result | Should -Be $false
        }

        It "LineRange と Pattern を組み合わせ" {
            $result = Test-TextFileContains -Path $script:testFile -LineRange 2,5 -Pattern "^\w+: \w+"
            $result | Should -Be $true
        }

        It "範囲の最初の行のみ検索" {
            $result = Test-TextFileContains -Path $script:testFile -LineRange 1,1 -Contains "Configuration"
            $result | Should -Be $true
        }

        It "範囲の最後の行のみ検索" {
            $result = Test-TextFileContains -Path $script:testFile -LineRange 9,9 -Contains "System started"
            $result | Should -Be $true
        }
    }

    Context "複数ファイルの検索" {
        It "複数ファイルから条件に合うものを見つける" {
            $file2 = [System.IO.Path]::GetTempFileName()
            $file3 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value "No match here"
            Set-Content -Path $file3 -Value "Found: localhost"
            
            try {
                $result1 = Test-TextFileContains -Path $script:testFile -Contains "localhost"
                $result2 = Test-TextFileContains -Path $file2 -Contains "localhost"
                $result3 = Test-TextFileContains -Path $file3 -Contains "localhost"
                
                $result1 | Should -Be $true
                $result2 | Should -Be $false
                $result3 | Should -Be $true
            }
            finally {
                Remove-Item $file2, $file3 -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "実用的な使用例" {
        It "設定ファイルに特定の設定が存在するか確認" {
            $hasDebug = Test-TextFileContains -Path $script:testFile -Pattern "^Debug: true$"
            $hasDebug | Should -Be $true
        }

        It "ログファイルにエラーが含まれるか確認" {
            $hasError = Test-TextFileContains -Path $script:testFile -Pattern "^ERROR:"
            $hasError | Should -Be $true
        }

        It "認証情報が設定されているか確認" {
            $hasUsername = Test-TextFileContains -Path $script:testFile -Contains "Username:"
            $hasPassword = Test-TextFileContains -Path $script:testFile -Contains "Password:"
            $hasUsername -and $hasPassword | Should -Be $true
        }

        It "特定のポート番号が設定されているか確認" {
            $hasPort = Test-TextFileContains -Path $script:testFile -Pattern "Port: 8080"
            $hasPort | Should -Be $true
        }
    }

    Context "エンコーディング" {
        It "UTF-8ファイルを正しく検索できる" {
            $content = @("日本語テキスト", "English Text", "混在 Mixed")
            Set-Content -Path $script:testFile -Value $content -Encoding UTF8
            $result = Test-TextFileContains -Path $script:testFile -Contains "日本語" -Encoding UTF8
            $result | Should -Be $true
        }

        It "日本語の正規表現検索" {
            Set-Content -Path $script:testFile -Value "名前: 太郎" -Encoding UTF8
            $result = Test-TextFileContains -Path $script:testFile -Pattern "名前: \w+" -Encoding UTF8
            $result | Should -Be $true
        }
    }

    Context "空ファイルと特殊ケース" {
        It "空ファイルの場合 False を返す" {
            Set-Content -Path $script:testFile -Value "" -Encoding UTF8
            $result = Test-TextFileContains -Path $script:testFile -Contains "anything"
            $result | Should -Be $false
        }

        It "空行のみのファイル" {
            Set-Content -Path $script:testFile -Value @("", "", "") -Encoding UTF8
            $result = Test-TextFileContains -Path $script:testFile -Contains "text"
            $result | Should -Be $false
        }

        It "ホワイトスペースのみの検索" {
            Set-Content -Path $script:testFile -Value "    " -Encoding UTF8
            $result = Test-TextFileContains -Path $script:testFile -Pattern "^\s+$"
            $result | Should -Be $true
        }
    }

    Context "パフォーマンス - 大きなファイル" {
        It "大きなファイルでも検索できる" {
            $largeContent = 1..1000 | ForEach-Object { "Line $($_): Some content here" }
            $largeContent += "FOUND: The needle in the haystack"
            $largeContent += 1001..2000 | ForEach-Object { "Line $($_): More content" }
            Set-Content -Path $script:testFile -Value $largeContent -Encoding UTF8
            
            $result = Test-TextFileContains -Path $script:testFile -Contains "needle"
            $result | Should -Be $true
        }

        It "大きなファイルで見つからない場合も適切に処理" {
            $largeContent = 1..1000 | ForEach-Object { "Line $($_): Content" }
            Set-Content -Path $script:testFile -Value $largeContent -Encoding UTF8
            
            $result = Test-TextFileContains -Path $script:testFile -Contains "NonExistent"
            $result | Should -Be $false
        }
    }

    Context "エラーハンドリング" {
        It "存在しないファイルでエラーになる" {
            { Test-TextFileContains -Path "C:\NonExistent\file.txt" -Contains "test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "Contains と Pattern を同時に指定するとエラーになる" {
            { Test-TextFileContains -Path $script:testFile -Contains "test" -Pattern "test" -ErrorAction Stop } | 
                Should -Throw
        }

        It "Contains も Pattern も指定しないと false を返す" {
            $result = Test-TextFileContains -Path $script:testFile
            $result | Should -Be $false
        }

        It "無効な正規表現でエラーになる" {
            { Test-TextFileContains -Path $script:testFile -Pattern "[invalid(" -ErrorAction Stop } | 
                Should -Throw
        }

        It "範囲外の行番号で false を返す" {
            $result = Test-TextFileContains -Path $script:testFile -LineRange 100,200 -Contains "test" -WarningAction SilentlyContinue
            $result | Should -Be $false
        }
    }

    Context "条件分岐での使用" {
        It "If文で使用できる" {
            if (Test-TextFileContains -Path $script:testFile -Contains "ERROR") {
                $errorFound = $true
            } else {
                $errorFound = $false
            }
            $errorFound | Should -Be $true
        }

        It "Where-Objectで使用できる" {
            $files = @($script:testFile)
            $filesWithError = $files | Where-Object { Test-TextFileContains -Path $_ -Contains "ERROR" }
            $filesWithError.Count | Should -Be 1
        }
    }

    Context "パイプライン入力" {
        It "パイプラインから複数のファイルを検索できる" {
            $file2 = [System.IO.Path]::GetTempFileName()
            $file3 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file2 -Value "Contains ERROR"
            Set-Content -Path $file3 -Value "No issues here"
            
            try {
                $results = @($script:testFile, $file2, $file3) | ForEach-Object {
                    [PSCustomObject]@{
                        File = $_
                        HasError = Test-TextFileContains -Path $_ -Contains "ERROR"
                    }
                }
                
                $results[0].HasError | Should -Be $true
                $results[1].HasError | Should -Be $true
                $results[2].HasError | Should -Be $false
            }
            finally {
                Remove-Item $file2, $file3 -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
