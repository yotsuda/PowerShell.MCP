using PowerShell.MCP.Proxy.Tools;
using Xunit;

namespace PowerShell.MCP.Tests;

/// <summary>
/// close_console's answer for a PID this session holds no pipe for. It used to
/// be "not a console owned by this session" in every case, which reads as an
/// ownership fault when the console had simply exited, most often an idle
/// standby console that closed itself minutes before the call.
/// </summary>
public class CloseConsoleRefusalTests
{
    [Fact]
    public void ExitedConsole_IsReportedAsGone_NotAsAnOwnershipRefusal()
    {
        var message = PowerShellTools.BuildCloseRefusal(9760, "18872", processRunning: false, isMcpConsole: false);

        Assert.Contains("no longer running", message);
        Assert.Contains("already exited", message);
        Assert.DoesNotContain("Refusing", message);
        Assert.DoesNotContain("not a PowerShell.MCP console", message);
    }

    [Fact]
    public void ExitedConsole_StillListsTheConsolesThisSessionOwns()
    {
        var message = PowerShellTools.BuildCloseRefusal(9760, "18872", processRunning: false, isMcpConsole: false);

        Assert.Contains("18872", message);
    }

    [Fact]
    public void ForeignMcpConsole_IsRefusedAsBelongingToSomeoneElse()
    {
        var message = PowerShellTools.BuildCloseRefusal(6244, "18872", processRunning: true, isMcpConsole: true);

        Assert.Contains("is a PowerShell.MCP console", message);
        Assert.Contains("another session", message);
        Assert.Contains("Refusing", message);
    }

    [Fact]
    public void ArbitraryProcess_IsRefusedAsNotAnMcpConsole()
    {
        var message = PowerShellTools.BuildCloseRefusal(4242, "none", processRunning: true, isMcpConsole: false);

        Assert.Contains("not a PowerShell.MCP console owned by this session", message);
        Assert.Contains("Refusing", message);
    }

    [Fact]
    public void RunningMcpConsole_IsNeverDescribedAsGone()
    {
        var message = PowerShellTools.BuildCloseRefusal(6244, "18872", processRunning: true, isMcpConsole: true);

        Assert.DoesNotContain("no longer running", message);
    }
}
