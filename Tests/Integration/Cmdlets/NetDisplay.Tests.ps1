# Test Net Display in Summary Messages

Describe "Net Display in Summary Messages" {
    BeforeAll {
        $script:testDir = Join-Path ([System.IO.Path]::GetTempPath()) "NetDisplayTests_$(Get-Random)"
        New-Item -Path $script:testDir -ItemType Directory -Force | Out-Null
    }

    AfterAll {
        if (Test-Path $script:testDir) {
            Remove-Item $script:testDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context "Add-LinesToFile net display" {
        It "新規ファイル作成時に (net: +n) を表示" {
            $testFile = Join-Path $script:testDir "new_file_add.txt"
            $output = Add-LinesToFile -Path $testFile -Content @("Line 1", "Line 2") | Out-String
            
            $output | Should -Match 'net: \+2'
        }

        It "既存ファイルへの追加時に (net: +n) を表示" {
            $testFile = Join-Path $script:testDir "existing_file_add.txt"
            Set-Content -Path $testFile -Value "Original"
            
            $output = Add-LinesToFile -Path $testFile -Content "Added line" | Out-String
            
            $output | Should -Match 'net: \+1'
        }
    }

    Context "Remove-LinesFromFile net display" {
        It "行削除時に (net: -n) を表示" {
            $testFile = Join-Path $script:testDir "remove_test.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")
            
            $output = Remove-LinesFromFile -Path $testFile -LineRange 1,2 | Out-String
            
            $output | Should -Match 'net: -2'
        }
    }

    Context "Update-LinesInFile net display" {

        It "行削除時に (net: -n) を表示" {
            $testFile = Join-Path $script:testDir "delete_lines.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2", "Line 3")
            
            $output = Update-LinesInFile -Path $testFile -LineRange 1,3 -Content @() | Out-String
            
            $output | Should -Match 'net: -3'
        }

        It "行更新時に (net: +x/-y) を表示" {
            $testFile = Join-Path $script:testDir "update_lines.txt"
            Set-Content -Path $testFile -Value @("Line 1", "Line 2")
            
            $output = Update-LinesInFile -Path $testFile -LineRange 1,2 -Content @("New 1", "New 2", "New 3") | Out-String
            
            # 2行削除、3行追加なので net: +1
            $output | Should -Match 'net: \+1'
        }
    }
}