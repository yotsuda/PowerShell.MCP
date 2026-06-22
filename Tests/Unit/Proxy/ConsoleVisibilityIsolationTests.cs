using System.IO.Pipes;
using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// End-to-end isolation test: a console OWNED by one AI must be invisible to
/// every other AI. "AI" == a (proxyPid, agentId) tuple, and ownership is
/// expressed entirely in the pipe name —
///   owned   : PSMCP.{proxyPid}.{agentId}.{pwshPid}   (4 segments)
///   unowned : PSMCP.{pwshPid}                         (2 segments)
/// Discovery scopes to the caller via EnumeratePipes(ProxyPid, agentId); the
/// only cross-AI scan, EnumerateUnownedPipes(), is restricted to 2-segment
/// names so another AI's owned console is never even a claim candidate.
///
/// Rather than mock the filesystem, these tests create REAL named pipes with
/// controlled names (synthetic proxy PIDs that no live proxy uses) and run the
/// actual EnumeratePipes / EnumerateUnownedPipes code paths against the OS pipe
/// namespace — the same scan production relies on. Assertions use
/// Contains/DoesNotContain on the specific names we created so ambient pipes
/// from real consoles on the dev/CI machine can't make the test flaky.
/// </summary>
public class ConsoleVisibilityIsolationTests
{
    private readonly ConsoleSessionManager _mgr = ConsoleSessionManager.Instance;

    // Synthetic proxy PIDs — high enough that no real proxy process collides,
    // so EnumeratePipes(ProxyPid, …) on these never picks up ambient pipes.
    private const int ProxyA = 770001;
    private const int ProxyB = 770002;
    private const string AgentX = "sa-aaaa1111";
    private const string AgentY = "sa-bbbb2222";

    /// <summary>
    /// Opens a real named pipe under the given name and starts listening so the
    /// pipe (Windows) / Unix-domain-socket file (Linux/macOS) becomes
    /// discoverable. On Unix the socket file is created on WaitForConnection, so
    /// we kick that off; callers poll WaitUntilVisible before asserting.
    /// </summary>
    private sealed class FakeConsolePipe : IDisposable
    {
        private readonly NamedPipeServerStream _stream;
        public FakeConsolePipe(string pipeName)
        {
            _stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _ = _stream.WaitForConnectionAsync(); // bind so the name is enumerable
        }
        public void Dispose() => _stream.Dispose();
    }

    // Polls the real enumeration until the expected pipe appears (defeats the
    // Unix bind race without a fixed sleep). Fails the test if it never shows.
    private void WaitUntilVisible(int proxyPid, string agentId, string expected, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (_mgr.EnumeratePipes(proxyPid, agentId).Contains(expected)) return;
            Thread.Sleep(25);
        }
        Assert.Fail($"Pipe '{expected}' never became visible to ({proxyPid}, {agentId}) within {timeoutMs}ms");
    }

    [Fact]
    public void OwnedConsole_IsInvisibleToADifferentProxy()
    {
        var ownedByA = ConsoleSessionManager.GetPipeNameForPids(ProxyA, AgentX, 11);
        var ownedByB = ConsoleSessionManager.GetPipeNameForPids(ProxyB, AgentX, 22);

        using var a = new FakeConsolePipe(ownedByA);
        using var b = new FakeConsolePipe(ownedByB);
        WaitUntilVisible(ProxyA, AgentX, ownedByA);
        WaitUntilVisible(ProxyB, AgentX, ownedByB);

        var seenByA = _mgr.EnumeratePipes(ProxyA, AgentX).ToList();
        Assert.Contains(ownedByA, seenByA);          // its own console
        Assert.DoesNotContain(ownedByB, seenByA);    // proxy B's console is invisible

        var seenByB = _mgr.EnumeratePipes(ProxyB, AgentX).ToList();
        Assert.Contains(ownedByB, seenByB);
        Assert.DoesNotContain(ownedByA, seenByB);    // and symmetrically
    }

    [Fact]
    public void OwnedConsole_IsInvisibleToADifferentAgentInTheSameProxy()
    {
        var ownedByX = ConsoleSessionManager.GetPipeNameForPids(ProxyA, AgentX, 33);
        var ownedByY = ConsoleSessionManager.GetPipeNameForPids(ProxyA, AgentY, 44);

        using var x = new FakeConsolePipe(ownedByX);
        using var y = new FakeConsolePipe(ownedByY);
        WaitUntilVisible(ProxyA, AgentX, ownedByX);
        WaitUntilVisible(ProxyA, AgentY, ownedByY);

        var seenByX = _mgr.EnumeratePipes(ProxyA, AgentX).ToList();
        Assert.Contains(ownedByX, seenByX);
        Assert.DoesNotContain(ownedByY, seenByX);    // sibling agent's console is invisible

        var seenByY = _mgr.EnumeratePipes(ProxyA, AgentY).ToList();
        Assert.Contains(ownedByY, seenByY);
        Assert.DoesNotContain(ownedByX, seenByY);
    }

    [Fact]
    public void OwnedConsole_NeverAppearsInTheUnownedClaimSet()
    {
        // A console owned by another AI must not be claimable by anyone — the
        // unowned scan only yields 2-segment names.
        var owned = ConsoleSessionManager.GetPipeNameForPids(ProxyA, AgentX, 55);
        var unowned = $"{ConsoleSessionManager.DefaultPipeName}.770055"; // 2-segment, user-started

        using var o = new FakeConsolePipe(owned);
        using var u = new FakeConsolePipe(unowned);
        WaitUntilVisible(ProxyA, AgentX, owned); // owned pipe is up...

        // ...and the unowned one is up (it shows under nobody's owned scope).
        var deadline = Environment.TickCount64 + 3000;
        while (Environment.TickCount64 < deadline &&
               !_mgr.EnumerateUnownedPipes().Contains(unowned))
        {
            Thread.Sleep(25);
        }

        var unownedSet = _mgr.EnumerateUnownedPipes().ToList();
        Assert.Contains(unowned, unownedSet);        // user-started console is claimable
        Assert.DoesNotContain(owned, unownedSet);    // an owned console is NOT claimable by others
    }
}
