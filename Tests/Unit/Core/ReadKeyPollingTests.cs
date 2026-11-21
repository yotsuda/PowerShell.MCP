using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// On Linux and macOS (seen under remote control, after Esc) PSReadLine's key
/// thread sat in Console.ReadKey holding .NET's stdin lock, and PSReadLine's
/// idle loop, reading CursorTop under the
/// same lock, stopped until a key was pressed -- taking the engine's heartbeat
/// and every AI command with it. The replacement key method waits by polling
/// KeyAvailable and reads only once a key is there.
/// </summary>
public class ReadKeyPollingTests
{
    private static readonly ConsoleKeyInfo KeyA = new('a', ConsoleKey.A, false, false, false);

    [Fact]
    public void Waits_by_polling_and_reads_only_once_a_key_is_there()
    {
        var checks = 0;
        var sleeps = new List<int>();
        var reads = 0;

        var key = ReadKeyPolling.ReadKey(
            intercept: true,
            keyAvailable: () => ++checks >= 4,
            readKey: _ => { reads++; Assert.True(checks >= 4, "ReadKey ran before a key was available"); return KeyA; },
            sleep: sleeps.Add);

        Assert.Equal(KeyA, key);
        Assert.Equal(1, reads);
        Assert.Equal(3, sleeps.Count);
        Assert.All(sleeps, ms => Assert.Equal(ReadKeyPolling.PollIntervalMs, ms));
    }

    [Fact]
    public void A_key_already_there_is_read_without_sleeping()
    {
        var slept = false;
        var key = ReadKeyPolling.ReadKey(true, () => true, _ => KeyA, _ => slept = true);

        Assert.Equal(KeyA, key);
        Assert.False(slept);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Intercept_is_passed_through(bool intercept)
    {
        bool? seen = null;
        ReadKeyPolling.ReadKey(intercept, () => true, i => { seen = i; return KeyA; }, _ => { });

        Assert.Equal(intercept, seen);
    }

    [Fact]
    public void Windows_is_left_alone()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal("not needed on Windows", ReadKeyPolling.Install());
        ReadKeyPolling.Uninstall(); // no-op, must not throw
    }
}
