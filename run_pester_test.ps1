Import-Module "C:\MyProj\PowerShell.MCP\PowerShell.MCP\bin\Release\net9.0\PowerShell.MCP.dll" -Force
Import-Module Pester -MinimumVersion 5.0.0

$config = New-PesterConfiguration
$config.Run.Path = "C:\MyProj\PowerShell.MCP\Tests\Integration\BlankLineSeparation.Tests.ps1"
$config.Output.Verbosity = "Detailed"

Invoke-Pester -Configuration $config
