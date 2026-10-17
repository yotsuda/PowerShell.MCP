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
    [Fact]
    public void Goodbye_FromTheProxy_IsRecognizedByTheDialer_AndPingIsNot()
    {
        // disconnect_remote_console sends this so a dialed-in console stops instead of redialing.
        var goodbye = JsonSerializer.Serialize(new GoodbyeParams(), RemoteConsoleJsonContext.Default.GoodbyeParams);
        var ping = JsonSerializer.Serialize(new PingParams(), RemoteConsoleJsonContext.Default.PingParams);
        Assert.True(RemoteConsoleDialer.IsGoodbye(goodbye));
        Assert.False(RemoteConsoleDialer.IsGoodbye(ping));
        Assert.False(RemoteConsoleDialer.IsGoodbye("not json"));
    }

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
            Assert.Equal("[AI remote control: waiting] ", RemoteControl.PromptTag);

            using var client = new RemoteConsoleClient();
            var notice = await client.ConnectAsync($"127.0.0.1:{port}", RemoteConsoleListener.FormatCode(code).ToLowerInvariant(), CancellationToken.None);
            Assert.Contains("Connected", notice);
            Assert.Equal(Environment.ProcessId, client.RemotePid);
            Assert.True(RemoteConsoleListener.IsConnected);
            // Who drives the console, not where the path is.
            Assert.Equal("[AI from localhost] ", RemoteControl.PromptTag);

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
        Assert.Equal("", RemoteControl.PromptTag);
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

    /// <summary>
    /// A peer that answers the challenge with a wrong client proof — what an
    /// online guess looks like. (A real client given a wrong code never gets
    /// that far: it checks the server's proof first and closes, which does not
    /// count.) <paramref name="console"/> speaks as a console dialing a proxy.
    /// </summary>
    private static async Task SendWrongClientProofAsync(int port, bool console = false)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        using var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync("localhost");
        var hello = console
            ? JsonSerializer.Serialize(new { hello = RemoteConsoleListener.Hello, role = "console", module_version = PowerShell.MCP.MCPModuleInitializer.ServerVersion, pid = 1, client_nonce = Convert.ToBase64String(new byte[32]) })
            : JsonSerializer.Serialize(new { hello = RemoteConsoleListener.Hello, proxy_version = PowerShell.MCP.MCPModuleInitializer.ServerVersion, client_nonce = Convert.ToBase64String(new byte[32]) });
        await NamedPipeServer.SendMessageAsync(ssl, hello, CancellationToken.None);
        await NamedPipeServer.ReceiveMessageAsync(ssl, CancellationToken.None); // the challenge
        await NamedPipeServer.SendMessageAsync(ssl, JsonSerializer.Serialize(new { client_proof = Convert.ToBase64String(new byte[32]) }), CancellationToken.None);
        await NamedPipeServer.ReceiveMessageAsync(ssl, CancellationToken.None); // bad_code
    }

    [Fact]
    public async Task FiveWrongClientProofs_StopTheListener_ButWrongCodesTypedIntoAClientDoNot()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            // A client with a wrong code stops at the server's proof: no attempt counted.
            var wrong = code[0] == 'A' ? "B" + code[1..] : "A" + code[1..];
            for (var i = 0; i < RemoteConsoleListener.MaxFailures + 1; i++)
            {
                using var client = new RemoteConsoleClient();
                await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync($"127.0.0.1:{port}", wrong, CancellationToken.None));
            }
            await Task.Delay(200);
            Assert.True(RemoteConsoleListener.IsListening);

            for (var i = 0; i < RemoteConsoleListener.MaxFailures; i++)
                await SendWrongClientProofAsync(port);
            await WaitUntilAsync(() => !RemoteConsoleListener.IsListening, 5);
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

        var moduleKey = RemoteConsoleListener.DeriveKey("7q4k-m2xp-abcd", salt);
        var proxyKey = RemoteConsoleClient.DeriveKey(RemoteConsoleClient.NormalizeCode("7Q4K M2XP ABCD"), salt, RemoteConsoleListener.KeyIterations);
        Assert.Equal(moduleKey, proxyKey);
        Assert.Equal(RemoteConsoleListener.Proof(moduleKey, "srv", nonceC, nonceS, thumb), RemoteConsoleClient.Proof(proxyKey, "srv", nonceC, nonceS, thumb));
        Assert.NotEqual(RemoteConsoleListener.Proof(moduleKey, "srv", nonceC, nonceS, thumb), RemoteConsoleListener.Proof(moduleKey, "cli", nonceC, nonceS, thumb));

        Assert.Equal("7Q4KM2XPABCD", RemoteConsoleListener.NormalizeCode("7q4k-m2xp-abcd"));
        Assert.Equal("7Q4K-M2XP-ABCD", RemoteConsoleListener.FormatCode("7Q4KM2XPABCD"));
        Assert.Equal("7Q4K-M2XP-ABCD", RemoteConsoleClient.FormatCode("7Q4KM2XPABCD"));
        Assert.Equal(12, RemoteConsoleClient.NewPairingCode().Length);
        Assert.Equal(RemoteConsoleListener.CodeLength, RemoteConsoleListener.NewPairingCode().Length);
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
            var instructions = await client.AcceptAsync(0, "none", CancellationToken.None);
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
    public async Task DialOut_WrongCode_EndsTheConsolesAttempt_WrongClientProofsStopAccepting()
    {
        using var client = new RemoteConsoleClient();
        try
        {
            await client.AcceptAsync(0, "none", CancellationToken.None);
            var wrong = client.AcceptCode![0] == 'A' ? "B" + client.AcceptCode[1..] : "A" + client.AcceptCode[1..];
            RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, wrong);
            // The console verifies the proxy's proof, fails, and stops for good.
            await WaitUntilAsync(() => !RemoteConsoleDialer.IsActive);
            Assert.False(RemoteConsoleDialer.IsActive);
            Assert.False(client.IsConnected);
            Assert.True(client.IsAccepting); // that was no attempt at the proxy

            for (var i = 0; i < 5; i++)
                await SendWrongClientProofAsync(client.AcceptPort, console: true);
            await WaitUntilAsync(() => !client.IsAccepting, 5);
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
        // Pasted into PowerShell 7, remote control starts in that window; the
        // module must outlive the script block it was imported from.
        Assert.Contains("Import-Module $psd1 -Global", script);
        Assert.Contains("Enable-MCPRemoteControl -ConnectTo $target -Code $Code", script);
        // A new window gets the code through the environment, not its command line.
        Assert.Contains("$env:PSMCP_REMOTE_CODE = $Code", script);
        Assert.DoesNotContain("-Code '\" + $Code", script);
        // Downloads are hashed in memory; nothing is checked in one place and used from another.
        Assert.Contains("RawContentStream.ToArray()", script);
        Assert.DoesNotContain("-OutFile", script);
        // As Administrator: an admins-only folder, signed PowerShell, no install=user.
        Assert.Contains("S-1-5-32-544", script);
        Assert.Contains("Test-MicrosoftSigned $msiPath", script);
        Assert.Contains("$Install -eq 'user' -and $elevated", script);
        // Installing is confirmed by the person at the console.
        Assert.Contains("Read-Host 'Continue? [y/N]'", script);
        foreach (var placeholder in new[] { "__INSTALL__", "__MODULE_VERSION__", "__MODULE_DLL_HASH__", "__MODULE_ZIP_HASH__", "__MODULE_NUPKG_HASH__", "__PWSH_VERSION__", "__PWSH_ZIP_HASH__", "__PWSH_MSI_HASH__" })
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

    // ----- the code is short-lived; reconnects prove a resume key -----

    [Fact]
    public async Task Code_PairsOnce_AndAfterADisconnectANewCodeIsShown()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            using (var first = new RemoteConsoleClient())
            {
                await first.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None);
                Assert.Null(RemoteConsoleListener.CurrentCode); // used up by the pairing
                await first.DisconnectAsync(CancellationToken.None);
            }
            // The goodbye ends the session there; a new code goes up.
            await WaitUntilAsync(() => RemoteConsoleListener.CurrentCode != null);
            var next = RemoteConsoleListener.CurrentCode!;
            Assert.NotEqual(code, next);

            // The old code is now just a wrong code (and counts as one attempt).
            using var replay = new RemoteConsoleClient();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => replay.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None));
            Assert.Contains("did not prove the pairing code", ex.Message);

            using var second = new RemoteConsoleClient();
            await second.ConnectAsync($"127.0.0.1:{port}", next, CancellationToken.None);
            Assert.Equal("pong", StatusOf(await second.SendRequestAsync(Ping(), CancellationToken.None)));
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task DroppedConnection_IsReopenedWithTheResumeKey_NotTheCode()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            using var client = new RemoteConsoleClient();
            await client.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None);
            client.DropConnection();
            await WaitUntilAsync(() => !RemoteConsoleListener.IsConnected);
            Assert.False(RemoteConsoleListener.IsConnected);

            // The code is spent, so only the resume key can have done this.
            Assert.Equal("pong", StatusOf(await client.SendRequestAsync(Ping(), CancellationToken.None)));
            Assert.True(RemoteConsoleListener.IsConnected);
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task ExpiredCode_IsRefused()
    {
        var lifetime = RemoteConsoleListener.CodeLifetime;
        RemoteConsoleListener.CodeLifetime = TimeSpan.FromMilliseconds(200);
        var port = FreePort();
        try
        {
            var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
            await Task.Delay(600);
            using var client = new RemoteConsoleClient();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None));
            Assert.Contains("expired", ex.Message);
        }
        finally
        {
            RemoteConsoleListener.CodeLifetime = lifetime;
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task OversizedFrameBeforeAuthentication_IsDropped_AndNotCountedAsAnAttempt()
    {
        var port = FreePort();
        RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            for (var i = 0; i < RemoteConsoleListener.MaxFailures + 1; i++)
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(IPAddress.Loopback, port);
                using var ssl = new System.Net.Security.SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync("localhost");
                // Claims a 1 MB hello: the listener must not allocate it, and closes.
                await ssl.WriteAsync(BitConverter.GetBytes(1_000_000));
                await ssl.FlushAsync();
                var buffer = new byte[16];
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    Assert.Equal(0, await ssl.ReadAsync(buffer, timeout.Token));
                }
                catch (IOException) { }
            }
            // Six garbage connections, yet no proof was ever sent: still listening.
            Assert.True(RemoteConsoleListener.IsListening);
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    private static async Task<string> ServeLocallyAsync(string request)
    {
        using var pipe = new MemoryStream();
        await NamedPipeServer.HandleRequestAsync(pipe, request, CancellationToken.None);
        pipe.Position = 0;
        return await NamedPipeServer.ReceiveMessageAsync(pipe, CancellationToken.None);
    }

    [Fact]
    public async Task UnderRemoteControl_LocalRequestsOtherThanStatus_AreRefused()
    {
        var port = FreePort();
        RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            foreach (var name in new[] { "execute_command", "execute_silent", "cancel", "consume_output", "set_window_title" })
                Assert.Contains("under remote control", await ServeLocallyAsync($"{{\"name\":\"{name}\",\"pipeline\":\"1\"}}"));
            Assert.Equal("remote", StatusOf(await ServeLocallyAsync(GetStatus())));
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task RemoteSession_CannotRunSilentlyOrRetitleTheConsole()
    {
        NamedPipeServer.IsRemoteRequest.Value = true;
        try
        {
            foreach (var name in new[] { "execute_silent", "set_window_title", "claim_console" })
                Assert.Contains("not available to a remote session", await ServeLocallyAsync($"{{\"name\":\"{name}\",\"pipeline\":\"1\",\"title\":\"x\"}}"));
        }
        finally
        {
            NamedPipeServer.IsRemoteRequest.Value = false;
        }
    }

    [Fact]
    public async Task DialOut_DroppedConnection_IsRedialedWithTheResumeKey()
    {
        using var client = new RemoteConsoleClient();
        try
        {
            await client.AcceptAsync(0, "none", CancellationToken.None);
            RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, client.AcceptCode!);
            await WaitUntilAsync(() => client.IsConnected && RemoteConsoleDialer.IsConnected);
            Assert.True(client.IsConnected);

            client.DropConnection();
            await WaitUntilAsync(() => !RemoteConsoleDialer.IsConnected);
            // The console redials (2 s backoff) and proves the resume key.
            await WaitUntilAsync(() => client.IsConnected && RemoteConsoleDialer.IsConnected);
            Assert.True(client.IsConnected);
            Assert.Equal("pong", StatusOf(await client.SendRequestAsync(Ping(), CancellationToken.None)));
        }
        finally
        {
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task DialOut_CodePairsOnce_ASecondConsoleWithItIsRefused()
    {
        using var client = new RemoteConsoleClient();
        try
        {
            await client.AcceptAsync(0, "none", CancellationToken.None);
            var code = client.AcceptCode!;
            RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, code);
            await WaitUntilAsync(() => client.IsConnected && RemoteConsoleDialer.IsConnected);
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
            client.DropConnection();

            RemoteConsoleListener.TakeNotices();
            RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, code);
            await WaitUntilAsync(() => !RemoteConsoleDialer.IsActive);
            Assert.False(RemoteConsoleDialer.IsActive);
            Assert.Contains(RemoteConsoleListener.TakeNotices(), n => n.Message.Contains("already used"));
            Assert.False(client.IsConnected);
        }
        finally
        {
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public void Titles_KeepTheAdministratorMark_OnlyWhenElevated_AndOnlyOnce()
    {
        var once = ConsoleTitle.Decorate("#123 Opal");
        Assert.Equal(ConsoleTitle.Elevated ? "Administrator: #123 Opal" : "#123 Opal", once);
        Assert.Equal(once, ConsoleTitle.Decorate(once));
    }

    [Fact]
    public async Task Reconnect_WhileTheConsoleStillHoldsTheOldConnection_ReplacesIt()
    {
        var port = FreePort();
        var code = RemoteConsoleListener.Start(port, IPAddress.Loopback);
        try
        {
            using var client = new RemoteConsoleClient();
            await client.ConnectAsync($"127.0.0.1:{port}", code, CancellationToken.None);
            // This side has given the connection up; the console has not
            // noticed and still counts it as live.
            client.AbandonConnection();
            Assert.True(RemoteConsoleListener.IsConnected);

            // The reconnect proves the resume key, so it is not "busy": it
            // takes over.
            Assert.Equal("pong", StatusOf(await client.SendRequestAsync(Ping(), CancellationToken.None)));
            Assert.True(RemoteConsoleListener.IsConnected);
            Assert.Equal("pong", StatusOf(await client.SendRequestAsync(Ping(), CancellationToken.None)));
        }
        finally
        {
            RemoteConsoleListener.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public async Task DialOut_ANewConsoleWithTheSpentCode_IsRefused_AndTheLiveSessionStays()
    {
        using var client = new RemoteConsoleClient();
        try
        {
            await client.AcceptAsync(0, "none", CancellationToken.None);
            RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, client.AcceptCode!);
            await WaitUntilAsync(() => client.IsConnected && RemoteConsoleDialer.IsConnected);

            // The console goes away without a word while the proxy still holds
            // its end; another console that has only the spent code (not the
            // resume key) must not take that session over.
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
            Assert.True(client.IsConnected);
            RemoteConsoleDialer.Start("127.0.0.1", client.AcceptPort, client.AcceptCode!);
            await WaitUntilAsync(() => !RemoteConsoleDialer.IsActive, 5);
            // A fresh dialer has only the (spent) code, not the resume key: it
            // is refused, and the proxy's live session is untouched.
            Assert.False(RemoteConsoleDialer.IsActive);
            Assert.True(client.IsConnected);
        }
        finally
        {
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public void Codes_TypedFullWidthByAnIme_AreTheSameCode()
    {
        Assert.Equal("7Q4KM2XPH9RT", RemoteConsoleListener.NormalizeCode("７ｑ４ｋ－ｍ２ｘｐ－ｈ９ｒｔ"));
        Assert.Equal("7Q4KM2XPH9RT", RemoteConsoleClient.NormalizeCode("７Ｑ４Ｋ　Ｍ２ＸＰ　Ｈ９ＲＴ"));
        // Letters and digits outside ASCII are not part of any code.
        Assert.Equal("AB", RemoteConsoleListener.NormalizeCode("aéb٣"));
        Assert.Equal(RemoteConsoleListener.KeyId(new byte[32]), RemoteConsoleClient.KeyId(new byte[32]));
    }

    [Fact]
    public async Task Dialer_GivesUp_WhenTheServerStaysUnreachableForTheCodesLifetime()
    {
        var lifetime = RemoteConsoleListener.CodeLifetime;
        RemoteConsoleListener.CodeLifetime = TimeSpan.FromSeconds(1);
        try
        {
            RemoteConsoleListener.TakeNotices();
            RemoteConsoleDialer.Start("127.0.0.1", FreePort(), "AAAABBBBCCCC");
            // First attempt fails at once, the next comes after the 2 s backoff
            // and finds the lifetime spent.
            await WaitUntilAsync(() => !RemoteConsoleDialer.IsActive, 10);
            Assert.False(RemoteConsoleDialer.IsActive);
            Assert.Contains(RemoteConsoleListener.TakeNotices(), n => n.Message.Contains("could not be reached"));
        }
        finally
        {
            RemoteConsoleListener.CodeLifetime = lifetime;
            RemoteConsoleDialer.Stop("test", fromConsoleThread: true);
        }
    }

    [Fact]
    public void Notices_CarryNoControlCharacters()
    {
        // A refusal's text can come from the other side: no escape sequences reach the terminal.
        Assert.Equal("a [2Jb", RemoteConsoleListener.Printable("a\u001b[2Jb", 100));
        Assert.Equal("abc…", RemoteConsoleListener.Printable("abcdef", 3));
    }
}