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

    private static async Task WaitUntilAsync(Func<bool> condition, int seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(50);
    }

    [Fact]
    public async Task DialOut_RightCode_ConsoleDialsIn_AndPings()
    {
        using var client = new RemoteConsoleClient();
        try
        {
            var instructions = await client.AcceptAsync(0, CancellationToken.None);
            Assert.Contains("Enable-MCPRemoteControl -ConnectTo", instructions);
            Assert.True(client.IsAccepting);

            RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, RemoteConsoleClient.FormatCode(client.AcceptCode!).ToLowerInvariant());
            // The proxy counts the session from sending its ack, the console
            // from reading it: wait for both.
            await WaitUntilAsync(() => client.IsConnected && RemoteConsoleDialer.IsConnected);
            Assert.True(client.IsConnected);
            Assert.True(RemoteConsoleDialer.IsConnected);
            Assert.Equal(Environment.ProcessId, client.RemotePid);
            Assert.Equal("pong", StatusOf(await client.SendRequestAsync(Ping(), CancellationToken.None)));
            Assert.NotEqual("remote", StatusOf(await client.SendRequestAsync(GetStatus(), CancellationToken.None)));

            // Over the pipe, a dialing console is spoken for as a listening one is.
            using var pipe = new MemoryStream();
            await NamedPipeServer.HandleRequestAsync(pipe, GetStatus(), CancellationToken.None);
            pipe.Position = 0;
            Assert.Equal("remote", StatusOf(await NamedPipeServer.ReceiveMessageAsync(pipe, CancellationToken.None)));
        }
        finally
        {
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task DialOut_WrongCode_EndsTheConsolesAttempt_AndFiveOfThemStopAccepting()
    {
        using var client = new RemoteConsoleClient();
        try
        {
            await client.AcceptAsync(0, CancellationToken.None);
            var wrong = client.AcceptCode![0] == 'A' ? "B" + client.AcceptCode[1..] : "A" + client.AcceptCode[1..];
            for (var i = 0; i < 5; i++)
            {
                RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, wrong);
                // The console verifies the proxy's proof, fails, and stops for good:
                // redialing with a wrong code would only burn the proxy's attempts.
                await WaitUntilAsync(() => !RemoteConsoleDialer.IsActive);
                Assert.False(RemoteConsoleDialer.IsActive);
                Assert.False(client.IsConnected);
            }
            await WaitUntilAsync(() => !client.IsAccepting);
            Assert.False(client.IsAccepting);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendRequestAsync(Ping(), CancellationToken.None));
            Assert.Contains("accepting stopped", ex.Message);
        }
        finally
        {
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public void DialOut_WhileListening_IsRefused()
    {
        var port = FreePort();
        RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => RemoteConsoleDialer.Start("127.0.0.1", 1, "AAAAAAAA"));
            Assert.Contains("already listening", ex.Message);
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public void Bootstrap_IsAsciiPowerShellThatParses_AndCarriesNoPairingCode()
    {
        var script = RemoteBootstrap.BootstrapTemplate;
        // Windows PowerShell 5.1 reads a BOM-less file in the ANSI code page.
        Assert.All(script, c => Assert.True(c < 128, $"non-ASCII character U+{(int)c:X4}"));
        System.Management.Automation.Language.Parser.ParseInput(script, out _, out var errors);
        Assert.Empty(errors);
        // The file is served over plain HTTP: the code travels on the pasted line.
        Assert.Contains("[Parameter(Mandatory = $true)][string]$Code", script);
        foreach (var placeholder in new[] { "__MODULE_VERSION__", "__MODULE_HASH__", "__MODULE_DLL_HASH__", "__PWSH_VERSION__", "__PWSH_HASH__" })
            Assert.Contains(placeholder, script);
    }

    [Fact]
    public async Task PrefixedStream_ReplaysThePeekedByte_ThenReadsOn()
    {
        using var inner = new MemoryStream("ET /x"u8.ToArray());
        using var stream = new PrefixedStream("G"u8.ToArray(), inner);
        var buffer = new byte[16];
        var total = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer.AsMemory(total))) > 0) total += n;
        Assert.Equal("GET /x", System.Text.Encoding.ASCII.GetString(buffer, 0, total));
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