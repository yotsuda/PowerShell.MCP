<#
.SYNOPSIS
    A wrapper script that runs Pester tests and displays error output concisely

.DESCRIPTION
    Runs Pester tests and filters out verbose error messages to provide
    more readable output.

    What is filtered:
    - Inner exception stack traces (--->, --- End of)
    - System.Management.Automation.*Exception detail lines
    - Duplicate exception messages (ArgumentNullException + MethodInvocationException)
    - Stack traces such as at System.*, at CallSite.*
    - Stack trace lines starting with at $...

.PARAMETER Path
    Path to a test file or directory (default: Integration)

.EXAMPLE
    .\Invoke-PesterConcise.ps1
    # Run all tests in the Integration directory (concise output)

.EXAMPLE
    .\Invoke-PesterConcise.ps1 -Path Integration/Cmdlets/Show-TextFiles.Tests.ps1
    # Run only a specific test file

.EXAMPLE
    .\Invoke-PesterConcise.ps1 -Path Unit
    # Run only the Unit tests

.NOTES
    Used in combination with the PesterConfiguration.psd1 setting (Verbosity = 'Minimal'),
    this script achieves the most concise output.
#>
param([string]$Path = "Integration")

$tempFile = [System.IO.Path]::GetTempFileName()
try {
    Invoke-Pester -Path $Path -PassThru *> $tempFile
    $lines = Get-Content $tempFile
    $skipUntilEnd = $false
    $lastLine = ""
    
    foreach ($line in $lines) {
        # Start of inner exception - skip until the next "--- End of"
        if ($line -match '^\s*--->') {
            $skipUntilEnd = $true
            continue
        }
        if ($skipUntilEnd) {
            if ($line -match '--- End of') { $skipUntilEnd = $false }
            continue
        }

        # Skip unwanted lines
        if ($line -match '^\s*at (System\.|CallSite\.)') { continue }
        if ($line -match 'at <ScriptBlock>') { continue }
        if ($line -match '^\s*at \$') { continue }  # Skip "at $result..." lines
        if ($line -match 'System\.Management\.Automation\.(RuntimeException|MethodInvocationException|ParameterBindingValidationException):') { continue }

        # Skip ArgumentNullException (when the previous line is MethodInvocationException)
        if ($line -match '^\s*ArgumentNullException:' -and $lastLine -match 'MethodInvocationException:') {
            continue
        }
        
        Write-Host $line
        $lastLine = $line
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}