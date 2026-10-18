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
/// proves — also while this side still holds the old connection, which the
/// proven reconnect replaces. When a session ends, a new code is shown, and
/// remote control stops if nobody pairs or resumes within the lifetime. A
/// failed attempt is a client proof that does not match; after five the
/// listener stops. Frames before authentication are capped, and only a few
/// handshakes run at once.
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

    /// <summary>One connection that paired: who, and how to end it if a proven reconnect replaces it.</summary>
    private sealed class Session
    {
        public required string From { get; init; }
        public required TcpClient Client { get; init; }
        public required CancellationTokenSource Cts { get; init; }
    }

    private static readonly object _lock = new();
    private static readonly SemaphoreSlim _pendingHandshakes = new(MaxPendingHandshakes, MaxPendingHandshakes);
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static X509Certificate2? _certificate;
    private static Timer? _expiryTimer;
    private static IPAddress[]? _allowFrom;
    private static byte[] _salt = [];
    private static byte[]? _codeKey;            // null once used
    private static string? _code;               // the code on screen, for messages
    private static byte[]? _resumeKey;          // set by a pairing, cleared by a goodbye
    private static byte[]? _previousResumeKey;  // the one the latest reconnect proved, until it is used
    private static DateTime _deadline;          // no session: pair or resume before this
    private static int _failures;
    private static readonly HashSet<string> _brokenOffFrom = []; // told once per address per code: each code is a new target
    private static int _port;
    private static Session? _session;

    public static bool IsListening { get { lock (_lock) return _listener != null; } }
    public static bool IsConnected { get { lock (_lock) return _session != null; } }
    public static int Port { get { lock (_lock) return _port; } }
    public static string? ConnectedFrom { get { lock (_lock) return _session?.From; } }
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
            _previousResumeKey = null;
            _failures = 0;
            _port = port;
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
        _brokenOffFrom.Clear();
        return code;
    }

    private static void CheckExpiry() =>
        Stop($"no MCP server paired or reconnected within {CodeLifetime.TotalMinutes:0} minutes", fromConsoleThread: false, onlyIfExpired: true);

    /// <summary>
    /// Stops listening and drops the session, if any. Returns false when not
    /// listening (or, with <paramref name="onlyIfExpired"/>, when a session
    /// came in or the deadline moved meanwhile — decided under the lock, so a
    /// pairing that just completed is not torn down). From a background
    /// thread (lockout, expiry) the console is told why and its title reset;
    /// from the runspace thread (a cmdlet, module removal) the caller does
    /// that itself.
    /// </summary>
    public static bool Stop(string reason, bool fromConsoleThread, bool onlyIfExpired = false)
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        X509Certificate2? certificate;
        Timer? timer;
        Session? session;
        lock (_lock)
        {
            if (_listener == null) return false;
            if (onlyIfExpired && (_session != null || DateTime.UtcNow <= _deadline)) return false;
            listener = _listener;
            cts = _cts;
            certificate = _certificate;
            timer = _expiryTimer;
            session = _session;
            _listener = null;
            _cts = null;
            _certificate = null;
            _expiryTimer = null;
            _allowFrom = null;
            _codeKey = null;
            _code = null;
            _resumeKey = null;
            _previousResumeKey = null;
            _salt = [];
            _session = null;
        }
        timer?.Dispose();
        try { cts?.Cancel(); } catch { }
        try { listener.Stop(); } catch { }
        try { session?.Client.Dispose(); } catch { }
        certificate?.Dispose();
        // Output the remote session left behind, now or from a command still
        // running, is not for whoever uses this console next.
        ExecutionState.EndRemoteEpoch();
        if (!fromConsoleThread)
        {
            Notify($"Remote control ended: {reason}.", "Yellow");
            SetTitle($"#{Environment.ProcessId} ____");
        }
        else
        {
            ClearPendingNotices();
        }
        return true;
    }

    /// <summary>
    /// Drops queued notices and title: the caller (a cmdlet on the runspace
    /// thread) reports and sets the title itself, and anything queued earlier
    /// would now be stale.
    /// </summary>
    internal static void ClearPendingNotices()
    {
        lock (_lock) { _notices.Clear(); _pendingTitle = null; }
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

    /// <summary>
    /// Upper-case ASCII letters and digits only: "7q4k-m2xp-…", "7Q4K M2XP …"
    /// and the full-width "７Ｑ４Ｋ…" an IME produces are the same code.
    /// </summary>
    public static string NormalizeCode(string code) =>
        new string(code.Normalize(NormalizationForm.FormKC).Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();

    internal static string NewPairingCode()
    {
        var chars = new char[CodeLength];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    // The proxy has copies of DeriveKey, Proof and KeyId (RemoteConsoleClient);
    // the two must stay identical.
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

    /// <summary>
    /// Names a resume key in a hello without revealing it (a hash of 256
    /// random bits), so the acceptor knows which of its keys the connector
    /// holds — the latest, or the one before when the ack that carried the
    /// latest never arrived.
    /// </summary>
    internal static string KeyId(byte[] key) => Convert.ToHexString(SHA256.HashData(key))[..16];

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
            IPAddress address;
            try { address = Unmap((client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None); }
            catch { client.Dispose(); continue; }
            IPAddress[]? allow;
            lock (_lock) allow = _allowFrom;
            if ((allow != null && !allow.Contains(address)) || !_pendingHandshakes.Wait(0))
            {
                client.Dispose();
                continue;
            }
            _ = Task.Run(() => ServeClientAsync(listener, client, address, ct), CancellationToken.None);
        }
    }

    /// <summary>What a handshake established: a reconnect (and the key it proved) or a pairing with the code.</summary>
    private sealed record Paired(bool Resumed, byte[] KeyUsed);

    /// <summary>
    /// One connection. Called holding a _pendingHandshakes slot, released here
    /// exactly once whatever happens.
    /// </summary>
    private static async Task ServeClientAsync(TcpListener listener, TcpClient client, IPAddress address, CancellationToken ct)
    {
        var holdingSlot = true;
        void ReleaseSlot()
        {
            if (!holdingSlot) return;
            holdingSlot = false;
            _pendingHandshakes.Release();
        }

        using (client)
        {
            try
            {
                var remote = DescribeRemote(address);
                X509Certificate2? certificate;
                lock (_lock) certificate = ReferenceEquals(_listener, listener) ? _certificate : null;
                if (certificate == null) return;

                client.NoDelay = true;
                using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                Paired? paired;
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
                    paired = await HandshakeAsync(ssl, certificate, listener, remote, handshake.Token);
                }
                catch
                {
                    // A dropped or malformed handshake is not an attempt: only
                    // a client proof that does not match counts (HandshakeAsync).
                    return;
                }
                finally
                {
                    ReleaseSlot();
                }
                if (paired == null) return; // refused, and answered

                await RunSessionAsync(listener, client, ssl, remote, paired, ct);
            }
            catch
            {
                // Anything unexpected ends this connection only.
            }
            finally
            {
                ReleaseSlot();
            }
        }
    }

    private static async Task RunSessionAsync(TcpListener listener, TcpClient client, SslStream ssl, string remote, Paired paired, CancellationToken ct)
    {
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var session = new Session { From = remote, Client = client, Cts = sessionCts };
        Session? replaced = null;
        bool busy, stopped;
        lock (_lock)
        {
            // A reconnect that proved the resume key replaces a connection
            // this side still holds (the peer saw it drop first); a code
            // pairing never does.
            stopped = !ReferenceEquals(_listener, listener);
            busy = stopped || (_session != null && !paired.Resumed);
            if (!busy)
            {
                replaced = _session;
                _session = session;
                _deadline = DateTime.MaxValue;
                _failures = 0;
            }
        }
        if (busy)
        {
            try
            {
                await SendJsonAsync(ssl, stopped
                    ? new { ok = false, error = "expired", message = "Remote control of this console has ended." }
                    : new { ok = false, error = "busy", message = "Another MCP server is connected to this console. Disconnect it first." }, ct);
            }
            catch { }
            return;
        }
        // A pairing with a code is a new session: what an earlier one left
        // (cached output, a command still running) is not for this one. A
        // resume continues the same session and keeps it.
        NamedPipeServer.RemoteEpoch.Value = paired.Resumed ? ExecutionState.RemoteEpoch : ExecutionState.EndRemoteEpoch();
        if (replaced != null)
        {
            try { replaced.Cts.Cancel(); } catch { }
            try { replaced.Client.Dispose(); } catch { }
        }

        // A fresh resume key on every pairing: what the next reconnect proves.
        // The key just proved stays good too until a request arrives here, in
        // case this ack never reaches the other side.
        var resumeKey = RandomNumberGenerator.GetBytes(32);
        try
        {
            await SendJsonAsync(ssl, new { ok = true, pid = Environment.ProcessId, version = MCPModuleInitializer.ServerVersion, resume_key = Convert.ToBase64String(resumeKey) }, ct);
            lock (_lock)
            {
                if (_session == session)
                {
                    _previousResumeKey = paired.Resumed ? paired.KeyUsed : null;
                    _resumeKey = resumeKey;
                }
            }
        }
        catch
        {
            EndSession(session, goodbye: false);
            return;
        }

        Announce(() => _session == session,
            paired.Resumed
                ? $"Remote AI session reconnected from {remote}."
                : $"Remote AI session connected from {remote}{(remote == "localhost" ? " (through a tunnel or a proxy on this machine)" : "")}.", "Green",
            RemoteControl.ConnectedTitle(remote));

        var goodbye = false;
        NamedPipeServer.IsRemoteRequest.Value = true;
        try
        {
            var first = true;
            while (!sessionCts.IsCancellationRequested)
            {
                string request;
                using (var idle = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token))
                {
                    idle.CancelAfter(IdleTimeout);
                    request = await NamedPipeServer.ReceiveMessageAsync(ssl, idle.Token);
                }
                if (first)
                {
                    // The other side has the new key: the old one is done.
                    first = false;
                    lock (_lock) { if (_session == session) _previousResumeKey = null; }
                }
                if (RemoteConsoleDialer.IsGoodbye(request))
                {
                    goodbye = true;
                    break;
                }
                await NamedPipeServer.HandleRequestAsync(ssl, request, sessionCts.Token);
            }
        }
        catch
        {
            // Disconnected, idle past the limit, or replaced by a reconnect.
        }
        finally
        {
            EndSession(session, goodbye);
        }
    }

    /// <summary>
    /// The session is over (unless a reconnect already replaced it). A new
    /// code goes up for whoever connects next; a dropped session (no goodbye)
    /// may also be resumed with its key, until the same deadline. Nobody by
    /// then, and remote control stops.
    /// </summary>
    private static void EndSession(Session session, bool goodbye)
    {
        lock (_lock)
        {
            if (_session != session) return;
            _session = null;
            if (_listener == null) return;
            if (goodbye)
            {
                // Ended on purpose: nobody resumes it, so what it left is dropped now.
                _resumeKey = null;
                _previousResumeKey = null;
                ExecutionState.EndRemoteEpoch();
            }
            var code = NewCodeLocked();
            var minutes = $"{CodeLifetime.TotalMinutes:0}";
            // Queued under the lock, so a reconnect that follows at once
            // cannot have its notice and title overtaken by these.
            Notify(goodbye
                ? $"Remote AI session ended. New pairing code: {FormatCode(code)} ({minutes} minutes)."
                : $"Remote AI session disconnected; it reconnects by itself within {minutes} minutes. New pairing code: {FormatCode(code)}.", "Yellow");
            SetTitle(RemoteControl.WaitingTitle);
        }
    }

    /// <summary>
    /// Proves the key both ways over the TLS session, bound to the
    /// certificate the client actually saw. Returns how the proxy paired, or
    /// null after answering a refusal.
    /// </summary>
    private static async Task<Paired?> HandshakeAsync(SslStream ssl, X509Certificate2 certificate, TcpListener listener, string remote, CancellationToken ct)
    {
        var helloJson = await NamedPipeServer.ReceiveMessageAsync(ssl, ct, MaxHandshakeFrame);
        using var hello = JsonDocument.Parse(helloJson);
        var root = hello.RootElement;

        // The exact PowerShell.MCP version is checked next: the same version
        // means the same protocol, so there is no separate protocol number.
        if ((root.TryGetProperty("hello", out var h) ? h.GetString() : null) != Hello)
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
                message = "The PowerShell.MCP versions on the two machines differ. Run Update-Module PowerShell.MCP on both, or on the older one with -RequiredVersion <the newer one's version>.",
            }, ct);
            return null;
        }

        var resume = root.TryGetProperty("resume", out var r) && r.ValueKind == JsonValueKind.True;
        var resumeId = root.TryGetProperty("resume_id", out var rid) && rid.ValueKind == JsonValueKind.String ? rid.GetString() : null;
        byte[]? key;
        byte[] salt;
        string? error = null;
        lock (_lock)
        {
            salt = _salt;
            if (!ReferenceEquals(_listener, listener)) { key = null; error = "expired"; }
            else if (resume)
            {
                key = _resumeKey != null && KeyId(_resumeKey) == resumeId ? _resumeKey
                    : _previousResumeKey != null && KeyId(_previousResumeKey) == resumeId ? _previousResumeKey
                    : null;
                // While connected the deadline is not running: a proven
                // reconnect replaces the connection this side still holds.
                if (key == null) error = "resume_unknown";
                else if (_session == null && DateTime.UtcNow > _deadline) error = "expired";
            }
            else
            {
                key = _codeKey;
                if (_session != null) error = "busy";
                else if (DateTime.UtcNow > _deadline) error = "expired";
                else if (key == null) error = "code_used";
            }
        }
        if (error != null)
        {
            await SendJsonAsync(ssl, new { ok = false, error, message = error }, ct);
            return null;
        }

        var clientNonce = Convert.FromBase64String(root.GetProperty("client_nonce").GetString() ?? "");
        if (clientNonce.Length is < 16 or > 64) throw new InvalidDataException("bad client nonce");
        var serverNonce = RandomNumberGenerator.GetBytes(32);
        var thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA256);

        await SendJsonAsync(ssl, new
        {
            salt = Convert.ToBase64String(salt),
            iterations = KeyIterations,
            server_nonce = Convert.ToBase64String(serverNonce),
            server_proof = Convert.ToBase64String(Proof(key!, "srv", clientNonce, serverNonce, thumbprint)),
        }, ct);

        string proofJson;
        try
        {
            proofJson = await NamedPipeServer.ReceiveMessageAsync(ssl, ct, MaxHandshakeFrame);
        }
        catch
        {
            // It took this side's proof and left without its own: the AI's
            // side found the code wrong (a typo there), or someone collected
            // the proof to try codes against offline. The two look alike from
            // here. Not counted as a failure — then anyone could end remote
            // control by hanging up five times — but the person here is told.
            if (!resume) NoteBrokenOff(listener, remote);
            throw;
        }
        using var proofDoc = JsonDocument.Parse(proofJson);
        var clientProof = Convert.FromBase64String(proofDoc.RootElement.GetProperty("client_proof").GetString() ?? "");
        var expected = Proof(key!, "cli", clientNonce, serverNonce, thumbprint);
        if (!CryptographicOperations.FixedTimeEquals(expected, clientProof))
        {
            await SendJsonAsync(ssl, new { ok = false, error = "bad_code", message = "The pairing code did not match." }, ct);
            CountFailure(listener, remote);
            return null;
        }

        // A code pairs once, also when two handshakes with it ran at the same time.
        if (!resume)
        {
            bool spent;
            lock (_lock)
            {
                spent = !ReferenceEquals(_codeKey, key);
                if (!spent) _codeKey = null;
            }
            if (spent)
            {
                await SendJsonAsync(ssl, new { ok = false, error = "code_used", message = "code_used" }, ct);
                return null;
            }
        }
        return new Paired(resume, key!);
    }

    private static void CountFailure(TcpListener listener, string remote)
    {
        int failures;
        lock (_lock)
        {
            if (!ReferenceEquals(_listener, listener)) return;
            failures = ++_failures;
        }
        Notify($"Remote pairing attempt from {remote} failed ({failures}/{MaxFailures}).", "Yellow");
        if (failures >= MaxFailures)
            Stop($"{MaxFailures} failed pairing attempts", fromConsoleThread: false);
    }

    /// <summary>Once per address per code, so repeated connections cannot flood the console.</summary>
    private static void NoteBrokenOff(TcpListener listener, string remote)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_listener, listener) || !_brokenOffFrom.Add(remote)) return;
        }
        Notify($"A connection from {remote} broke off pairing (a mistyped code on the AI's side, or someone probing). If that is not your AI's machine: Disable-MCPRemoteControl.", "Yellow");
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
        title = ConsoleTitle.Decorate(Printable(title, 200));
        lock (_lock) _pendingTitle = title;
    }

    /// <summary>
    /// Queues a notice and the title together, only if <paramref name="current"/>
    /// still holds under the lock: a session that a stop (which clears the
    /// queue under the same lock) or a reconnect has already ended must not
    /// announce itself afterwards. <paramref name="current"/> takes no other lock.
    /// </summary>
    internal static void Announce(Func<bool> current, string message, string color, string title)
    {
        lock (_lock)
        {
            if (!current()) return;
            Notify(message, color);
            SetTitle(title);
        }
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
