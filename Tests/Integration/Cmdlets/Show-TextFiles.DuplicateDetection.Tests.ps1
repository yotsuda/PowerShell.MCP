Describe "Show-TextFiles -Contains duplicate detection" {
    BeforeAll {
        $script:testFile = New-TemporaryFile
        "line1
line2
line3
line4
line5" | Set-Content -Path $script:testFile.FullName
    }
    
    AfterAll {
        if (Test-Path $script:testFile.FullName) {
            Remove-Item $script:testFile.FullName -Force
        }
    }
    
    It "連続するマッチ行で重複出力がないこと" {
        $result = Show-TextFiles -Path $script:testFile.FullName -Pattern "line"
        # ヘッダー + 5行のマッチ行 = 6行（コンテキスト行は重複しない）
        $result.Count | Should -Be 6
        
        # 各行が1回だけ出力されていることを確認
        ($result | Where-Object { $_ -match "^\s+1:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+2:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+3:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+4:" }).Count | Should -Be 1
        ($result | Where-Object { $_ -match "^\s+5:" }).Count | Should -Be 1
    }
}
