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

function Test-ThrowsQuietly {
    <#
    .SYNOPSIS
    例外がスローされることを検証するが、エラー出力を完全に抑制する
    
    .DESCRIPTION
    Pester テストで Should -Throw の代わりに使用することで、
    大量のエラーメッセージとスタックトレースの出力を抑制し、
    トークン消費を大幅に削減する
    
    .PARAMETER ScriptBlock
    実行するスクリプトブロック
    
    .PARAMETER ExpectedMessage
    期待されるエラーメッセージ（オプション）。指定した場合、メッセージの一致を検証する
    
    .EXAMPLE
    Test-ThrowsQuietly { Add-LinesToFile -Path "invalid" -LineNumber -1 -Content "test" }
    
    .EXAMPLE
    Test-ThrowsQuietly { Show-TextFile -Path "missing.txt" } -ExpectedMessage "File not found"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ScriptBlock]$ScriptBlock,
        [Parameter(Mandatory = $false)]
        [string]$ExpectedMessage
    )
    
    $caught = $false
    $exceptionMessage = $null
    
    # エラーレコードをクリア
    $Error.Clear()
    
    # ErrorActionPreference を Stop に設定して非終了エラーも例外にする
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Stop'
    
    # すべてのコマンドに -ErrorAction Stop を適用
    $previousDefaultParameters = $PSDefaultParameterValues.Clone()
    $PSDefaultParameterValues['*:ErrorAction'] = 'Stop'
    
    try {
        # 出力を完全に抑制（2>&1 は使わない - $Error を保持するため）
        $null = & $ScriptBlock *>&1
    }
    catch {
        $caught = $true
        $exceptionMessage = $_.Exception.Message
    }
    finally {
        # ErrorActionPreference を元に戻す
        $ErrorActionPreference = $previousErrorActionPreference
        
        # PSDefaultParameterValues を元に戻す
        $PSDefaultParameterValues.Clear()
        foreach ($key in $previousDefaultParameters.Keys) {
            $PSDefaultParameterValues[$key] = $previousDefaultParameters[$key]
        }
    }
    
    # catch されなかったが $Error にエラーが追加された場合もチェック
    if (-not $caught -and $Error.Count -gt 0) {
        $caught = $true
        $exceptionMessage = $Error[0].Exception.Message
    }
    
    # エラーレコードを再度クリア
    $Error.Clear()
    
    # 例外がスローされたことを検証
    $caught | Should -BeTrue -Because "Expected an exception to be thrown"
    
    # 期待されるメッセージの検証（オプション）
    if ($ExpectedMessage) {
        $exceptionMessage | Should -Match $ExpectedMessage
    }
}

Export-ModuleMember -Function @("New-TestFile", "Remove-TestFile", "Test-ThrowsQuietly")