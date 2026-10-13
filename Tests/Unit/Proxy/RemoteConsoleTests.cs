using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PowerShell.MCP.Proxy.Models;
using PowerShell.MCP.Proxy.Services;
using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// The remote console end to end over loopback: the module's listener in this
/// process, the proxy's client connecting to it. The listener is a process
/// singleton, so these tests share one class (xUnit runs a class's tests one
/// at a time) and each stops it on the way out.
/// </summary>
public class RemoteConsoleTests
{
    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static string Ping() => JsonSerializer.Serialize(new PingParams(), RemoteConsoleJsonContext.Default.PingParams);
    private static string GetStatus() => JsonSerializer.Serialize(new GetStatusParams(), PowerShellJsonRpcContext.Default.GetStatusParams);

    private static string StatusOf(string response)
    {
        var header = response.Split("\n\n")[0];
        using var doc = JsonDocument.Parse(header);
        return doc.RootElement.GetProperty("status").GetString()!;
    }

    [Fact]
    public async Task RightCode_ConnectsAndPings()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            Assert.True(RemoteConsoleListener.IsListening);
            Assert.Equal(RemoteConsoleListener.CodeLength, code.Length);

            using var client = new RemoteConsoleClient();
            var notice = await client.ConnectAsync($"127.0.0.1:{port}", RemoteConsoleListener.FormatCode(code).ToLowerInvariant(), CancellationToken.None);
            Assert.Contains("Connected", notice);
            Assert.Equal(Environment.ProcessId, client.RemotePid);
            Assert.True(RemoteConsoleListener.IsConnected);

            var pong = await client.SendRequestAsync(Ping(), CancellationToken.None);
            Assert.Equal("pong", StatusOf(pong));

            // A remote proxy is the owner for the session: it is never told
            // "remote" (that answer is for local probes over the pipe).
            var status = await client.SendRequestAsync(GetStatus(), CancellationToken.None);
            Assert.NotEqual("remote", StatusOf(status));

            await client.DisconnectAsync(CancellationToken.None);
            Assert.False(client.IsConnected);
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task WrongCode_IsRefusedBeforeTheClientProvesAnything()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            var wrong = code[0] == 'A' ? "B" + code[1..] : "A" + code[1..];
            using var client = new RemoteConsoleClient();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync($"127.0.0.1:{port}", wrong, CancellationToken.None));
            Assert.Contains("did not prove the pairing code", ex.Message);
            Assert.False(client.IsConnected);
            Assert.True(RemoteConsoleListener.IsListening); // one failure is not five
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task FiveFailures_StopTheListener()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            var wrong = code[0] == 'A' ? "B" + code[1..] : "A" + code[1..];
            for (var i = 0; i < RemoteConsoleListener.MaxFailures; i++)
            {
                using var client = new RemoteConsoleClient();
                await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync($"127.0.0.1:{port}", wrong, CancellationToken.None));
            }
            // The server counts the dropped handshake when its read fails,
            // a moment after the client gave up.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (RemoteConsoleListener.IsListening && DateTime.UtcNow < deadline) await Task.Delay(50);
            Assert.False(RemoteConsoleListener.IsListening);

            using var late = new RemoteConsoleClient();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => late.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None));
            Assert.Contains("Could not reach", ex.Message);
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task SecondProxy_IsRefusedAsBusy()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            using var first = new RemoteConsoleClient();
            await first.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None);

            using var second = new RemoteConsoleClient();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => second.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None));
            Assert.Contains("Another MCP server is connected", ex.Message);

            // The first session is untouched by the refused one.
            Assert.Equal("pong", StatusOf(await first.SendRequestAsync(Ping(), CancellationToken.None)));
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task SecondListener_OnTheSamePort_IsRefused()
    {
        var port = FreePort();
        RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => RemoteConsoleListener.Start(port, IPAddress.Loopback));
            Assert.Contains("already listening", ex.Message);
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task LocalProbe_OfAListeningConsole_IsToldRemote()
    {
        var port = FreePort();
        RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            using var pipe = new MemoryStream();
            await NamedPipeServer.HandleRequestAsync(pipe, GetStatus(), CancellationToken.None);
            pipe.Position = 0;
            var response = await NamedPipeServer.ReceiveMessageAsync(pipe, CancellationToken.None);
            Assert.Equal("remote", StatusOf(response));
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }

        // Not listening: the same probe is a normal status answer again.
        using var after = new MemoryStream();
        await NamedPipeServer.HandleRequestAsync(after, GetStatus(), CancellationToken.None);
        after.Position = 0;
        Assert.NotEqual("remote", StatusOf(await NamedPipeServer.ReceiveMessageAsync(after, CancellationToken.None)));
    }

    [Fact]
    public void KeyDerivationAndProofs_AgreeBetweenModuleAndProxy()
    {
        var salt = new byte[16];
        var nonceC = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var nonceS = Enumerable.Range(32, 32).Select(i => (byte)i).ToArray();
        var thumb = Enumerable.Range(64, 32).Select(i => (byte)i).ToArray();

        var moduleKey = RemoteConsoleListener.DeriveKey("7q4k-m2xp", salt);
        var proxyKey = RemoteConsoleClient.DeriveKey(RemoteConsoleClient.NormalizeCode("7Q4K M2XP"), salt, RemoteConsoleListener.KeyIterations);
        Assert.Equal(moduleKey, proxyKey);
        Assert.Equal(RemoteConsoleListener.Proof(moduleKey, "srv", nonceC, nonceS, thumb), RemoteConsoleClient.Proof(proxyKey, "srv", nonceC, nonceS, thumb));
        Assert.NotEqual(RemoteConsoleListener.Proof(moduleKey, "srv", nonceC, nonceS, thumb), RemoteConsoleListener.Proof(moduleKey, "cli", nonceC, nonceS, thumb));

        Assert.Equal("7Q4KM2XP", RemoteConsoleListener.NormalizeCode("7q4k-m2xp"));
        Assert.Equal("7Q4K-M2XP", RemoteConsoleListener.FormatCode("7Q4KM2XP"));
    }

    [Theory]
    [InlineData("::ffff:192.168.0.10", "192.168.0.10")]  // IPv4 peer on the dual-stack socket
    [InlineData("192.168.0.10", "192.168.0.10")]
    [InlineData("::1", "localhost")]                      // an SSH tunnel or a proxy on the same machine
    [InlineData("127.0.0.1", "localhost")]
    [InlineData("fe80::1", "fe80::1")]
    public void DescribeRemote_ReadsAsAPersonWould(string address, string expected)
    {
        Assert.Equal(expected, RemoteConsoleListener.DescribeRemote(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("build-pc", "build-pc", RemoteConsoleClient.DefaultPort)]
    [InlineData("10.0.0.5:5000", "10.0.0.5", 5000)]
    [InlineData("[fe80::1]", "fe80::1", RemoteConsoleClient.DefaultPort)]
    [InlineData("[fe80::1]:5000", "fe80::1", 5000)]
    public void ParseTarget_ReadsHostAndPort(string target, string host, int port)
    {
        Assert.Equal((host, port), RemoteConsoleClient.ParseTarget(target));
    }
}