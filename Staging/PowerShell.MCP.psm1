# PowerShell.MCP Module Script
# Provides automatic cleanup when Remove-Module is executed

# On Linux/macOS, PSReadLine interferes with timer events
# Remove it to ensure MCP polling works correctly
if (-not $IsWindows) {
    Remove-Module PSReadLine -ErrorAction SilentlyContinue
}

# Set OnRemove script block to execute cleanup automatically
$ExecutionContext.SessionState.Module.OnRemove = {
    try {
        #Write-Host "[PowerShell.MCP] Module removal detected, starting cleanup..." -ForegroundColor Yellow
        
        # Load and execute MCPCleanup.ps1 from embedded resources
        $assembly = [System.Reflection.Assembly]::GetAssembly([PowerShell.MCP.MCPModuleInitializer])
        $resourceName = "PowerShell.MCP.Resources.MCPCleanup.ps1"
        
        $stream = $assembly.GetManifestResourceStream($resourceName)
        if ($stream) {
            $reader = New-Object System.IO.StreamReader($stream)
            $cleanupScript = $reader.ReadToEnd()
            $reader.Close()
            $stream.Close()
            
            # Execute cleanup script
            Invoke-Expression $cleanupScript
            #Write-Host "[PowerShell.MCP] OnRemove cleanup completed" -ForegroundColor Green
        } else {
            #Write-Warning "[PowerShell.MCP] MCPCleanup.ps1 resource not found"
        }
    }
    catch {
        #Write-Warning "[PowerShell.MCP] Error during module removal cleanup: $($_.Exception.Message)"
    }
}

#Write-Host "[PowerShell.MCP] Module loaded with OnRemove cleanup support" -ForegroundColor Green

<#
.SYNOPSIS
    Gets the path to the PowerShell.MCP.Proxy executable for the current platform.

.DESCRIPTION
    Returns the full path to the platform-specific PowerShell.MCP.Proxy executable.
    Use this path in your MCP client configuration.

.PARAMETER Escape
    If specified, escapes backslashes for use in JSON configuration files.

.EXAMPLE
    Get-MCPProxyPath
    Returns: C:\Program Files\PowerShell\7\Modules\PowerShell.MCP\bin\win-x64\PowerShell.MCP.Proxy.exe

.EXAMPLE
    Get-MCPProxyPath -Escape
    Returns: C:\\Program Files\\PowerShell\\7\\Modules\\PowerShell.MCP\\bin\\win-x64\\PowerShell.MCP.Proxy.exe

.OUTPUTS
    System.String
#>
function Get-MCPProxyPath {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [switch]$Escape
    )

    $moduleBase = $PSScriptRoot
    $binFolder = Join-Path $moduleBase 'bin'

    # Determine RID based on OS and architecture
    $rid = if ($IsWindows) {
        switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
            'X64'  { 'win-x64' }
            'Arm64' { 'win-arm64' }
            default { 'win-x64' }
        }
    }
    elseif ($IsMacOS) {
        switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
            'X64'  { 'osx-x64' }
            'Arm64' { 'osx-arm64' }
            default { 'osx-x64' }
        }
    }
    elseif ($IsLinux) {
        switch ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture) {
            'X64'  { 'linux-x64' }
            'Arm64' { 'linux-arm64' }
            'Arm'   { 'linux-arm' }
            default { 'linux-x64' }
        }
    }
    else {
        throw "Unsupported operating system"
    }

    # Determine executable name
    $exeName = if ($IsWindows) { 'PowerShell.MCP.Proxy.exe' } else { 'PowerShell.MCP.Proxy' }

    $proxyPath = Join-Path $binFolder $rid $exeName

    if (-not (Test-Path $proxyPath)) {
        throw "PowerShell.MCP.Proxy not found at: $proxyPath. Please ensure the module is properly installed for your platform ($rid)."
    }

    if ($Escape) {
        return $proxyPath.Replace('\', '\\')
    }

    return $proxyPath
}


<#
.SYNOPSIS
    Gets information about the MCP client that owns this console.

.DESCRIPTION
    Returns ownership information for the current PowerShell console, including
    whether it is owned by an MCP proxy, the proxy's PID, and the client name
    (e.g., Claude Desktop, Claude Code, VS Code).

.EXAMPLE
    Get-MCPOwner

    Owned      : True
    ProxyPid   : 22208
    ClientName : Claude Desktop

.EXAMPLE
    Get-MCPOwner

    Owned      : False
    ProxyPid   :
    ClientName :

.OUTPUTS
    PSCustomObject with Owned, ProxyPid, and ClientName properties
#>
function Get-MCPOwner {
    [CmdletBinding()]
    [OutputType([PSCustomObject])]
    param()

    $pipeName = [PowerShell.MCP.MCPModuleInitializer]::GetPipeName()

    if (-not $pipeName) {
        return [PSCustomObject]@{
            Owned      = $false
            ProxyPid   = $null
            ClientName = $null
        }
    }

    # Parse pipe name segments
    # Unowned: PowerShell.MCP.Communication.{pwshPid} (4 segments)
    # Owned:   PowerShell.MCP.Communication.{proxyPid}.{pwshPid} (5 segments)
    $segments = $pipeName.Split('.')

    if ($segments.Length -ne 5) {
        return [PSCustomObject]@{
            Owned      = $false
            ProxyPid   = $null
            ClientName = $null
        }
    }

    $proxyPid = [int]$segments[3]

    # Determine client name by traversing process tree
    $clientName = $null
    try {
        $process = Get-Process -Id $proxyPid -ErrorAction SilentlyContinue
        if ($process) {
            # Check parent processes to find MCP client
            $currentProcess = $process
            for ($i = 0; $i -lt 5; $i++) {
                $processName = $currentProcess.ProcessName.ToLower()

                if ($processName -eq 'claude') {
                    $clientName = 'Claude Desktop'
                    break
                }
                elseif ($processName -eq 'code' -or $processName -eq 'code - insiders') {
                    $clientName = 'VS Code'
                    break
                }
                elseif ($processName -match 'cursor') {
                    $clientName = 'Cursor'
                    break
                }

                # Try to get parent process
                try {
                    $parentId = (Get-CimInstance Win32_Process -Filter "ProcessId = $($currentProcess.Id)" -ErrorAction SilentlyContinue).ParentProcessId
                    if ($parentId) {
                        $currentProcess = Get-Process -Id $parentId -ErrorAction SilentlyContinue
                        if (-not $currentProcess) { break }
                    }
                    else { break }
                }
                catch { break }
            }

            # If not identified, use proxy process name
            if (-not $clientName) {
                $clientName = $process.ProcessName
            }
        }
    }
    catch {
        # Ignore errors in process lookup
    }

    return [PSCustomObject]@{
        Owned      = $true
        ProxyPid   = $proxyPid
        ClientName = $clientName
    }
}

Export-ModuleMember -Function Get-MCPProxyPath, Get-MCPOwner
