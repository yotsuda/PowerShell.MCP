# TestHelpers.psm1

function New-TestFile {
    param([object[]]$Content = @(), [string]$Encoding = "UTF8")
    $tempFile = [System.IO.Path]::GetTempFileName()
    if ($Content.Count -gt 0) {
        $Content | Out-File -FilePath $tempFile -Encoding $Encoding -Force
    }
    return $tempFile
}

function Remove-TestFile {
    param([string[]]$Path)
    foreach ($p in $Path) {
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
    }
}

Export-ModuleMember -Function @("New-TestFile", "Remove-TestFile")
