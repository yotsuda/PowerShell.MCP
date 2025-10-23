param(
    [Parameter(Mandatory)]
    [string[]]$TestFiles
)

foreach ($file in $TestFiles) {
    if (!(Test-Path $file)) {
        Write-Warning "File not found: $file"
        continue
    }
    
    Write-Host "Processing: $file" -ForegroundColor Cyan
    
    # バックアップ作成
    $backupFile = "$file.before-silent-errors"
    Copy-Item $file $backupFile -Force
    
    $content = Get-Content $file -Raw
    $originalContent = $content
    
    # パターン1: 単一行の { ... } | Should -Throw "..."
    $pattern1 = '\{\s*([^\}]+?)\s*\}\s*\|\s*Should\s+-Throw\s+"([^"]+)"'
    
    $content = [regex]::Replace($content, $pattern1, {
        param($m)
        $cmd = $m.Groups[1].Value.Trim()
        $msg = $m.Groups[2].Value
        
        $indent = "            "
        return "`$err = `$null`n$indent$cmd -ErrorVariable err -ErrorAction SilentlyContinue`n$indent`$err.Count | Should -BeGreaterThan 0`n$indent`$err[0].Exception.Message | Should -BeLike `"$msg`""
    })
    
    # パターン2: 複数行の { ... } | \n Should -Throw "..."
    $pattern2 = '\{\s*([^\}]+?)\s*\}\s*\|\s*\r?\n\s*Should\s+-Throw\s+"([^"]+)"'
    
    $content = [regex]::Replace($content, $pattern2, {
        param($m)
        $cmd = $m.Groups[1].Value.Trim()
        $msg = $m.Groups[2].Value
        
        $indent = "            "
        return "`$err = `$null`n$indent$cmd -ErrorVariable err -ErrorAction SilentlyContinue`n$indent`$err.Count | Should -BeGreaterThan 0`n$indent`$err[0].Exception.Message | Should -BeLike `"$msg`""
    })
    
    if ($content -ne $originalContent) {
        $content | Set-Content $file -Encoding UTF8 -NoNewline
        Write-Host "  ✅ Updated" -ForegroundColor Green
    } else {
        Write-Host "  ⏭️  No changes needed" -ForegroundColor Gray
        Remove-Item $backupFile
    }
}

Write-Host "`nDone! Backups saved with .before-silent-errors extension" -ForegroundColor Green
