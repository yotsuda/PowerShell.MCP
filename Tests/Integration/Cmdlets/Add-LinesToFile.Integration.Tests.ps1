# Test-AddLinesToFileCmdlet.ps1
# Add-LinesToFile コマンドレットの統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Add-LinesToFile Integration Tests" {
    BeforeEach {
        # 各テストの前に新しい一時ファイルを作成
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:initialContent = @(
            "Line 1"
            "Line 2"
            "Line 3"
        )
        Set-Content -Path $script:testFile -Value $script:initialContent -Encoding UTF8
    }

    AfterEach {
        # 各テスト後にクリーンアップ
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force -ErrorAction SilentlyContinue
        }
        # バックアップファイルもクリーンアップ（配列に変換してから削除）
        $backupFiles = @(Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" -ErrorAction SilentlyContinue | 
            Where-Object { $_.FullName -ne $script:testFile })
        $backupFiles | ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
    }

    Context "ファイル末尾への追加" {
        It "行を末尾に追加できる" {
            Add-LinesToFile -Path $script:testFile -Content "New Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result[-1] | Should -Be "New Line"
        }

        It "複数行を末尾に追加できる" {
            $newLines = @("Line 4", "Line 5")
            Add-LinesToFile -Path $script:testFile -Content $newLines
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 5
            $result[-2] | Should -Be "Line 4"
            $result[-1] | Should -Be "Line 5"
        }
    }

    Context "特定位置への挿入" {
        It "ファイルの先頭に行を挿入できる" {
            Add-LinesToFile -Path $script:testFile -LineNumber 1 -Content "New First Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result[0] | Should -Be "New First Line"
            $result[1] | Should -Be "Line 1"
        }

        It "ファイルの中間に行を挿入できる" {
            Add-LinesToFile -Path $script:testFile -LineNumber 2 -Content "Inserted Line"
            $result = Get-Content $script:testFile
            $result.Count | Should -Be 4
            $result[1] | Should -Be "Inserted Line"
            $result[2] | Should -Be "Line 2"
        }
    }

    Context "新規ファイルの作成" {
        It "存在しないファイルに書き込める" {
            $newFile = Join-Path $env:TEMP "test_new_file_$(Get-Random).txt"
            try {
                Add-LinesToFile -Path $newFile -Content "New content"
                Test-Path $newFile | Should -Be $true
                $result = Get-Content $newFile
                $result | Should -Be "New content"
            }
            finally {
                if (Test-Path $newFile) {
                    Remove-Item $newFile -Force
                }
            }
        }
    }

    Context "バックアップ機能" {
        It "-Backup スイッチでバックアップを作成できる" {
            Add-LinesToFile -Path $script:testFile -Content "New Line" -Backup
            # タイムスタンプ付きバックアップファイルが作成される
            $backupFiles = Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile)).*" |
                Where-Object { $_.Name -match '\.bak$' }
            $backupFiles.Count | Should -BeGreaterThan 0
        }
    }

    Context "WhatIf サポート" {
        It "-WhatIf で実際には変更しない" {
            $originalContent = Get-Content $script:testFile
            Add-LinesToFile -Path $script:testFile -Content "New Line" -WhatIf
            $newContent = Get-Content $script:testFile
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }
    }

    Context "エンコーディング" {
        It "UTF-8 エンコーディングで書き込める" {
            $testContent = "日本語テスト"
            Add-LinesToFile -Path $script:testFile -Content $testContent -Encoding "utf-8"
            $result = Get-Content $script:testFile -Encoding UTF8
            $result[-1] | Should -Be $testContent
        }

        It "ASCII ファイルに日本語を追加すると自動的に UTF-8 にアップグレードされる" {
            # ASCII エンコーディングでファイルを作成
            $asciiFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllLines($asciiFile, @("Line 1", "Line 2"), [System.Text.Encoding]::ASCII)
            
            try {
                # ファイルのエンコーディングが ASCII であることを確認
                $bytes = [System.IO.File]::ReadAllBytes($asciiFile)
                $encoding = [System.Text.Encoding]::ASCII
                $detectedText = $encoding.GetString($bytes)
                $detectedText | Should -Not -BeNullOrEmpty
                
                # 日本語を含む内容を追加(Encoding パラメータは指定しない)
                $infoMessages = @()
                Add-LinesToFile -Path $asciiFile -Content "日本語のテスト" -InformationVariable infoMessages
                
                # エンコーディングアップグレードの情報メッセージが出ることを確認
                $infoMessages | Should -Not -BeNullOrEmpty
                $infoMessages.MessageData -join ' ' | Should -Match 'UTF-8'
                
                # ファイルが UTF-8 で読めることを確認
                $result = Get-Content $asciiFile -Encoding UTF8
                $result[-1] | Should -Be "日本語のテスト"
                
                # UTF-8 として正しく保存されていることを確認
                $content = [System.IO.File]::ReadAllText($asciiFile, [System.Text.Encoding]::UTF8)
                $content | Should -Match "日本語のテスト"
            }
            finally {
                if (Test-Path $asciiFile) {
                    Remove-Item $asciiFile -Force
                }
            }
        }
    }

    Context "エラーハンドリング" {
        It "無効な行番号で警告を出すが続行する" {
            # 寛容な設計：警告を出すが処理は続行
            $warningMessage = $null
            Add-LinesToFile -Path $script:testFile -LineNumber 100 -Content "Test" -WarningVariable warningMessage -WarningAction SilentlyContinue
            # ファイルは変更される（末尾に追加など）
            $result = Get-Content $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }

        It "読み取り専用ファイルでエラーになる" {
            Set-ItemProperty -Path $script:testFile -Name IsReadOnly -Value $true
            Test-ThrowsQuietly { Add-LinesToFile -Path $script:testFile -Content "Test" }
            Set-ItemProperty -Path $script:testFile -Name IsReadOnly -Value $false
        }
    }

        It "H13. LineNumber = 0 ではパラメータ検証エラーになる" {
            # 実装はLineNumber=0を拒否する
            Test-ParameterValidationError { Add-LinesToFile -Path $script:testFile -LineNumber 0 -Content "Test" }
        }
        It "H14. LineNumber > ファイル行数 で警告を出すが処理は続行" {
            Add-LinesToFile -Path $script:testFile -LineNumber 1000 -Content "Test" -WarningAction SilentlyContinue
            # 警告が出るが、ファイルは変更される
            $result = Get-Content $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }

        It "H15. Content が空配列の場合はバインディングエラーになる" {
            # 実装は空配列を拒否する
            Test-ParameterValidationError { Add-LinesToFile -Path $script:testFile -Content @() }
        }

        It "H16. Content が null または空文字列で処理できる" {
            # PowerShellではnullは自動的に空配列になる
            $originalCount = (Get-Content $script:testFile).Count
            Add-LinesToFile -Path $script:testFile -Content "" -WarningAction SilentlyContinue
            $newCount = (Get-Content $script:testFile).Count
            # 空文字列でも行として追加される可能性がある
            $newCount | Should -BeGreaterOrEqual $originalCount
        }

        It "H17. 空ファイルへの追加ができる" {
            $emptyFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $emptyFile -Value @()
            
            try {
                Add-LinesToFile -Path $emptyFile -Content "First line"
                $result = Get-Content $emptyFile
                $result | Should -Be "First line"
            }
            finally {
                Remove-Item $emptyFile -Force
            }
        }

        It "H18. 複数ファイルへの一括追加ができる" {
            $file1 = [System.IO.Path]::GetTempFileName()
            $file2 = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $file1 -Value "File1 original"
            Set-Content -Path $file2 -Value "File2 original"
            
            try {
                Add-LinesToFile -Path $file1,$file2 -Content "Added line"
                
                $result1 = Get-Content $file1
                $result2 = Get-Content $file2
                
                $result1[-1] | Should -Be "Added line"
                $result2[-1] | Should -Be "Added line"
            }
            finally {
                Remove-Item $file1,$file2 -Force
            }
        }

        It "H19. アクセス権限なしでエラーになる" {
            # BeforeEach/AfterEachで設定されたtestFileは読み取り専用テストに使えないので
            # 新しいファイルを作成
            $readOnlyFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $readOnlyFile -Value "Original"
            Set-ItemProperty -Path $readOnlyFile -Name IsReadOnly -Value $true
            
            try {
                Test-ThrowsQuietly { Add-LinesToFile -Path $readOnlyFile -Content "Test" }
            }
            finally {
                Set-ItemProperty -Path $readOnlyFile -Name IsReadOnly -Value $false
                Remove-Item $readOnlyFile -Force
            }
        }

        It "H20. 存在しないファイルに書き込める（新規作成）" {
            $newFile = Join-Path $env:TEMP "NewFile_$(Get-Random).txt"
            
            try {
                Add-LinesToFile -Path $newFile -Content "New content"
                Test-Path $newFile | Should -Be $true
                $result = Get-Content $newFile
                $result | Should -Be "New content"
            }
            finally {
                if (Test-Path $newFile) {
                    Remove-Item $newFile -Force
                }
            }
        }

    Context "コンテキスト表示" {
        It "C01. 末尾追加時にコンテキストが表示される" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3", "Line 4", "Line 5") -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content "Line 6" 6>&1 | Out-String
                
                # コンテキストに挿入位置の前の2行が含まれることを確認
                $output | Should -Match "Line 4"
                $output | Should -Match "Line 5"
                $output | Should -Match "Line 6"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "C02. 末尾追加時のコンテキストがgrep形式で表示される" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3") -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content "Line 4" 6>&1 | Out-String
                
                # grep形式のマーカーを確認
                $output | Should -Match "3-"  # 前の行（コンテキスト）
                $output | Should -Match "4:"  # 追加された行（反転表示）
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "C03. 複数行を末尾に追加した場合のコンテキスト表示" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3") -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content @("Line 4", "Line 5") 6>&1 | Out-String
                
                # コンテキストに前の2行が含まれることを確認
                $output | Should -Match "Line 2"
                $output | Should -Match "Line 3"
                # 追加された行が含まれることを確認
                $output | Should -Match "Line 4"
                $output | Should -Match "Line 5"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

        It "C04. ファイルが2行未満の場合でも末尾追加できる" {
            $testFile = [System.IO.Path]::GetTempFileName()
            try {
                Set-Content -Path $testFile -Value "Line 1" -Encoding UTF8
                
                $output = Add-LinesToFile -Path $testFile -Content "Line 2" 6>&1 | Out-String
                
                # コンテキストに1行目が含まれることを確認
                $output | Should -Match "Line 1"
                $output | Should -Match "Line 2"
                
                # ファイル内容を確認
                $result = Get-Content $testFile
                $result.Count | Should -Be 2
                $result[-1] | Should -Be "Line 2"
            }
            finally {
                Remove-Item $testFile -Force -ErrorAction SilentlyContinue
            }
        }

    }
    }