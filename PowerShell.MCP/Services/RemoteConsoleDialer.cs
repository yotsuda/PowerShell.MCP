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
/// host:port -Code XXXX-XXXX-XXXX</c>; the proxy is waiting there with
/// <c>accept_remote_console</c>, which made the code.
/// </summary>
/// <remarks>
/// Same TLS, same proofs, same frames as the listener; only the roles are
/// swapped. The side that accepted the connection proves first — here the
/// proxy — so a console that dials a wrong address sends nothing. The code
/// pairs once; the proxy then hands over a resume key, and a dropped
/// connection is redialed with backoff, proving that key instead. A refused
/// handshake (wrong code, expired code, another version, proxy busy) is final
/// and ends remote control, with the reason printed in the console; so does
/// the proxy's goodbye (disconnect_remote_console).
/// </remarks>
public static class RemoteConsoleDialer
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private static readonly object _lock = new();
    // Written under _lock; read without it by Announce's check, which runs
    // under the listener's lock (taking this one there would invert the order).
    private static volatile CancellationTokenSource? _cts;
    private static string? _host;
    private static int _port;
    private static bool _connected;

    public static bool IsActive { get { lock (_lock) return _cts != null; } }
    public static bool IsConnected { get { lock (_lock) return _connected; } }
    public static string Target { get { lock (_lock) return _host == null ? "" : $"{_host}:{_port}"; } }
    public static string Host { get { lock (_lock) return _host ?? ""; } }

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
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "goodbye";
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Stops dialing and drops the session. False when not active.</summary>
    public static bool Stop(string reason, bool fromConsoleThread) => Stop(reason, fromConsoleThread, owner: null);

    /// <summary>
    /// With <paramref name="owner"/>, stops only the RunAsync that owns that
    /// token, checked and cleared in one step: a late one, after Disable and a
    /// new Enable, must not end its successor.
    /// </summary>
    private static bool Stop(string reason, bool fromConsoleThread, CancellationToken? owner)
    {
        CancellationTokenSource? cts;
        lock (_lock)
        {
            if (_cts == null || (owner != null && _cts.Token != owner)) return false;
            cts = _cts;
            _cts = null;
            _host = null;
            _connected = false;
        }
        try { cts.Cancel(); } catch { }
        // As RemoteConsoleListener.Stop: the remote session's output is not for the next user.
        ExecutionState.EndRemoteEpoch();
        if (!fromConsoleThread)
        {
            RemoteConsoleListener.Notify($"Remote control ended: {reason}.", "Yellow");
            RemoteConsoleListener.SetTitle($"#{Environment.ProcessId} ____");
        }
        return true;
    }

    private static void StopOwn(CancellationToken ct, string reason) => Stop(reason, fromConsoleThread: false, owner: ct);

    /// <summary>Notice and title for the RunAsync that owns <paramref name="ct"/>, unless it has been stopped.</summary>
    private static void Announce(CancellationToken ct, string message, string color, string title) =>
        RemoteConsoleListener.Announce(() => _cts?.Token == ct && !ct.IsCancellationRequested, message, color, title);

    private static async Task RunAsync(string host, int port, string code, CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(2);
        var announcedWaiting = false;
        // The code pairs once; after that, what a redial proves is the
        // resume key the proxy handed over.
        string? pairingCode = code;
        byte[]? resumeKey = null;
        // Unreachable for as long as a code lives (before pairing, or since
        // the last session dropped): the other side has stopped waiting, so
        // stop too rather than dial a closed port for ever.
        var unreachableSince = DateTime.UtcNow;
        long epoch = 0;
        while (!ct.IsCancellationRequested)
        {
            if (DateTime.UtcNow - unreachableSince > RemoteConsoleListener.CodeLifetime)
            {
                StopOwn(ct, $"the MCP server at {host}:{port} could not be reached for {RemoteConsoleListener.CodeLifetime.TotalMinutes:0} minutes");
                return;
            }
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

                (string? refused, bool retry, byte[]? nextResumeKey) outcome;
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    handshake.CancelAfter(HandshakeTimeout);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    }, handshake.Token);
                    outcome = await HandshakeAsConnectorAsync(ssl, pairingCode, resumeKey, thumbprint!, handshake.Token);
                }
                if (outcome.retry)
                {
                    // Reconnecting while the other side is still claiming
                    // the previous connection: try again shortly.
                    throw new IOException(outcome.refused);
                }
                if (outcome.refused != null)
                {
                    // Final: redialing would only count against the proxy's
                    // five attempts, or ask again for what it no longer has.
                    StopOwn(ct, outcome.refused);
                    return;
                }
                var resumed = resumeKey != null;
                resumeKey = outcome.nextResumeKey;
                pairingCode = null;

                lock (_lock) { if (_cts == null || _cts.Token != ct) return; _connected = true; }
                backoff = TimeSpan.FromSeconds(2);
                announcedWaiting = false;
                Announce(ct, $"Remote AI session {(resumed ? "reconnected" : "connected")} ({host}:{port}).", "Green", RemoteControl.ConnectedTitle(host));

                // The code pairs once per Enable, so one epoch covers every
                // redial; Stop ends it.
                if (!resumed) epoch = ExecutionState.EndRemoteEpoch();
                NamedPipeServer.RemoteEpoch.Value = epoch;
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
                            StopOwn(ct, "the AI ended the session (disconnect_remote_console)");
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
                unreachableSince = DateTime.UtcNow;
                Announce(ct, $"Remote AI session disconnected. Redialing {host}:{port} for up to {RemoteConsoleListener.CodeLifetime.TotalMinutes:0} minutes.", "Yellow", RemoteControl.WaitingTitle);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) return;
                if (!announcedWaiting)
                {
                    RemoteConsoleListener.Notify($"Cannot reach {host}:{port} yet ({ex.Message.TrimEnd('.')}); retrying for up to {RemoteConsoleListener.CodeLifetime.TotalMinutes:0} minutes, or until Disable-MCPRemoteControl.", "DarkGray");
                    announcedWaiting = true;
                }
            }

            try { await Task.Delay(backoff, ct); } catch (OperationCanceledException) { return; }
            backoff = backoff * 2 > MaxBackoff ? MaxBackoff : backoff * 2;
        }
    }

    /// <summary>
    /// The connector's half of the pairing: say hello, check that the proxy
    /// proves the key before anything is sent, then prove in turn. With a
    /// resume key it proves that; otherwise the code. Returns the reason the
    /// proxy (or this side) refused — with retry set when it is only "busy"
    /// while reconnecting — or the resume key for the next redial.
    /// </summary>
    private static async Task<(string? refused, bool retry, byte[]? resumeKey)> HandshakeAsConnectorAsync(SslStream ssl, string? code, byte[]? resumeKey, byte[] thumbprint, CancellationToken ct)
    {
        var clientNonce = RandomNumberGenerator.GetBytes(32);
        await SendJsonAsync(ssl, new
        {
            hello = RemoteConsoleListener.Hello,
            role = "console",
            module_version = MCPModuleInitializer.ServerVersion,
            pid = Environment.ProcessId,
            client_nonce = Convert.ToBase64String(clientNonce),
            resume = resumeKey != null,
            resume_id = resumeKey != null ? RemoteConsoleListener.KeyId(resumeKey) : null,
        }, ct);

        using var challenge = JsonDocument.Parse(await NamedPipeServer.ReceiveMessageAsync(ssl, ct, RemoteConsoleListener.MaxHandshakeFrame));
        var root = challenge.RootElement;
        if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            return Refused(root, resumeKey != null);

        var serverNonce = Convert.FromBase64String(root.GetProperty("server_nonce").GetString() ?? "");
        var serverProof = Convert.FromBase64String(root.GetProperty("server_proof").GetString() ?? "");
        byte[] key;
        if (resumeKey != null)
        {
            key = resumeKey;
        }
        else
        {
            var salt = Convert.FromBase64String(root.GetProperty("salt").GetString() ?? "");
            var iterations = root.GetProperty("iterations").GetInt32();
            if (iterations <= 0 || iterations > 2_000_000) return ("the other side asked for an unreasonable key derivation; it is not a PowerShell.MCP proxy", false, null);
            key = RemoteConsoleListener.DeriveKey(code!, salt, iterations);
        }

        var expected = RemoteConsoleListener.Proof(key, "srv", clientNonce, serverNonce, thumbprint);
        if (!CryptographicOperations.FixedTimeEquals(expected, serverProof))
            return ("the other side did not prove the pairing code (wrong code, wrong address, or an interceptor); nothing was sent to it", false, null);

        await SendJsonAsync(ssl, new { client_proof = Convert.ToBase64String(RemoteConsoleListener.Proof(key, "cli", clientNonce, serverNonce, thumbprint)) }, ct);

        using var ack = JsonDocument.Parse(await NamedPipeServer.ReceiveMessageAsync(ssl, ct, RemoteConsoleListener.MaxHandshakeFrame));
        if (!(ack.RootElement.TryGetProperty("ok", out var ackOk) && ackOk.ValueKind == JsonValueKind.True))
            return Refused(ack.RootElement, resumeKey != null);
        var next = Convert.FromBase64String(ack.RootElement.GetProperty("resume_key").GetString() ?? "");
        if (next.Length != 32) return ("the proxy sent no usable resume key", false, null);
        return (null, false, next);
    }

    private static (string? refused, bool retry, byte[]? resumeKey) Refused(JsonElement root, bool resuming)
    {
        var busy = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String && e.GetString() == "busy";
        return (RefusalText(root, resuming), resuming && busy, null);
    }

    /// <summary>
    /// The reason for a refusal, in our own words: until it has proved the
    /// code, the other side is unauthenticated, so its text is not shown.
    /// </summary>
    private static string RefusalText(JsonElement root, bool resuming)
    {
        var error = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
        return error switch
        {
            "version_mismatch" => "the PowerShell.MCP versions on the two machines differ; install the same version on both",
            "busy" => "the MCP server is already connected to another console",
            "bad_code" => "the pairing code did not match",
            "expired" => resuming ? "the MCP server stopped waiting for this console to reconnect" : "the pairing code expired; ask the AI for a new one (accept_remote_console)",
            "code_used" => "that pairing code was already used; ask the AI for a new one (accept_remote_console)",
            "resume_unknown" => "the MCP server no longer knows this session; ask the AI for a new code (accept_remote_console)",
            "protocol" => "the other side is not a PowerShell.MCP server",
            _ => "the MCP server refused the connection",
        };
    }

    private static Task SendJsonAsync(Stream stream, object value, CancellationToken ct) =>
        NamedPipeServer.SendMessageAsync(stream, JsonSerializer.Serialize(value), ct);
}
