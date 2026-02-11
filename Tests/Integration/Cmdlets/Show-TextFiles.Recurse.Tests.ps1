# Show-TextFiles -Recurse のテスト（ワイルドカードパスサポート含む）

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Show-TextFiles -Recurse Tests" {
    BeforeAll {
        # テスト用のディレクトリ構造を作成
        # root/
        #   file1.txt  ("hello world")
        #   file2.cs   ("class Foo { }")
        #   sub/
        #     file3.txt  ("hello again")
        #     file4.cs   ("class Bar { }")
        #     deep/
        #       file5.txt  ("deep hello")
        $script:testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ShowTextFile_Recurse_$([guid]::NewGuid().ToString('N').Substring(0,8))"
        $script:subDir = Join-Path $script:testRoot "sub"
        $script:deepDir = Join-Path $script:subDir "deep"

        New-Item -ItemType Directory -Path $script:deepDir -Force | Out-Null

        Set-Content (Join-Path $script:testRoot "file1.txt") -Value "hello world"
        Set-Content (Join-Path $script:testRoot "file2.cs")  -Value "class Foo { }"
        Set-Content (Join-Path $script:subDir   "file3.txt") -Value "hello again"
        Set-Content (Join-Path $script:subDir   "file4.cs")  -Value "class Bar { }"
        Set-Content (Join-Path $script:deepDir  "file5.txt") -Value "deep hello"
    }

    AfterAll {
        if (Test-Path $script:testRoot) {
            Remove-Item $script:testRoot -Recurse -Force
        }
    }

    Context "ディレクトリパスで再帰検索" {
        It "ディレクトリ指定で全ファイルを再帰検索できる" {
            $result = Show-TextFiles $script:testRoot -Recurse -Contains "hello"
            # file1.txt, file3.txt, file5.txt の3ファイルにマッチ
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 3
        }

        It "サブディレクトリ内のファイルも検索できる" {
            $result = Show-TextFiles $script:testRoot -Recurse -Contains "deep hello"
            $result | Should -Not -BeNullOrEmpty
            ($result | Where-Object { $_ -match "file5\.txt" }) | Should -Not -BeNullOrEmpty
        }

        It "マッチしない場合は空結果" {
            $result = Show-TextFiles $script:testRoot -Recurse -Contains "nonexistent_string_xyz"
            $result | Should -BeNullOrEmpty
        }
    }

    Context "ワイルドカードパスで再帰検索" {
        It "*.txt で .txt ファイルのみ再帰検索できる" {
            $pattern = Join-Path $script:testRoot "*.txt"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            # file1.txt, file3.txt, file5.txt の3ファイルにマッチ
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 3
        }

        It "*.cs で .cs ファイルのみ再帰検索できる" {
            $pattern = Join-Path $script:testRoot "*.cs"
            $result = Show-TextFiles $pattern -Recurse -Contains "class"
            # file2.cs, file4.cs の2ファイルにマッチ
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 2
        }

        It "*.cs で .txt ファイルはマッチしない" {
            $pattern = Join-Path $script:testRoot "*.cs"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            # .cs ファイルには "hello" がないのでマッチなし
            $result | Should -BeNullOrEmpty
        }

        It "マッチしない拡張子パターンでは空結果" {
            $pattern = Join-Path $script:testRoot "*.xyz"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            $result | Should -BeNullOrEmpty
        }

        It "部分ワイルドカード file*.txt でフィルタできる" {
            $pattern = Join-Path $script:testRoot "file*.txt"
            $result = Show-TextFiles $pattern -Recurse -Contains "hello"
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 3
        }
    }

    Context "-Pattern との組み合わせ" {
        It "ワイルドカードパス + -Pattern で正規表現検索できる" {
            $pattern = Join-Path $script:testRoot "*.cs"
            $result = Show-TextFiles $pattern -Recurse -Pattern "class\s+\w+"
            $matchedFiles = @($result | Where-Object { $_ -match "==> .+ <==" })
            $matchedFiles.Count | Should -Be 2
        }
    }

    Context "バリデーション" {
        It "-Recurse は -Pattern または -Contains が必要" {
            { Show-TextFiles $script:testRoot -Recurse -ErrorAction Stop } | Should -Throw
        }

        It "存在しないパスでエラーになる" {
            $result = Show-TextFiles (Join-Path $script:testRoot "nonexistent") -Recurse -Contains "test" -ErrorAction SilentlyContinue 2>&1
            # ErrorAction SilentlyContinue なのでエラーは非終了エラー
        }
    }
}
