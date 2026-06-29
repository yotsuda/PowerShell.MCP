# Run-AllTests.ps1
# Helper script that runs all tests

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("Unit", "Integration", "All")]
    [string]$TestType = "All",
    
    [Parameter()]
    [switch]$Detailed,
    
    [Parameter()]
    [switch]$CodeCoverage
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "PowerShell.MCP Test Runner" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Check the Pester version
$pesterModule = Get-Module -ListAvailable -Name Pester | Sort-Object Version -Descending | Select-Object -First 1
if ($null -eq $pesterModule) {
    Write-Host "⚠ The Pester module is not installed." -ForegroundColor Yellow
    Write-Host "To install it: Install-Module -Name Pester -Force -SkipPublisherCheck" -ForegroundColor Yellow
    exit 1
}

Write-Host "✓ Pester version: $($pesterModule.Version)" -ForegroundColor Green

# Run C# unit tests
if ($TestType -in @("Unit", "All")) {
    Write-Host "`n--- C# Unit Tests ---" -ForegroundColor Cyan
    $testProjectPath = $scriptRoot
    
    if (Test-Path (Join-Path $testProjectPath "PowerShell.MCP.Tests.csproj")) {
        Push-Location $testProjectPath
        try {
            Write-Host "Running dotnet test..." -ForegroundColor Yellow
            dotnet test --verbosity quiet --nologo
            if ($LASTEXITCODE -ne 0) {
                Write-Host "❌ C# unit tests failed." -ForegroundColor Red
            } else {
                Write-Host "✓ C# unit tests passed." -ForegroundColor Green
            }
        }
        finally {
            Pop-Location
        }
    } else {
        Write-Host "⚠ Test project not found: PowerShell.MCP.Tests.csproj" -ForegroundColor Yellow
    }
}

# Run PowerShell integration tests
if ($TestType -in @("Integration", "All")) {
    Write-Host "`n--- PowerShell Integration Tests ---" -ForegroundColor Cyan
    $integrationTestPath = Join-Path $scriptRoot "Integration"
    
    if (Test-Path $integrationTestPath) {
        $testFiles = Get-ChildItem -Path $integrationTestPath -Filter "*.Tests.ps1"
        
        if ($testFiles.Count -eq 0) {
            Write-Host "⚠ No integration test files found." -ForegroundColor Yellow
        } else {
            Write-Host "Found $($testFiles.Count) test file(s)" -ForegroundColor Yellow
            
            $pesterConfig = @{
                Run = @{
                    Path = $integrationTestPath
                    PassThru = $true
                }
                Output = @{
                    Verbosity = if ($Detailed) { "Detailed" } else { "None" }
                    StackTraceVerbosity = "None"
                    CIFormat = "None"
                }
                Should = @{
                    ErrorAction = 'SilentlyContinue'
                }
            }
            
            if ($CodeCoverage) {
                $pesterConfig.CodeCoverage = @{
                    Enabled = $true
                }
            }
            
            # Tell Pester to look for *.Tests.ps1 files
            $config = New-PesterConfiguration -Hashtable $pesterConfig
            $config.Run.Path = Get-ChildItem -Path $integrationTestPath -Filter "*.Tests.ps1" | Select-Object -ExpandProperty FullName
            # Run the tests (Output.Verbosity = "None" to minimize output)
            $result = Invoke-Pester -Configuration $config
            
            Write-Host "`n--- Test Results ---" -ForegroundColor Cyan
            Write-Host "Total: $($result.TotalCount)" -ForegroundColor White
            Write-Host "Passed: $($result.PassedCount)" -ForegroundColor Green
            Write-Host "Failed: $($result.FailedCount)" -ForegroundColor Red
            Write-Host "Skipped: $($result.SkippedCount)" -ForegroundColor Yellow
            
            if ($result.FailedCount -gt 0) {
                Write-Host "`n❌ There were integration test failures." -ForegroundColor Red
                exit 1
            } else {
                Write-Host "`n✓ All integration tests passed." -ForegroundColor Green
            }
        }
    } else {
        Write-Host "⚠ Integration test path not found: $integrationTestPath" -ForegroundColor Yellow
    }
}

Write-Host "`n==================================" -ForegroundColor Cyan
Write-Host "Test execution completed!" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan

<#
.SYNOPSIS
    Runs all PowerShell.MCP tests.

.DESCRIPTION
    This script runs both the C# unit tests and the PowerShell integration tests.

.PARAMETER TestType
    Specifies the type of tests to run. "Unit", "Integration", or "All" (default).

.PARAMETER Detailed
    Shows detailed output.

.PARAMETER CodeCoverage
    Enables code coverage (PowerShell integration tests only).

.EXAMPLE
    .\Run-AllTests.ps1
    Runs all tests.

.EXAMPLE
    .\Run-AllTests.ps1 -TestType Unit
    Runs only the C# unit tests.

.EXAMPLE
    .\Run-AllTests.ps1 -TestType Integration -Detailed
    Runs only the PowerShell integration tests with detailed output.
#>