using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Services;

/// <summary>
/// Lets an MCP proxy on another machine drive this console.
/// </summary>
/// <remarks>
/// <para>
/// The console is the server, as it is for the local pipe: Enable-MCPRemoteControl
/// binds a TCP port, prints a pairing code, and waits. The proxy connects,
/// the two sides prove to each other that they hold the same key, and from
/// then on the connection carries the same length-prefixed JSON requests the
/// pipe does — one request, one response, served by
/// NamedPipeServer.HandleRequestAsync — for as long as it lasts. A user at
/// this machine keeps the prompt and sees every command the remote AI runs.
/// </para>
/// <para>
/// Security. The connection is TLS, under a certificate generated when the
/// listener starts; the proxy accepts it blind and binds the handshake to its
/// SHA-256 thumbprint instead, so a man in the middle cannot complete the
/// handshake. The server proves first, so a proxy that reaches a wrong host
/// learns nothing and sends nothing — which also means anyone who reaches the
/// port can take that proof away and guess the code offline. The code is
/// therefore short-lived: 12 characters from a 32-letter alphabet (60 bits),
/// PBKDF2-SHA256 with 300,000 iterations, good for ONE pairing and only for
/// <see cref="CodeLifetime"/>. A successful pairing hands the proxy a random
/// 256-bit resume key, which is what a reconnect after a dropped connection
/// proves; when a session ends, a new code is shown, and remote control stops
/// if nobody pairs or resumes within the lifetime. Failures are counted from
/// the moment a proof has been sent (a port scan or garbage is not an
/// attempt); after five the listener stops. Frames before authentication are
/// capped, and only a few handshakes run at once.
/// </para>
/// <para>
/// Ownership. The console never calls ConsoleLiveness.SetOwned — an unowned
/// console is never reaped — and a local proxy probing it over the pipe is
/// told "remote" so it does not claim it; other local requests are refused
/// while remote control is on. The proxy keeps the connection open and pings
/// while idle; a session that goes quiet for two minutes is dropped so a dead
/// peer cannot hold the console.
/// </para>
/// </remarks>
public static class RemoteConsoleListener
{
    public const int DefaultPort = 42777;
    public const int ProtocolVersion = 2;
    public const string Hello = "PSMCP-remote";
    public const int KeyIterations = 300_000;
    public const int MaxFailures = 5;
    public const int CodeLength = 12;
    internal const int MaxHandshakeFrame = 16 * 1024;
    private const int MaxPendingHandshakes = 4;

    /// <summary>How long a pairing code (or, after a session ends, the resume key) stays good. Settable for tests.</summary>
    internal static TimeSpan CodeLifetime { get; set; } = TimeSpan.FromMinutes(10);

    // No 0/O or 1/I: the code is read off a screen and typed.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ExpiryCheck = TimeSpan.FromSeconds(15);

    private static readonly object _lock = new();
    private static readonly SemaphoreSlim _pendingHandshakes = new(MaxPendingHandshakes, MaxPendingHandshakes);
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static X509Certificate2? _certificate;
    private static Timer? _expiryTimer;
    private static IPAddress[]? _allowFrom;
    private static byte[] _salt = [];
    private static byte[]? _codeKey;       // null once used
    private static string? _code;          // the code on screen, for messages
    private static byte[]? _resumeKey;     // set by a pairing, cleared by a goodbye
    private static DateTime _deadline;     // no session: pair or resume before this
    private static int _failures;
    private static int _port;
    private static string? _connectedFrom;
    private static object? _session;

    public static bool IsListening { get { lock (_lock) return _listener != null; } }
    public static bool IsConnected { get { lock (_lock) return _connectedFrom != null; } }
    public static int Port { get { lock (_lock) return _port; } }
    public static string? ConnectedFrom { get { lock (_lock) return _connectedFrom; } }
    /// <summary>The code currently valid, raw (null once used); for the cmdlet and tests.</summary>
    internal static string? CurrentCode { get { lock (_lock) return _codeKey != null ? _code : null; } }

    /// <summary>
    /// Binds the port and starts accepting. Returns the pairing code (raw,
    /// 12 characters; see <see cref="FormatCode"/> for display). Throws
    /// InvalidOperationException when already listening or the port is taken.
    /// With <paramref name="allowFrom"/>, connections from other addresses
    /// are closed before TLS. Does not touch the host: the calling cmdlet
    /// prints and sets the title, because it runs on the runspace thread
    /// where a silent command would wait on itself.
    /// </summary>
    public static string Start(int port, IPAddress address, IPAddress[]? allowFrom = null)
    {
        lock (_lock)
        {
            if (_listener != null)
                throw new InvalidOperationException($"This console is already listening on port {_port}.");
            if (RemoteConsoleDialer.IsActive)
                throw new InvalidOperationException($"This console is already dialing {RemoteConsoleDialer.Target}. Disable-MCPRemoteControl first.");

            var certificate = CreateCertificate();
            var listener = new TcpListener(address, port);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                listener.Server.DualMode = true;
            try
            {
                listener.Start();
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                certificate.Dispose();
                throw new InvalidOperationException(
                    $"Port {port} is already in use — another console on this machine is probably listening. Stop it first, or choose another -Port.", ex);
            }
            catch
            {
                certificate.Dispose();
                throw;
            }

            _listener = listener;
            _cts = new CancellationTokenSource();
            _certificate = certificate;
            _allowFrom = allowFrom is { Length: > 0 } ? allowFrom.Select(Unmap).ToArray() : null;
            _resumeKey = null;
            _failures = 0;
            _port = port;
            _connectedFrom = null;
            _session = null;
            var code = NewCodeLocked();
            _expiryTimer = new Timer(_ => CheckExpiry(), null, ExpiryCheck, ExpiryCheck);

            var token = _cts.Token;
            _ = Task.Run(() => AcceptLoopAsync(listener, token), CancellationToken.None);
            return code;
        }
    }

    /// <summary>A fresh code (new salt) and a fresh deadline. Called under _lock.</summary>
    private static string NewCodeLocked()
    {
        var code = NewPairingCode();
        _salt = RandomNumberGenerator.GetBytes(16);
        _codeKey = DeriveKey(code, _salt);
        _code = code;
        _deadline = DateTime.UtcNow + CodeLifetime;
        return code;
    }

    private static void CheckExpiry()
    {
        lock (_lock)
        {
            if (_listener == null || _connectedFrom != null || DateTime.UtcNow <= _deadline) return;
        }
        Stop($"no MCP server paired or reconnected within {CodeLifetime.TotalMinutes:0} minutes", fromConsoleThread: false);
    }

    /// <summary>
    /// Stops listening and drops the session, if any. Returns false when not
    /// listening. From a background thread (lockout, expiry) the console is
    /// told why and its title reset; from the runspace thread (a cmdlet,
    /// module removal) the caller does that itself.
    /// </summary>
    public static bool Stop(string reason, bool fromConsoleThread)
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        X509Certificate2? certificate;
        Timer? timer;
        lock (_lock)
        {
            if (_listener == null) return false;
            listener = _listener;
            cts = _cts;
            certificate = _certificate;
            timer = _expiryTimer;
            _listener = null;
            _cts = null;
            _certificate = null;
            _expiryTimer = null;
            _allowFrom = null;
            _codeKey = null;
            _code = null;
            _resumeKey = null;
            _salt = [];
            _connectedFrom = null;
            _session = null;
        }
        timer?.Dispose();
        try { cts?.Cancel(); } catch { }
        try { listener.Stop(); } catch { }
        certificate?.Dispose();
        cts?.Dispose();
        // Output the remote session left behind is not for whoever uses this
        // console next (a local AI would otherwise drain it as its own).
        ExecutionState.ClearCachedOutputs();
        if (!fromConsoleThread)
        {
            Notify($"Remote control ended: {reason}.", "Yellow");
            SetTitle($"#{Environment.ProcessId} ____");
        }
        else
        {
            // The caller reports and sets the title itself; a notice queued
            // earlier would now be stale.
            lock (_lock) { _notices.Clear(); _pendingTitle = null; }
        }
        return true;
    }

    private static IPAddress Unmap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>
    /// The peer address as a person reads it. The listener's socket is
    /// dual-stack, so an IPv4 peer arrives as an IPv4-mapped IPv6 address
    /// (::ffff:192.168.0.10) and is unmapped; a loopback peer — an SSH
    /// tunnel or a proxy on this machine — is "localhost" rather than ::1.
    /// </summary>
    internal static string DescribeRemote(IPAddress? address)
    {
        if (address == null) return "unknown";
        address = Unmap(address);
        return IPAddress.IsLoopback(address) ? "localhost" : address.ToString();
    }

    /// <summary>XXXX-XXXX-XXXX.</summary>
    public static string FormatCode(string code) =>
        code.Length == CodeLength ? string.Join('-', code.Chunk(4).Select(c => new string(c))) : code;

    /// <summary>Upper case, letters and digits only: "7q4k-m2xp-…" and "7Q4K M2XP …" are the same code.</summary>
    public static string NormalizeCode(string code) =>
        new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    internal static string NewPairingCode()
    {
        var chars = new char[CodeLength];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    // The proxy has copies of DeriveKey and Proof (RemoteConsoleClient); the
    // two must stay identical.
    internal static byte[] DeriveKey(string code, byte[] salt) => DeriveKey(code, salt, KeyIterations);

    internal static byte[] DeriveKey(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(NormalizeCode(code)), salt, iterations, HashAlgorithmName.SHA256, 32);

    internal static byte[] Proof(byte[] key, string label, byte[] clientNonce, byte[] serverNonce, byte[] thumbprint)
    {
        var data = new byte[label.Length + clientNonce.Length + serverNonce.Length + thumbprint.Length];
        var offset = Encoding.ASCII.GetBytes(label, data);
        clientNonce.CopyTo(data, offset); offset += clientNonce.Length;
        serverNonce.CopyTo(data, offset); offset += serverNonce.Length;
        thumbprint.CopyTo(data, offset);
        return HMACSHA256.HashData(key, data);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=PowerShell.MCP remote console", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // server authentication
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        // SChannel needs the private key in a key container, which a
        // freshly created certificate's key is not; a PKCS#12 round trip
        // gives it one for the life of this object.
        var pfx = created.Export(X509ContentType.Pkcs12);
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfx, null);
#else
        return new X509Certificate2(pfx, (string?)null);
#endif
    }

    private static async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested) return;
                continue;
            }
            // Not on the allow list, or too many handshakes already under
            // way: closed at once, before TLS, and not counted.
            var address = Unmap((client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None);
            IPAddress[]? allow;
            lock (_lock) allow = _allowFrom;
            if ((allow != null && !allow.Contains(address)) || !_pendingHandshakes.Wait(0))
            {
                client.Dispose();
                continue;
            }
            _ = Task.Run(() => ServeClientAsync(client, ct), CancellationToken.None);
        }
    }

    /// <summary>How far a handshake got: failures count only once a proof was sent.</summary>
    private sealed class Attempt { public bool ProofSent; }

    private static async Task ServeClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = DescribeRemote((client.Client.RemoteEndPoint as IPEndPoint)?.Address);
        X509Certificate2? certificate;
        lock (_lock) certificate = _certificate;
        if (certificate == null) { _pendingHandshakes.Release(); client.Dispose(); return; }

        using (client)
        {
            client.NoDelay = true;
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            var attempt = new Attempt();
            bool? resumed;
            try
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct);
                handshake.CancelAfter(HandshakeTimeout);
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                }, handshake.Token);
                resumed = await HandshakeAsync(ssl, certificate, remote, attempt, handshake.Token);
            }
            catch
            {
                // A proxy given the wrong code verifies the server's proof,
                // fails, and simply closes: once our proof is out, a drop is
                // a failed attempt. Before that it is noise (a port scan, a
                // stray TLS client) and costs nothing.
                if (attempt.ProofSent) CountFailure(remote);
                return;
            }
            finally
            {
                _pendingHandshakes.Release();
            }
            if (resumed == null) return; // refused, and answered

            var session = new object();
            bool busy;
            lock (_lock)
            {
                busy = _listener == null || _connectedFrom != null;
                if (!busy)
                {
                    _connectedFrom = remote;
                    _session = session;
                    _deadline = DateTime.MaxValue;
                    _failures = 0;
                }
            }
            if (busy)
            {
                try { await SendJsonAsync(ssl, new { ok = false, error = "busy", message = "Another MCP server is connected to this console. Disconnect it first." }, ct); }
                catch { }
                return;
            }

            // A fresh resume key on every pairing: what a reconnect proves.
            var resumeKey = RandomNumberGenerator.GetBytes(32);
            try
            {
                await SendJsonAsync(ssl, new { ok = true, pid = Environment.ProcessId, version = MCPModuleInitializer.ServerVersion, resume_key = Convert.ToBase64String(resumeKey) }, ct);
                lock (_lock) { if (_session == session) _resumeKey = resumeKey; }
            }
            catch
            {
                EndSession(session, goodbye: false);
                return;
            }

            Notify(resumed.Value
                ? $"Remote AI session reconnected from {remote}."
                : $"Remote AI session connected from {remote}{(remote == "localhost" ? " (through a tunnel or a proxy on this machine)" : "")}.", "Green");
            SetTitle($"PowerShell.MCP [remote control: connected from {remote}]");

            var goodbye = false;
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
                    if (RemoteConsoleDialer.IsGoodbye(request))
                    {
                        goodbye = true;
                        break;
                    }
                    await NamedPipeServer.HandleRequestAsync(ssl, request, ct);
                }
            }
            catch
            {
                // Disconnected, or idle past the limit.
            }
            finally
            {
                EndSession(session, goodbye);
            }
        }
    }

    /// <summary>
    /// The session is over. A new code goes up for whoever connects next; a
    /// dropped session (no goodbye) may also be resumed with its key, until
    /// the same deadline. Nobody by then, and remote control stops.
    /// </summary>
    private static void EndSession(object session, bool goodbye)
    {
        string code;
        lock (_lock)
        {
            if (_session != session) return;
            _session = null;
            _connectedFrom = null;
            if (_listener == null) return;
            if (goodbye) _resumeKey = null;
            code = NewCodeLocked();
        }
        var minutes = $"{CodeLifetime.TotalMinutes:0}";
        Notify(goodbye
            ? $"Remote AI session ended. To connect again within {minutes} minutes, give the AI the new pairing code {FormatCode(code)}; after that remote control ends."
            : $"Remote AI session disconnected. That MCP server can reconnect by itself within {minutes} minutes; another one needs the new pairing code {FormatCode(code)}. After that remote control ends.", "Yellow");
        SetTitle($"PowerShell.MCP [remote control: listening :{Port}]");
    }

    /// <summary>
    /// Proves the key both ways over the TLS session, bound to the
    /// certificate the client actually saw. Returns whether the proxy resumed
    /// a session (true) or paired with the code (false), or null after
    /// answering a refusal.
    /// </summary>
    private static async Task<bool?> HandshakeAsync(SslStream ssl, X509Certificate2 certificate, string remote, Attempt attempt, CancellationToken ct)
    {
        var helloJson = await NamedPipeServer.ReceiveMessageAsync(ssl, ct, MaxHandshakeFrame);
        using var hello = JsonDocument.Parse(helloJson);
        var root = hello.RootElement;

        if ((root.TryGetProperty("hello", out var h) ? h.GetString() : null) != Hello ||
            (root.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : -1) != ProtocolVersion)
        {
            await SendJsonAsync(ssl, new { ok = false, error = "protocol", message = "Not a PowerShell.MCP remote proxy." }, ct);
            return null;
        }

        var proxyVersion = root.TryGetProperty("proxy_version", out var pv) ? pv.GetString() : null;
        if (proxyVersion != MCPModuleInitializer.ServerVersion)
        {
            // Version lockstep, as over the pipe. Not a failed pairing: the
            // code may well be right. The version itself is not told to an
            // unauthenticated peer.
            await SendJsonAsync(ssl, new
            {
                ok = false,
                error = "version_mismatch",
                message = "The PowerShell.MCP versions on the two machines differ. Install the same version on both.",
            }, ct);
            return null;
        }

        var resume = root.TryGetProperty("resume", out var r) && r.ValueKind == JsonValueKind.True;
        byte[]? key;
        byte[] salt;
        string? refusal = null, error = null;
        lock (_lock)
        {
            salt = _salt;
            key = resume ? _resumeKey : _codeKey;
            if (_connectedFrom != null) { error = "busy"; refusal = "Another MCP server is connected to this console. Disconnect it first."; }
            else if (DateTime.UtcNow > _deadline) { error = "expired"; refusal = "The pairing code expired."; }
            else if (key == null) { error = resume ? "resume_unknown" : "code_used"; refusal = resume ? "This console no longer knows that session." : "That pairing code was already used."; }
        }
        if (refusal != null)
        {
            await SendJsonAsync(ssl, new { ok = false, error, message = refusal }, ct);
            return null;
        }

        var clientNonce = Convert.FromBase64String(root.GetProperty("client_nonce").GetString() ?? "");
        if (clientNonce.Length is < 16 or > 64) throw new InvalidDataException("bad client nonce");
        var serverNonce = RandomNumberGenerator.GetBytes(32);
        var thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA256);

        attempt.ProofSent = true;
        await SendJsonAsync(ssl, new
        {
            salt = Convert.ToBase64String(salt),
            iterations = KeyIterations,
            server_nonce = Convert.ToBase64String(serverNonce),
            server_proof = Convert.ToBase64String(Proof(key!, "srv", clientNonce, serverNonce, thumbprint)),
        }, ct);

        var proofJson = await NamedPipeServer.ReceiveMessageAsync(ssl, ct, MaxHandshakeFrame);
        using var proofDoc = JsonDocument.Parse(proofJson);
        var clientProof = Convert.FromBase64String(proofDoc.RootElement.GetProperty("client_proof").GetString() ?? "");
        var expected = Proof(key!, "cli", clientNonce, serverNonce, thumbprint);
        if (!CryptographicOperations.FixedTimeEquals(expected, clientProof))
        {
            await SendJsonAsync(ssl, new { ok = false, error = "bad_code", message = "The pairing code did not match." }, ct);
            CountFailure(remote);
            return null;
        }

        // A code pairs once.
        if (!resume) lock (_lock) { if (ReferenceEquals(_codeKey, key)) _codeKey = null; }
        return resume;
    }

    private static void CountFailure(string remote)
    {
        int failures;
        lock (_lock)
        {
            if (_listener == null) return;
            failures = ++_failures;
        }
        Notify($"Remote pairing attempt from {remote} failed ({failures}/{MaxFailures}).", "Yellow");
        if (failures >= MaxFailures)
            Stop($"{MaxFailures} failed pairing attempts", fromConsoleThread: false);
    }

    private static Task SendJsonAsync(Stream stream, object value, CancellationToken ct) =>
        NamedPipeServer.SendMessageAsync(stream, JsonSerializer.Serialize(value), ct);

    // Notices and the title are queued here from the listener's threads and
    // applied by the engine's timer tick (MCPPollingEngine.ps1), which runs
    // on the runspace thread while the prompt is idle — the one moment a
    // line can be printed without landing in the middle of something, and
    // one that always comes, so a connection made while the user was busy
    // is still reported afterwards.
    private static readonly Queue<(string Message, string Color)> _notices = new();
    private static string? _pendingTitle;

    internal static void Notify(string message, string color)
    {
        message = Printable(message, 600);
        lock (_lock) _notices.Enqueue((message, color));
    }

    internal static void SetTitle(string title)
    {
        title = Printable(title, 200);
        lock (_lock) _pendingTitle = title;
    }

    /// <summary>
    /// Text that may have come from the other side (a refusal's reason)
    /// reaches the terminal: no control characters, so no escape sequences
    /// can clear the screen, fake a prompt or touch the clipboard; and
    /// bounded.
    /// </summary>
    internal static string Printable(string text, int max)
    {
        var clean = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return clean.Length > max ? clean[..max] + "…" : clean;
    }

    /// <summary>Notices queued since the last call, oldest first; for the engine tick.</summary>
    public static (string Message, string Color)[] TakeNotices()
    {
        lock (_lock)
        {
            if (_notices.Count == 0) return [];
            var taken = _notices.ToArray();
            _notices.Clear();
            return taken;
        }
    }

    /// <summary>The title to set, or null when it is already current; for the engine tick.</summary>
    public static string? TakePendingTitle()
    {
        lock (_lock)
        {
            var title = _pendingTitle;
            _pendingTitle = null;
            return title;
        }
    }
}
