# PowerShell.MCP Module Script
# Provides automatic cleanup when Remove-Module is executed

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
