try {
    # A remote listener, if any, goes with the module: it would otherwise keep
    # serving a proxy on another machine from a runspace with no engine.
    try { [void][PowerShell.MCP.Services.RemoteConsoleListener]::Stop('module removed', $true) } catch {}

    $eventSubscriber = Get-EventSubscriber -SourceIdentifier MCP_Poll -ErrorAction SilentlyContinue
    if ($eventSubscriber) {
        Unregister-Event -SourceIdentifier MCP_Poll -ErrorAction SilentlyContinue
        #Write-Host '[PowerShell.MCP] Event handler unregistered' -ForegroundColor Green
    } else {
        #Write-Host '[PowerShell.MCP] No event handler found for MCP_Poll' -ForegroundColor Yellow
    }

    if (Test-Path Variable:global:McpTimer) {
        $global:McpTimer.Stop()
        $global:McpTimer.Dispose()
        Remove-Variable -Name McpTimer -Scope Global -ErrorAction SilentlyContinue
        #Write-Host '[PowerShell.MCP] Timer disposed and variable removed' -ForegroundColor Green
    } else {
        #Write-Host '[PowerShell.MCP] No McpTimer variable found' -ForegroundColor Yellow
    }

    #Write-Host '[PowerShell.MCP] Cleanup completed successfully' -ForegroundColor Green
} catch {
    #Write-Warning "[PowerShell.MCP] Cleanup error: $($_.Exception.Message)"
}
