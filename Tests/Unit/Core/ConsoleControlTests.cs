using System.Diagnostics;
using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// ConsoleControl.EndSurvivors: what a Windows cancel does with the command's
/// processes that outlive the Ctrl+C (see the report in
/// docs/Issue-Console-Stuck-After-Cancel-2026-11-19.md).
/// </summary>
public class ConsoleControlTests
{
    private static Process StartQuiet(string file, string args)
        => Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true })!;

    [Fact]
    public void EndSurvivors_EndsAProcessStillRunningAfterTheGrace_AndNamesIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var p = StartQuiet("ping.exe", "-n 60 127.0.0.1");
        try
        {
            var ended = ConsoleControl.EndSurvivors(new[] { p.Id }, graceMs: 200);

            Assert.True(p.WaitForExit(5000), "the survivor must have been ended");
            var only = Assert.Single(ended);
            Assert.Equal($"PING (pid {p.Id})", only, ignoreCase: true);
        }
        finally { try { p.Kill(); } catch { } }
    }

    [Fact]
    public void EndSurvivors_LeavesOutAProcessThatExitsWithinTheGrace()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var p = StartQuiet("cmd.exe", "/d /c exit 0");

        var ended = ConsoleControl.EndSurvivors(new[] { p.Id }, graceMs: 3000);

        Assert.Empty(ended);
        Assert.True(p.HasExited);
    }

    [Fact]
    public void EndSurvivors_WithNothingToDo_ReturnsAtOnce()
    {
        var sw = Stopwatch.StartNew();
        Assert.Empty(ConsoleControl.EndSurvivors(Array.Empty<int>()));
        Assert.True(sw.ElapsedMilliseconds < 500);
    }
}
