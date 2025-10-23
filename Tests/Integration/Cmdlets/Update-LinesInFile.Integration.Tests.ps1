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

        It "ASCII ファイルに日本語を更新すると自動的に UTF-8 にアップグレードされる" {
            # ASCII エンコーディングでファイルを作成
            $asciiFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllLines($asciiFile, @("Line 1", "Line 2", "Line 3"), [System.Text.Encoding]::ASCII)
            
            try {
                # ファイルのエンコーディングが ASCII であることを確認
                $bytes = [System.IO.File]::ReadAllBytes($asciiFile)
                $encoding = [System.Text.Encoding]::ASCII
                $detectedText = $encoding.GetString($bytes)
                $detectedText | Should -Not -BeNullOrEmpty
                
                # 日本語を含む内容で行を更新(Encoding パラメータは指定しない)
                $infoMessages = @()
                Update-LinesInFile -Path $asciiFile -LineRange 2 -Content "日本語の更新テスト" -InformationVariable infoMessages
                
                # エンコーディングアップグレードの情報メッセージが出ることを確認
                $infoMessages | Should -Not -BeNullOrEmpty
                $infoMessages.MessageData -join ' ' | Should -Match 'UTF-8'
                
                # ファイルが UTF-8 で読めることを確認
                $result = Get-Content $asciiFile -Encoding UTF8
                $result[1] | Should -Be "日本語の更新テスト"
                
                # UTF-8 として正しく保存されていることを確認
                $content = [System.IO.File]::ReadAllText($asciiFile, [System.Text.Encoding]::UTF8)
                $content | Should -Match "日本語の更新テスト"
            }
            finally {
                if (Test-Path $asciiFile) {
                    Remove-Item $asciiFile -Force
                }
            }
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
                Get-Item @($script:testFile, $file2) | Update-LinesInFile -LineRange 1 -Content "Updated"
                (Get-Content $script:testFile)[0] | Should -Be "Updated"
                (Get-Content $file2)[0] | Should -Be "Updated"
            }
            finally {
                Remove-Item $file2 -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "メッセージ表示" {
        It "-LineRange 1,-1 で行範囲置換時は正しい行数を表示" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 1,-1 -Content "A","B","C"
            
            # メッセージが "Replaced X line(s)" 形式で、正しい行数が表示されることを確認
            $message = $output | Out-String
            $message | Should -Match "Replaced \d+ line\(s\) with \d+ line\(s\)"
            $message | Should -Not -Match "\d{4,}"  # 4桁以上の数字（int.MaxValueなど）が含まれていないこと
        }

        It "通常の LineRange では従来のメッセージを表示" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content "X","Y","Z"
            
            # メッセージが "Replaced X line(s)" 形式であることを確認
            $message = $output | Out-String
            $message | Should -Match "Replaced \d+ line\(s\)"
        }
        
        It "削除時は Removed メッセージを表示" {
            $output = Update-LinesInFile -Path $script:testFile -LineRange 2,4
            
            $message = $output | Out-String
            $message | Should -Match "Removed \d+ line\(s\)"
        }
    }
}