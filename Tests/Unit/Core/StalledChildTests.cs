using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;
using PowerShell.MCP.Services;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// Tests the timeout-path diagnostic that names a child stalled on console
/// input (see StalledChild). The whole point of it is to be silent unless the
/// condition really holds — a false positive would tell an AI to re-run a
/// command that was merely slow — so these tests are mostly about NOT firing.
/// </summary>
public class StalledChildTests
{
    private const int STD_INPUT_HANDLE = -10;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    /// <summary>
    /// The detector reads this process's console. A console-less test host has
    /// no children attached to one, so those runs assert only that it stays
    /// quiet and does not throw.
    /// </summary>
    private static bool HostHasConsole =>
        OperatingSystem.IsWindows() && GetConsoleMode(GetStdHandle(STD_INPUT_HANDLE), out _);

    [Fact]
    public void Says_nothing_when_no_child_is_stalled()
    {
        Assert.Null(StalledChild.Describe());
    }

    [Fact]
    public void Says_nothing_about_a_child_that_is_merely_busy()
    {
        if (!HostHasConsole) return;

        // A child attached to this console that is actually working must not be
        // reported: the CPU-movement sample is what separates "blocked" from
        // "slow", and getting that wrong would send an AI to re-run a command
        // that only needed more time.
        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("for /L %i in (1,1,100000000) do @rem");

        using var busy = Process.Start(psi)!;
        try
        {
            // Old enough to clear the minimum-age guard.
            System.Threading.Thread.Sleep(3500);
            Assert.Null(StalledChild.Describe());
        }
        finally
        {
            try { if (!busy.HasExited) busy.Kill(); } catch { }
        }
    }

    [Fact]
    public void Never_throws_and_returns_promptly()
    {
        // It runs inside a timeout response, so it must be cheap and must not
        // be able to turn a timeout into an error.
        var sw = Stopwatch.StartNew();
        var ex = Record.Exception(() => StalledChild.Describe());
        sw.Stop();

        Assert.Null(ex);
        Assert.True(sw.Elapsed.TotalSeconds < 5,
            $"Describe() took {sw.Elapsed.TotalSeconds:F1}s; it sits on the timeout path and must stay cheap.");
    }
}
