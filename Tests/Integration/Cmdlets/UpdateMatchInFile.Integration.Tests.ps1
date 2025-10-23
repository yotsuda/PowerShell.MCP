# Update-MatchInFile コマンドレットの統合テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-MatchInFile Integration Tests" {
    BeforeAll {
        # テスト用の一時ファイルを作成
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterEach {
        # 各テスト後にファイルをクリーンアップ
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        $script:testFile = [System.IO.Path]::GetTempFileName()
    }

    AfterAll {
        # 最終クリーンアップ
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
    }

    Context "空文字列での置換（削除）" {
        It "Contains パラメータで空文字列指定時、マッチしたテキストを削除できる" {
            # Arrange
            Set-Content -Path $script:testFile -Value "Server=localhost:8080" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -Contains ":8080" -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "Server=localhost"
        }

        It "Pattern パラメータで空文字列指定時、マッチしたテキストを削除できる" {
            # Arrange
            Set-Content -Path $script:testFile -Value "Price: `$99.99 (tax included)" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -Pattern '\$[\d.]+\s*' -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "Price: (tax included)"
        }

        It "複数箇所のマッチをすべて削除できる（Contains）" {
            # Arrange
            Set-Content -Path $script:testFile -Value "test1 DEBUG test2 DEBUG test3" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -Contains "DEBUG " -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "test1 test2 test3"
        }

        It "複数箇所のマッチをすべて削除できる（Pattern）" {
            # Arrange
            Set-Content -Path $script:testFile -Value "abc123def456ghi789" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -Pattern '\d+' -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            $result | Should -BeExactly "abcdefghi"
        }
    }

    Context "Replacement パラメータの必須チェック" $err = $null
            It "Contains を指定して Replacement を省略するとエラーになる" {
            # Arrange
            Set-Content -Path $script:testFile -Value "test content" -Encoding UTF8 -NoNewline
            
            # Act & Assert
            { Update-MatchInFile -Path $script:testFile -Contains "test" -ErrorVariable err -ErrorAction SilentlyContinue
            $err.Count | Should -BeGreaterThan 0
            $err[0].Exception.Message | Should -BeLike "*Both -Contains and -Replacement must be specified together*"
        }

        It "Pattern を指定して Replacement を省略するとエラーになる" $err = $null
            # Arrange
            Set-Content -Path $script:testFile -Value "test123" -Encoding UTF8 -NoNewline
            
            # Act & Assert
            { Update-MatchInFile -Path $script:testFile -Pattern '\d+' -ErrorVariable err -ErrorAction SilentlyContinue
            $err.Count | Should -BeGreaterThan 0
            $err[0].Exception.Message | Should -BeLike "*Both -Pattern and -Replacement must be specified together*"
        }

        It "Replacement のみ指定するとエラーになる" $err = $null
            # Arrange
            Set-Content -Path $script:testFile -Value "test content" -Encoding UTF8 -NoNewline
            
            # Act & Assert
            { Update-MatchInFile -Path $script:testFile -Replacement "new" -ErrorVariable err -ErrorAction SilentlyContinue
            $err.Count | Should -BeGreaterThan 0
            $err[0].Exception.Message | Should -BeLike "*Either -Contains*or -Pattern*must be specified*"
        }
    }

    Context "エンコーディング処理" {
        It "空文字列での置換後もエンコーディングが保持される（UTF-8）" {
            # Arrange
            Set-Content -Path $script:testFile -Value "日本語テキスト DEBUG 終了" -Encoding UTF8 -NoNewline
            
            # Act
            Update-MatchInFile -Path $script:testFile -Contains "DEBUG " -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw -Encoding UTF8
            $result | Should -BeExactly "日本語テキスト 終了"
        }

        It "空文字列での置換後もエンコーディングが保持される（Shift_JIS）" {
            # Arrange
            $content = "日本語テキスト DEBUG 終了"
            [System.IO.File]::WriteAllText($script:testFile, $content, [System.Text.Encoding]::GetEncoding("Shift_JIS"))
            
            # Act
            Update-MatchInFile -Path $script:testFile -Contains "DEBUG " -Replacement ""
            
            # Assert
            $result = [System.IO.File]::ReadAllText($script:testFile, [System.Text.Encoding]::GetEncoding("Shift_JIS"))
            $result | Should -BeExactly "日本語テキスト 終了"
        }
    }

    Context "改行の保持" {
        It "末尾の改行がある場合、削除後も保持される" {
            # Arrange
            # "Line1", "DEBUG", "Line2" の3行にして、2行目の "DEBUG" を削除する
            Set-Content -Path $script:testFile -Value @("Line1", "DEBUG", "Line2") -Encoding UTF8
            
            # Act
            Update-MatchInFile -Path $script:testFile -Contains "DEBUG" -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            # DEBUGが削除され、空行が残る。Windowsでは CRLF になる
            $result | Should -BeExactly "Line1`r`n`r`nLine2`r`n"
        }

        It "末尾の改行がない場合、削除後も改行なしのまま" {
            # Arrange
            [System.IO.File]::WriteAllText($script:testFile, "Line1`r`nDEBUG`r`nLine2", [System.Text.Encoding]::UTF8)
            
            # Act
            Update-MatchInFile -Path $script:testFile -Contains "DEBUG" -Replacement ""
            
            # Assert
            $result = Get-Content -Path $script:testFile -Raw
            # DEBUGが削除され、空行が残る
            $result | Should -BeExactly "Line1`r`n`r`nLine2"
        }
    }
}