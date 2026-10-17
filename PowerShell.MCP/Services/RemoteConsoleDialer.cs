using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerShell.MCP.Services;

/// <summary>
/// The other way round: this console dials the proxy's machine, for the
/// usual case where the remote machine sits behind NAT, a cloud NSG or a
/// firewall nobody wants to open. <c>Enable-MCPRemoteControl -ConnectTo
/// host:port -Code XXXX-XXXX</c>; the proxy is waiting there with
/// <c>accept_remote_console</c>, which made the code.
/// </summary>
/// <remarks>
/// Same TLS, same proofs, same frames as the listener; only the roles are
/// swapped. The side that accepted the connection proves first — here the
/// proxy — so a console that dials a wrong address sends nothing. The
/// session then runs exactly as a listener's does (NamedPipeServer serves
/// one frame at a time). A dropped connection is redialed with backoff; a
/// refused handshake (wrong code, other version, proxy busy) is final and
/// ends remote control, with the reason printed in the console.
/// </remarks>
public static class RemoteConsoleDialer
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private static readonly object _lock = new();
    private static CancellationTokenSource? _cts;
    private static string? _host;
    private static int _port;
    private static bool _connected;

    public static bool IsActive { get { lock (_lock) return _cts != null; } }
    public static bool IsConnected { get { lock (_lock) return _connected; } }
    public static string Target { get { lock (_lock) return _host == null ? "" : $"{_host}:{_port}"; } }

    /// <summary>Starts dialing in the background. Throws when already active or listening.</summary>
    public static void Start(string host, int port, string code)
    {
        var normalized = RemoteConsoleListener.NormalizeCode(code);
        if (normalized.Length == 0) throw new ArgumentException("The pairing code is empty.");
        lock (_lock)
        {
            if (_cts != null) throw new InvalidOperationException($"This console is already dialing {Target}.");
            if (RemoteConsoleListener.IsListening)
                throw new InvalidOperationException($"This console is already listening on port {RemoteConsoleListener.Port}. Disable-MCPRemoteControl first.");
            _cts = new CancellationTokenSource();
            _host = host;
            _port = port;
            _connected = false;
            var token = _cts.Token;
            _ = Task.Run(() => RunAsync(host, port, normalized, token), CancellationToken.None);
        }
    }

    /// <summary>True for the proxy's "goodbye" frame (sent by disconnect_remote_console).</summary>
    internal static bool IsGoodbye(string request)
    {
        try
        {
            using var doc = JsonDocument.Parse(request);
            return doc.RootElement.TryGetProperty("name", out var name) && name.GetString() == "goodbye";
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Stops dialing and drops the session. False when not active.</summary>
    public static bool Stop(string reason, bool fromConsoleThread)
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (_cts == null) return false;
            cts = _cts;
            _cts = null;
            _host = null;
            _connected = false;
        }
        try { cts.Cancel(); } catch { }
        cts.Dispose();
        if (!fromConsoleThread)
        {
            RemoteConsoleListener.Notify($"Remote control ended: {reason}.", "Yellow");
            RemoteConsoleListener.SetTitle($"#{Environment.ProcessId} ____");
        }
        return true;
    }

    private static async Task RunAsync(string host, int port, string code, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);
        var announcedWaiting = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcp = new TcpClient { NoDelay = true };
                using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    connecting.CancelAfter(ConnectTimeout);
                    await tcp.ConnectAsync(host, port, connecting.Token);
                }

                byte[]? thumbprint = null;
                using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, _) =>
                {
                    if (certificate == null) return false;
                    thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA256);
                    return true;
                });

                string? refused;
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    handshake.CancelAfter(HandshakeTimeout);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    }, handshake.Token);
                    refused = await HandshakeAsConnectorAsync(ssl, code, thumbprint!, handshake.Token);
                }
                if (refused != null)
                {
                    // Final: redialing with the same code would only count
                    // against the proxy's five attempts.
                    Stop(refused, fromConsoleThread: false);
                    return;
                }

                lock (_lock) _connected = true;
                backoff = TimeSpan.FromSeconds(2);
                announcedWaiting = false;
                RemoteConsoleListener.Notify($"Remote AI session connected ({host}:{port}).", "Green");
                RemoteConsoleListener.SetTitle($"PowerShell.MCP [remote control: connected to {host}:{port}]");

                NamedPipeServer.IsRemoteRequest.Value = true;
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        string request;
                        using (var idle = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            idle.CancelAfter(IdleTimeout);
                            request = await NamedPipeServer.ReceiveMessageAsync(ssl, idle.Token);
                        }
                        // disconnect_remote_console ended the session on purpose: stop for good
                        // instead of redialing as after a drop.
                        if (IsGoodbye(request))
                        {
                            lock (_lock) _connected = false;
                            Stop("the AI ended the session (disconnect_remote_console)", fromConsoleThread: false);
                            return;
                        }
                        await NamedPipeServer.HandleRequestAsync(ssl, request, ct);
                    }
                }
                catch
                {
                    // Dropped or idle: fall through to redial.
                }
                finally
                {
                    lock (_lock) _connected = false;
                }
                if (ct.IsCancellationRequested) return;
                RemoteConsoleListener.Notify($"Remote AI session disconnected. Redialing {host}:{port}.", "Yellow");
                RemoteConsoleListener.SetTitle($"PowerShell.MCP [remote control: connecting to {host}:{port}]");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!announcedWaiting)
                {
                    RemoteConsoleListener.Notify($"Cannot reach {host}:{port} yet ({ex.Message.TrimEnd('.')}); retrying until Disable-MCPRemoteControl.", "DarkGray");
                    announcedWaiting = true;
                }
            }

            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { return; }
            backoff = backoff * 2 > MaxBackoff ? MaxBackoff : backoff * 2;
        }
    }

    /// <summary>
    /// The connector's half of the pairing: say hello, check that the proxy
    /// proves the code before anything is sent, then prove in turn. Returns
    /// null on success, or the reason the proxy (or this side) refused.
    /// </summary>
    private static async Task<string?> HandshakeAsConnectorAsync(SslStream ssl, string code, byte[] thumbprint, CancellationToken ct)
    {
        var clientNonce = RandomNumberGenerator.GetBytes(32);
        await SendJsonAsync(ssl, new
        {
            hello = RemoteConsoleListener.Hello,
            v = RemoteConsoleListener.ProtocolVersion,
            role = "console",
            module_version = MCPModuleInitializer.ServerVersion,
            pid = Environment.ProcessId,
            client_nonce = Convert.ToBase64String(clientNonce),
        }, ct);

        using var challenge = JsonDocument.Parse(await NamedPipeServer.ReceiveMessageAsync(ssl, ct));
        var root = challenge.RootElement;
        if (root.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
            return RefusalText(root);

        var salt = Convert.FromBase64String(root.GetProperty("salt").GetString() ?? "");
        var iterations = root.GetProperty("iterations").GetInt32();
        if (iterations <= 0 || iterations > 2_000_000) return "the other side asked for an unreasonable key derivation; it is not a PowerShell.MCP proxy";
        var serverNonce = Convert.FromBase64String(root.GetProperty("server_nonce").GetString() ?? "");
        var serverProof = Convert.FromBase64String(root.GetProperty("server_proof").GetString() ?? "");

        var key = RemoteConsoleListener.DeriveKey(code, salt, iterations);
        var expected = RemoteConsoleListener.Proof(key, "srv", clientNonce, serverNonce, thumbprint);
        if (!CryptographicOperations.FixedTimeEquals(expected, serverProof))
            return "the other side did not prove the pairing code (wrong code, wrong address, or an interceptor); nothing was sent to it";

        await SendJsonAsync(ssl, new { client_proof = Convert.ToBase64String(RemoteConsoleListener.Proof(key, "cli", clientNonce, serverNonce, thumbprint)) }, ct);

        using var ack = JsonDocument.Parse(await NamedPipeServer.ReceiveMessageAsync(ssl, ct));
        if (!(ack.RootElement.TryGetProperty("ok", out var ackOk) && ackOk.GetBoolean()))
            return RefusalText(ack.RootElement);
        return null;
    }

    private static string RefusalText(JsonElement root)
    {
        if (root.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } message) return message.TrimEnd('.');
        if (root.TryGetProperty("error", out var e)) return $"the proxy refused the connection ({e.GetString()})";
        return "the proxy refused the connection";
    }

    private static Task SendJsonAsync(Stream stream, object value, CancellationToken ct) =>
        NamedPipeServer.SendMessageAsync(stream, JsonSerializer.Serialize(value), ct);
}