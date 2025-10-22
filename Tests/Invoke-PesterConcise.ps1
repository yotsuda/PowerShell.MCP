<#
.SYNOPSIS
    Pesterテストを実行し、エラー出力を簡潔に表示するラッパースクリプト

.DESCRIPTION
    Pesterテストを実行し、冗長なエラーメッセージをフィルタリングして
    より読みやすい出力を提供します。
    
    フィルタリング内容:
    - 内部例外スタックトレース（--->, --- End of）
    - System.Management.Automation.*Exception の詳細行
    - 重複する例外メッセージ（ArgumentNullException + MethodInvocationException）
    - at System.*, at CallSite.* などのスタックトレース
    - at $... で始まるスタックトレース行

.PARAMETER Path
    テストファイルまたはディレクトリのパス（デフォルト: Integration）

.EXAMPLE
    .\Invoke-PesterConcise.ps1
    # Integration ディレクトリの全テストを実行（簡潔な出力）

.EXAMPLE
    .\Invoke-PesterConcise.ps1 -Path Integration/Cmdlets/Show-TextFile.Tests.ps1
    # 特定のテストファイルのみ実行

.EXAMPLE
    .\Invoke-PesterConcise.ps1 -Path Unit
    # Unit テストのみ実行

.NOTES
    このスクリプトは PesterConfiguration.psd1 の設定（Verbosity = 'Minimal'）
    と組み合わせて使用することで、最も簡潔な出力を実現します。
#>
param([string]$Path = "Integration")

$tempFile = [System.IO.Path]::GetTempFileName()
try {
    Invoke-Pester -Path $Path -PassThru *> $tempFile
    $lines = Get-Content $tempFile
    $skipUntilEnd = $false
    $lastLine = ""
    
    foreach ($line in $lines) {
        # 内部例外開始 - 次の "--- End of" までスキップ
        if ($line -match '^\s*--->') {
            $skipUntilEnd = $true
            continue
        }
        if ($skipUntilEnd) {
            if ($line -match '--- End of') { $skipUntilEnd = $false }
            continue
        }
        
        # 不要な行をスキップ
        if ($line -match '^\s*at (System\.|CallSite\.)') { continue }
        if ($line -match 'at <ScriptBlock>') { continue }
        if ($line -match '^\s*at \$') { continue }  # "at $result..." 行をスキップ
        if ($line -match 'System\.Management\.Automation\.(RuntimeException|MethodInvocationException|ParameterBindingValidationException):') { continue }
        
        # ArgumentNullException をスキップ（前の行が MethodInvocationException の場合）
        if ($line -match '^\s*ArgumentNullException:' -and $lastLine -match 'MethodInvocationException:') {
            continue
        }
        
        Write-Host $line
        $lastLine = $line
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}