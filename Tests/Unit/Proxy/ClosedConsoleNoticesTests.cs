using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// The self-close notes a console leaves for its proxy (the module's
/// <c>ConsoleLiveness.RecordSelfClose</c> writes them; this class reads them).
/// <c>LiveRootForTests</c> is a static seam, so everything that sets it runs
/// in one serial collection.
/// </summary>
[Collection("ClosedConsoleNotices serial")]
public class ClosedConsoleNoticesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerShell.MCP.Tests", Guid.NewGuid().ToString("N"));
    private const int ProxyPid = 1_234_567;

    public ClosedConsoleNoticesTests()
    {
        ClosedConsoleNotices.LiveRootForTests = _root;
    }

    public void Dispose()
    {
        ClosedConsoleNotices.LiveRootForTests = null;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string Plant(string agentId, string fileName, string content)
    {
        var dir = ClosedConsoleNotices.GroupDir(ProxyPid, agentId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void GroupDir_MirrorsTheModuleLayout()
    {
        // The module writes <root>/<proxyPid>.<agent>/<pid>; a null or empty
        // agent id is the main agent, "default". Any drift here and the proxy
        // reads an empty directory forever.
        Assert.Equal(Path.Combine(_root, "1234567.default"), ClosedConsoleNotices.GroupDir(ProxyPid, null));
        Assert.Equal(Path.Combine(_root, "1234567.default"), ClosedConsoleNotices.GroupDir(ProxyPid, ""));
        Assert.Equal(Path.Combine(_root, "1234567.sa-abc"), ClosedConsoleNotices.GroupDir(ProxyPid, "sa-abc"));
    }

    [Fact]
    public void TakeReason_ReturnsTheNoteOnceAndDeletesIt()
    {
        var path = Plant("default", "4242" + ClosedConsoleNotices.Suffix, "closed itself after 10 min without use\n");

        Assert.Equal("closed itself after 10 min without use", ClosedConsoleNotices.TakeReason(ProxyPid, "default", 4242));
        Assert.False(File.Exists(path));
        Assert.Null(ClosedConsoleNotices.TakeReason(ProxyPid, "default", 4242));
    }

    [Fact]
    public void TakeReason_NullWhenThereIsNoNoteOrNoDirectory()
    {
        Assert.Null(ClosedConsoleNotices.TakeReason(ProxyPid, "default", 1));
        Plant("default", "7" , "123456789"); // a liveness marker, not a note
        Assert.Null(ClosedConsoleNotices.TakeReason(ProxyPid, "default", 7));
    }

    [Fact]
    public void TakeAll_ReturnsEveryNote_LeavesMarkersAlone_SkipsJunk()
    {
        var marker = Plant("default", "100", "638000000000000000");
        Plant("default", "100" + ClosedConsoleNotices.Suffix, "closed itself after 10 min without use");
        Plant("default", "200" + ClosedConsoleNotices.Suffix, "closed itself after 12 min without use");
        var junk = Plant("default", "junk" + ClosedConsoleNotices.Suffix, "x");
        Plant("sa-other", "300" + ClosedConsoleNotices.Suffix, "another agent's note");

        var notes = ClosedConsoleNotices.TakeAll(ProxyPid, "default");

        Assert.Equal(2, notes.Count);
        Assert.Equal("closed itself after 10 min without use", notes[100]);
        Assert.Equal("closed itself after 12 min without use", notes[200]);
        Assert.True(File.Exists(marker), "liveness markers belong to the consoles");
        Assert.True(File.Exists(junk), "only notes named by a PID are taken");
        Assert.Empty(ClosedConsoleNotices.TakeAll(ProxyPid, "default"));
        Assert.Single(ClosedConsoleNotices.TakeAll(ProxyPid, "sa-other"));
    }

    [Fact]
    public void Describe_SaysWhyWhenKnown_AndWhatWentWithIt()
    {
        var withReason = ClosedConsoleNotices.Describe("PID #20888 Hallway", "closed itself after 10 min without use");
        Assert.StartsWith("⚠ Console PID #20888 Hallway was closed: it closed itself after 10 min without use.", withReason);
        Assert.Contains("Start-Job jobs it was running ended with it", withReason);

        var withoutReason = ClosedConsoleNotices.Describe("PID #20888 Hallway", null);
        Assert.StartsWith("⚠ Console PID #20888 Hallway was closed: its window was closed or its process ended outside this server.", withoutReason);
        Assert.Contains("Start-Job jobs it was running ended with it", withoutReason);
    }
}
