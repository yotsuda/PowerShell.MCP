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
/// the two sides prove to each other that they hold the same code, and from
/// then on the connection carries the same length-prefixed JSON requests the
/// pipe does — one request, one response, served by
/// NamedPipeServer.HandleRequestAsync — for as long as it lasts. A user at
/// this machine keeps the prompt and sees every command the remote AI runs.
/// </para>
/// <para>
/// Security. The connection is TLS, under a certificate generated when the
/// listener starts; the proxy accepts it blind and binds the handshake to its
/// SHA-256 thumbprint instead, so a man in the middle cannot complete the
/// handshake. The pairing code is 8 characters from a 32-letter alphabet
/// (about 40 bits); the key is PBKDF2-SHA256 of it with 300,000 iterations,
/// so an offline guess costs real time. The server proves first: a proxy
/// that reaches a wrong host (a typo in the address) learns nothing and sends
/// nothing. Every handshake that does not end in a valid client proof counts
/// as a failure, and after five the listener stops. One proxy at a time.
/// </para>
/// <para>
/// Ownership. The console never calls ConsoleLiveness.SetOwned — an unowned
/// console is never reaped — and a local proxy probing it over the pipe is
/// told "remote" so it does not claim it. The proxy keeps the connection open
/// and pings while idle; a session that goes quiet for two minutes is
/// dropped so a dead peer cannot hold the console.
/// </para>
/// </remarks>
public static class RemoteConsoleListener
{
    public const int DefaultPort = 42777;
    public const int ProtocolVersion = 1;
    public const string Hello = "PSMCP-remote";
    public const int KeyIterations = 300_000;
    public const int MaxFailures = 5;
    public const int CodeLength = 8;

    // No 0/O or 1/I: the code is read off a screen and typed.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(120);

    private static readonly object _lock = new();
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static X509Certificate2? _certificate;
    private static byte[] _salt = [];
    private static byte[] _key = [];
    private static int _failures;
    private static int _port;
    private static string? _connectedFrom;

    public static bool IsListening { get { lock (_lock) return _listener != null; } }
    public static bool IsConnected { get { lock (_lock) return _connectedFrom != null; } }
    public static int Port { get { lock (_lock) return _port; } }
    public static string? ConnectedFrom { get { lock (_lock) return _connectedFrom; } }

    /// <summary>
    /// Binds the port and starts accepting. Returns the pairing code (raw,
    /// 8 characters; see <see cref="FormatCode"/> for display). Throws
    /// InvalidOperationException when already listening or the port is taken.
    /// Does not touch the host: the calling cmdlet prints and sets the title,
    /// because it runs on the runspace thread where a silent command would
    /// wait on itself.
    /// </summary>
    public static string Start(int port, IPAddress address)
    {
        lock (_lock)
        {
            if (_listener != null)
                throw new InvalidOperationException($"This console is already listening on port {_port}.");
            if (RemoteConsoleDialer.IsActive)
                throw new InvalidOperationException($"This console is already dialing {RemoteConsoleDialer.Target}. Disable-MCPRemoteControl first.");

            var code = NewPairingCode();
            var salt = RandomNumberGenerator.GetBytes(16);
            var key = DeriveKey(code, salt);
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
            _salt = salt;
            _key = key;
            _failures = 0;
            _port = port;
            _connectedFrom = null;

            var token = _cts.Token;
            _ = Task.Run(() => AcceptLoopAsync(listener, token), CancellationToken.None);
            return code;
        }
    }

    /// <summary>
    /// Stops listening and drops the session, if any. Returns false when not
    /// listening. From a background thread (lockout) the console is told
    /// why and its title reset; from the runspace thread (a cmdlet, module
    /// removal) the caller does that itself.
    /// </summary>
    public static bool Stop(string reason, bool fromConsoleThread)
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        X509Certificate2? certificate;
        lock (_lock)
        {
            if (_listener == null) return false;
            listener = _listener;
            cts = _cts;
            certificate = _certificate;
            _listener = null;
            _cts = null;
            _certificate = null;
            _key = [];
            _salt = [];
            _connectedFrom = null;
        }
        try { cts?.Cancel(); } catch { }
        try { listener.Stop(); } catch { }
        certificate?.Dispose();
        cts?.Dispose();
        if (!fromConsoleThread)
        {
            Notify($"Remote listener stopped: {reason}.", "Yellow");
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

    /// <summary>
    /// The peer address as a person reads it. The listener's socket is
    /// dual-stack, so an IPv4 peer arrives as an IPv4-mapped IPv6 address
    /// (::ffff:192.168.0.10) and is unmapped; a loopback peer — an SSH
    /// tunnel or a proxy on this machine — is "localhost" rather than ::1.
    /// </summary>
    internal static string DescribeRemote(IPAddress? address)
    {
        if (address == null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address) ? "localhost" : address.ToString();
    }

    public static string FormatCode(string code) =>
        code.Length == CodeLength ? $"{code[..4]}-{code[4..]}" : code;

    /// <summary>Upper case, letters and digits only: "7q4k-m2xp" and "7Q4K M2XP" are the same code.</summary>
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
            _ = Task.Run(() => ServeClientAsync(client, ct), CancellationToken.None);
        }
    }

    private static async Task ServeClientAsync(TcpClient client, CancellationToken ct)
    {
        var remote = DescribeRemote((client.Client.RemoteEndPoint as IPEndPoint)?.Address);
        X509Certificate2? certificate;
        byte[] key, salt;
        lock (_lock)
        {
            certificate = _certificate;
            key = _key;
            salt = _salt;
        }
        if (certificate == null) { client.Dispose(); return; }

        using (client)
        {
            client.NoDelay = true;
            using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
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
                if (!await HandshakeAsync(ssl, certificate, key, salt, remote, handshake.Token))
                    return;
            }
            catch
            {
                // A connection that drops mid-handshake is a failed attempt
                // too: a proxy given the wrong code verifies the server's
                // proof, fails, and simply closes.
                CountFailure(remote);
                return;
            }

            bool busy;
            lock (_lock)
            {
                busy = _listener == null || _connectedFrom != null;
                if (!busy) _connectedFrom = remote;
            }
            if (busy)
            {
                try
                {
                    await SendJsonAsync(ssl, new { ok = false, error = "busy", message = "Another MCP server is connected to this console. Disconnect it first." }, ct);
                }
                catch { }
                return;
            }

            try
            {
                await SendJsonAsync(ssl, new { ok = true, pid = Environment.ProcessId, version = MCPModuleInitializer.ServerVersion }, ct);
            }
            catch
            {
                lock (_lock) { if (_connectedFrom == remote) _connectedFrom = null; }
                return;
            }

            Notify($"Remote MCP server connected from {remote}{(remote == "localhost" ? " (through a tunnel or a proxy on this machine)" : "")}.", "Green");
            SetTitle($"PowerShell.MCP [remote control: connected from {remote}]");

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
                    await NamedPipeServer.HandleRequestAsync(ssl, request, ct);
                }
            }
            catch
            {
                // Disconnected, or idle past the limit.
            }
            finally
            {
                bool stillListening;
                lock (_lock)
                {
                    if (_connectedFrom == remote) _connectedFrom = null;
                    stillListening = _listener != null;
                }
                if (stillListening)
                {
                    Notify($"Remote MCP server at {remote} disconnected. Listening again on port {Port}.", "DarkGray");
                    SetTitle($"PowerShell.MCP [remote control: listening :{Port}]");
                }
            }
        }
    }

    /// <summary>
    /// Proves the shared code both ways over the TLS session, bound to the
    /// certificate the client actually saw. Returns false (after answering)
    /// when the proxy is not ours, is another version, or has the wrong code.
    /// </summary>
    private static async Task<bool> HandshakeAsync(SslStream ssl, X509Certificate2 certificate, byte[] key, byte[] salt, string remote, CancellationToken ct)
    {
        var helloJson = await NamedPipeServer.ReceiveMessageAsync(ssl, ct);
        using var hello = JsonDocument.Parse(helloJson);
        var root = hello.RootElement;

        if ((root.TryGetProperty("hello", out var h) ? h.GetString() : null) != Hello ||
            (root.TryGetProperty("v", out var v) ? v.GetInt32() : -1) != ProtocolVersion)
        {
            await SendJsonAsync(ssl, new { ok = false, error = "protocol", message = "Not a PowerShell.MCP remote proxy." }, ct);
            CountFailure(remote);
            return false;
        }

        var proxyVersion = root.TryGetProperty("proxy_version", out var pv) ? pv.GetString() : null;
        if (proxyVersion != MCPModuleInitializer.ServerVersion)
        {
            // Version lockstep, as over the pipe. Not a failed pairing: the
            // code may well be right.
            await SendJsonAsync(ssl, new
            {
                ok = false,
                error = "version_mismatch",
                server_version = MCPModuleInitializer.ServerVersion,
                message = $"PowerShell.MCP {MCPModuleInitializer.ServerVersion} on the remote console does not match this proxy ({proxyVersion}). Install the same version on both machines.",
            }, ct);
            return false;
        }

        var clientNonce = Convert.FromBase64String(root.GetProperty("client_nonce").GetString() ?? "");
        if (clientNonce.Length < 16) throw new InvalidDataException("client nonce too short");
        var serverNonce = RandomNumberGenerator.GetBytes(32);
        var thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA256);

        await SendJsonAsync(ssl, new
        {
            salt = Convert.ToBase64String(salt),
            iterations = KeyIterations,
            server_nonce = Convert.ToBase64String(serverNonce),
            server_proof = Convert.ToBase64String(Proof(key, "srv", clientNonce, serverNonce, thumbprint)),
        }, ct);

        var proofJson = await NamedPipeServer.ReceiveMessageAsync(ssl, ct);
        using var proofDoc = JsonDocument.Parse(proofJson);
        var clientProof = Convert.FromBase64String(proofDoc.RootElement.GetProperty("client_proof").GetString() ?? "");
        var expected = Proof(key, "cli", clientNonce, serverNonce, thumbprint);
        if (!CryptographicOperations.FixedTimeEquals(expected, clientProof))
        {
            await SendJsonAsync(ssl, new { ok = false, error = "bad_code", message = "The pairing code did not match." }, ct);
            CountFailure(remote);
            return false;
        }

        lock (_lock) _failures = 0;
        return true;
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
        lock (_lock) _notices.Enqueue((message, color));
    }

    internal static void SetTitle(string title)
    {
        lock (_lock) _pendingTitle = title;
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