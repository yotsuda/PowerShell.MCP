using PowerShell.MCP;
using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// Tests the graceful-degradation path added in 1.10.1: when the embedded
/// polling engine cannot start (e.g. a transient AMSI/antivirus block), the
/// module must stay alive with EngineReady=false, surface actionable guidance,
/// and fast-fail commands instead of hanging until timeout.
///
/// The dotnet-test host has no PowerShell runspace, so ScriptBlock.Create().Invoke()
/// inside TryStartEngine throws — which is exactly the failure shape we want to
/// assert degrades cleanly. (We therefore cannot test the success path here; that
/// is covered by the module-import step in the cross-platform CI job.)
/// </summary>
public class EngineLifecycleTests
{
    [Fact]
    public void TryStartEngine_WithoutRunspace_DegradesGracefully()
    {
        var started = MCPModuleInitializer.TryStartEngine();

        Assert.False(started);
        Assert.False(MCPModuleInitializer.EngineReady);
        Assert.False(string.IsNullOrEmpty(MCPModuleInitializer.LastEngineErrorMessage));
    }

    [Fact]
    public void GetEngineNotReadyMessage_IsActionable()
    {
        var msg = MCPModuleInitializer.GetEngineNotReadyMessage();

        // Must tell the user how to recover and why it happened.
        Assert.Contains("Restart-MCPServer", msg);
        Assert.Contains("AMSI", msg);
    }

    [Fact]
    public void GetEngineNotReadyMessage_IncludesLastError_AfterFailedStart()
    {
        // A failed start records the underlying exception; the guidance message
        // should append it so the user can distinguish AMSI from CLM/WDAC.
        MCPModuleInitializer.TryStartEngine(); // fails (no runspace), sets LastError

        var msg = MCPModuleInitializer.GetEngineNotReadyMessage();

        Assert.Contains("Last error:", msg);
    }

    [Fact]
    public void ExecuteSilentCommand_EngineNotReady_FastFailsWithGuidance()
    {
        // Force the not-ready state (no runspace => TryStartEngine leaves it false).
        MCPModuleInitializer.TryStartEngine();
        Assert.False(MCPModuleInitializer.EngineReady);

        // The guard must short-circuit BEFORE WaitForResult; otherwise this would
        // block the full timeout. Equality with the canonical message proves the
        // fast-fail path was taken rather than the command being stashed.
        var result = McpServerHost.ExecuteSilentCommand("Get-Date");

        Assert.Equal(MCPModuleInitializer.GetEngineNotReadyMessage(), result);
        Assert.Contains("Restart-MCPServer", result);
    }
}
