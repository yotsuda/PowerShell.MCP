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

function Test-ParameterValidationError {
    <#
    .SYNOPSIS
    パラメータ検証エラーが発生することを検証する
    
    .DESCRIPTION
    PowerShellのパラメータ検証（ValidateRange、ValidateNotNullなど）が
    エラーをスローすることを検証する
    
    .PARAMETER ScriptBlock
    実行するスクリプトブロック
    
    .EXAMPLE
    Test-ParameterValidationError { Add-LinesToFile -Path "file.txt" -LineNumber -1 -Content "test" }
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ScriptBlock]$ScriptBlock
    )
    
    { & $ScriptBlock } | Should -Throw
}

Export-ModuleMember -Function @("New-TestFile", "Remove-TestFile", "Test-ParameterValidationError")