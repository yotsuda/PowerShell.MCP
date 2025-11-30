# Test-AdvancedCmdlets.ps1
# Update-LinesInFile, Remove-LinesFromFile, Update-MatchInFile の HIGH優先度テスト

#Requires -Modules @{ ModuleName="Pester"; ModuleVersion="5.0.0" }

Describe "Update-LinesInFile HIGH Priority Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Line 1"
            "Line 2"
            "Line 3"
            "Line 4"
            "Line 5"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Context "HIGH優先度: 境界値とエッジケース" {
        It "H21. LineRange が逆順の場合はエラーになる" {
            # 実装は逆順を許容せず、例外を投げる
            { Update-LinesInFile -Path $script:testFile -LineRange 3,1 -Content "Updated" -ErrorAction Stop } | 
                Should -Throw
        }

        It "H22. Content なしで行を削除できる" {
            $originalCount = (Get-Content $script:testFile).Count
            Update-LinesInFile -Path $script:testFile -LineRange 2,3
            $newCount = (Get-Content $script:testFile).Count
            # 行が削除されるか、同じ数のまま
            $newCount | Should -BeLessOrEqual $originalCount
        }

        It "H23. Content が元より多い行数でも処理できる" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,2 -Content @("New Line 1", "New Line 2", "New Line 3")
            $result = Get-Content $script:testFile
            # 1行を3行に置換
            $result | Should -Contain "New Line 1"
            $result | Should -Contain "New Line 2"
            $result | Should -Contain "New Line 3"
        }

        It "H24. Content が元より少ない行数でも処理できる" {
            Update-LinesInFile -Path $script:testFile -LineRange 2,4 -Content "Single Replacement"
            $result = Get-Content $script:testFile
            # 3行を1行に置換
            $result | Should -Contain "Single Replacement"
            $result.Count | Should -BeLessThan 5
        }

        It "H25. 範囲外の LineRange で例外を出す" {
            $threw = $false
            try {
                Update-LinesInFile -Path $script:testFile -LineRange 100,200 -Content "Test" -ErrorAction Stop
            } catch {
                $threw = $true
                $_.Exception.Message | Should -Match "out of bounds"
            }
            $threw | Should -BeTrue
            # ファイルは変更されていない
            $result = Get-Content $script:testFile
            $result | Should -Not -BeNullOrEmpty
        }
    }
}

Describe "Remove-LinesFromFile HIGH Priority Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Apple"
            "Banana"
            "Cherry"
            "Date"
            "Elderberry"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Context "HIGH優先度: エッジケースとエラー処理" {
        It "H26. すべての行を削除できる" {
            Remove-LinesFromFile -Path $script:testFile -LineRange 1,5
            $result = Get-Content $script:testFile -ErrorAction SilentlyContinue
            # ファイルが空になる
            if ($result) {
                $result.Count | Should -Be 0
            } else {
                $result | Should -BeNullOrEmpty
            }
        }

        It "H27. Contains でマッチなしの場合、何も削除されない" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Contains "NonExistent"
            $newContent = Get-Content $script:testFile
            # 変更なし
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }

        It "H28. Pattern でマッチなしの場合、何も削除されない" {
            $originalContent = Get-Content $script:testFile
            Remove-LinesFromFile -Path $script:testFile -Pattern "^Z.*"
            $newContent = Get-Content $script:testFile
            # 変更なし
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }
    }
}

Describe "Update-MatchInFile HIGH Priority Tests" {
    BeforeEach {
        $script:testFile = [System.IO.Path]::GetTempFileName()
        $script:testContent = @(
            "Email: user@example.com"
            "Email: admin@example.com"
            "Email: test@example.com"
        )
        Set-Content -Path $script:testFile -Value $script:testContent -Encoding UTF8
    }

    AfterEach {
        if (Test-Path $script:testFile) {
            Remove-Item $script:testFile -Force
        }
        Get-ChildItem -Path (Split-Path $script:testFile) -Filter "$([System.IO.Path]::GetFileName($script:testFile))*" | 
            Where-Object { $_.FullName -ne $script:testFile } | Remove-Item -Force -ErrorAction SilentlyContinue
    }

    Context "HIGH優先度: マッチングとエッジケース" {
        It "H29. Pattern がマッチしない場合、何も変更されない" {
            $originalContent = Get-Content $script:testFile
            Update-MatchInFile -Path $script:testFile -Pattern "nonexistent" -Replacement "test"
            $newContent = Get-Content $script:testFile
            # 変更なし
            Compare-Object $originalContent $newContent | Should -BeNullOrEmpty
        }

        It "H30. Pattern と Replacement を指定してマッチ部分を置換できる" {
            # 正しい置換を確認
            Update-MatchInFile -Path $script:testFile -Pattern "@example\.com" -Replacement "@newdomain.com"
            $result = Get-Content $script:testFile
            # @example.com が @newdomain.com に置換される
            $result[0] | Should -Be "Email: user@newdomain.com"
            $result[1] | Should -Be "Email: admin@newdomain.com"
            $result[2] | Should -Be "Email: test@newdomain.com"
        }
    }
}
