using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// The descendant walk StalledChild uses to tell a stalled command from one
/// whose other processes are still working, on made-up snapshots.
/// </summary>
public class ProcessTreeTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 18, 0, 0, DateTimeKind.Utc);

    private static Func<uint, DateTime?> Starts(Dictionary<uint, int> secondsAfterT0)
        => pid => secondsAfterT0.TryGetValue(pid, out var s) ? T0.AddSeconds(s) : null;

    [Fact]
    public void FindsChildrenAndGrandchildren_NotSiblingsOfTheRoot()
    {
        // pwsh 10 <- dotnet test 11 <- MSBuild nodes 12, 13 <- VBCSCompiler 14 (under 13)
        // 20 is another console's child, and 1 the proxy above the root.
        var snapshot = new Dictionary<uint, (uint Parent, string Name)>
        {
            [1] = (0, "PowerShell.MCP.Proxy.exe"),
            [10] = (1, "pwsh.exe"),
            [11] = (10, "dotnet.exe"),
            [12] = (11, "dotnet.exe"),
            [13] = (11, "dotnet.exe"),
            [14] = (13, "VBCSCompiler.exe"),
            [20] = (1, "pwsh.exe"),
            [21] = (20, "dotnet.exe"),
        };
        var found = ProcessTree.Descendants(10, snapshot, _ => null);
        Assert.Equal(new uint[] { 11, 12, 13, 14 }, found.OrderBy(p => p));
    }

    [Fact]
    public void LeavesOut_AProcessOlderThanItsRecordedParent_AndWhatHangsBelowIt()
    {
        // 30 started before 11 took the id 11 its parent once had: not 11's child.
        var snapshot = new Dictionary<uint, (uint Parent, string Name)>
        {
            [10] = (1, "pwsh.exe"),
            [11] = (10, "dotnet.exe"),
            [30] = (11, "unrelated.exe"),
            [31] = (30, "its-child.exe"),
            [12] = (11, "dotnet.exe"),
        };
        var starts = Starts(new() { [10] = 0, [11] = 100, [30] = 50, [31] = 60, [12] = 110 });
        Assert.Equal(new uint[] { 11, 12 }, ProcessTree.Descendants(10, snapshot, starts).OrderBy(p => p));
    }

    [Fact]
    public void Ends_OnALoopInAStaleSnapshot_AndAtTheLimit()
    {
        var loop = new Dictionary<uint, (uint Parent, string Name)>
        {
            [10] = (12, "pwsh.exe"),
            [11] = (10, "a.exe"),
            [12] = (11, "b.exe"),
            [0] = (0, "[System Process]"),
        };
        Assert.Equal(new uint[] { 11, 12 }, ProcessTree.Descendants(10, loop, _ => null).OrderBy(p => p));

        var wide = Enumerable.Range(100, 50).ToDictionary(p => (uint)p, _ => ((uint)10, "x.exe"));
        Assert.Equal(5, ProcessTree.Descendants(10, wide, _ => null, max: 5).Count);
    }
}
