using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// On Linux and macOS a named pipe is a Unix domain socket file
/// (CoreFxPipe_{name} in /tmp) that outlives its console. Every scan found the
/// dead console's socket again and reported the console closed again, in each
/// response. The socket is now removed once its process is gone, and a dead
/// pipe is reported once.
/// </summary>
public class StaleSocketTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "psmcp-stale-" + Guid.NewGuid().ToString("N")[..8]);

    public StaleSocketTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Socket(string pipeName)
    {
        var path = Path.Combine(_dir, "CoreFxPipe_" + pipeName);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void The_socket_of_a_dead_console_is_removed()
    {
        var path = Socket("PSMCP.100.default.200");

        ConsoleSessionManager.RemoveStaleSocket("PSMCP.100.default.200", 200, _ => false, isUnix: true, directories: [_dir]);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void A_live_process_keeps_its_socket()
    {
        // Module removed or console stuck: the pipe is silent, the process is there.
        var path = Socket("PSMCP.100.default.201");

        ConsoleSessionManager.RemoveStaleSocket("PSMCP.100.default.201", 201, _ => true, isUnix: true, directories: [_dir]);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Windows_touches_nothing()
    {
        var path = Socket("PSMCP.100.default.202");

        ConsoleSessionManager.RemoveStaleSocket("PSMCP.100.default.202", 202, _ => false, isUnix: false, directories: [_dir]);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Only_the_named_pipe_is_removed()
    {
        var other = Socket("PSMCP.100.default.204");
        Socket("PSMCP.100.default.203");

        ConsoleSessionManager.RemoveStaleSocket("PSMCP.100.default.203", 203, _ => false, isUnix: true, directories: [_dir]);

        Assert.True(File.Exists(other));
    }

    [Fact]
    public void A_missing_socket_or_directory_is_not_an_error()
    {
        ConsoleSessionManager.RemoveStaleSocket("PSMCP.100.default.205", 205, _ => false, isUnix: true,
            directories: [_dir, Path.Combine(_dir, "does-not-exist")]);
    }

    [Fact]
    public void A_dead_pipe_is_reported_once_until_it_is_in_use_again()
    {
        var manager = ConsoleSessionManager.Instance;
        var agentId = manager.AllocateSubAgentId();
        var pipe = ConsoleSessionManager.GetPipeNameForPids(manager.ProxyPid, agentId, 92001);

        Assert.True(manager.TryMarkDeadPipeReported(pipe));
        Assert.False(manager.TryMarkDeadPipeReported(pipe));

        manager.SetActivePipeName(agentId, pipe);
        Assert.True(manager.TryMarkDeadPipeReported(pipe));

        manager.ClearDeadPipe(agentId, pipe);
    }
}
