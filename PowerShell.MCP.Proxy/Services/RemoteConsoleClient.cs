using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using PowerShell.MCP.Proxy.Models;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// The proxy's end of a session with a console on another machine (the
/// module's RemoteConsoleListener is the other end; the protocol is described
/// there). One connection, held open, carrying the same framed JSON requests
/// the pipe does, one at a time; pinged while idle so a dead peer is noticed.
/// </summary>
/// <remarks>
/// Reconnecting. The key derived from the pairing code is kept for the life
/// of the proxy, so a dropped connection is reopened without asking for the
/// code again — before the next request, never by resending one that was in
/// flight (the console may have run it). A listener restarted on the other
/// side has a new code and a new salt; the cached key is then useless and the
/// user is asked for the new code.
/// </remarks>
public sealed class RemoteConsoleClient : IDisposable
{
    public const int DefaultPort = 42777;
    public const int ProtocolVersion = 1;
    public const string Hello = "PSMCP-remote";

    private const int MaxIterations = 2_000_000;
    private const int MaxMessageLength = 10 * 1024 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(200);
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SessionWait = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AcceptHandshakeTimeout = TimeSpan.FromSeconds(20);
    private const int MaxAcceptFailures = 5;
    private const int KeyIterations = 300_000;
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private SslStream? _ssl;
    private Timer? _pingTimer;
    private string? _host;
    private int _port;
    private byte[]? _key;
    private byte[]? _salt;

    // ----- accept mode: the console dials us (Enable-MCPRemoteControl -ConnectTo) -----
    // The roles of the listening mode, swapped: we hold the code, the salt
    // and the certificate, we prove first, and the console redials on a
    // drop. _ssl/_tcp are then set from the accept loop, under _sessionLock,
    // and _sessionReady lets a waiting request pick the session up.
    private readonly object _sessionLock = new();
    private TcpListener? _acceptListener;
    private CancellationTokenSource? _acceptCts;
    private X509Certificate2? _acceptCertificate;
    private byte[] _acceptSalt = [];
    private byte[] _acceptKey = [];
    private string? _acceptCode;
    private int _acceptPort;
    private int _acceptFailures;
    private string? _acceptStopped;
    private string? _peer;
    // What a bare machine fetches over HTTP on the accept port; null when
    // this proxy cannot find its module folder.
    private volatile RemoteBootstrap? _bootstrap;
    private TaskCompletionSource<bool> _sessionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsConnected => _ssl != null;
    public bool IsAccepting => _acceptListener != null;
    public int AcceptPort => _acceptPort;
    internal string? AcceptCode => _acceptCode;
    public string? Host => _host;
    public int RemotePid { get; private set; }
    public string? RemoteVersion { get; private set; }

    /// <summary>"host" or "host:port" as the user gave it, or the console that dialed in, for messages.</summary>
    public string Target =>
        _acceptListener != null || _peer != null
            ? (_peer != null ? $"{_peer} (dialed in)" : $"(waiting on port {_acceptPort})")
            : _host == null ? "(not connected)" : _port == DefaultPort ? _host : $"{_host}:{_port}";

    /// <summary>host, host:port, [v6], [v6]:port.</summary>
    public static (string host, int port) ParseTarget(string target)
    {
        target = target.Trim();
        if (target.Length == 0) throw new ArgumentException("The host is empty.");
        if (target.StartsWith('['))
        {
            var end = target.IndexOf(']');
            if (end < 0) throw new ArgumentException($"'{target}' is not a valid host: unclosed '['.");
            var rest = target[(end + 1)..];
            if (rest.Length == 0) return (target[1..end], DefaultPort);
            if (rest.StartsWith(':') && int.TryParse(rest[1..], out var p6) && p6 is > 0 and <= 65535) return (target[1..end], p6);
            throw new ArgumentException($"'{target}' is not a valid host[:port].");
        }
        var colon = target.LastIndexOf(':');
        if (colon > 0 && target.IndexOf(':') == colon)
        {
            if (int.TryParse(target[(colon + 1)..], out var p) && p is > 0 and <= 65535) return (target[..colon], p);
            throw new ArgumentException($"'{target}' has an invalid port.");
        }
        return (target, DefaultPort);
    }

    public async Task<string> ConnectAsync(string target, string code, CancellationToken ct)
    {
        var (host, port) = ParseTarget(target);
        var normalized = NormalizeCode(code);
        if (normalized.Length == 0) throw new ArgumentException("The pairing code is empty.");

        await _gate.WaitAsync(ct);
        try
        {
            if (_ssl != null)
                throw new InvalidOperationException($"Already connected to {Target}. Call disconnect_remote_console first.");
            if (_acceptListener != null)
                throw new InvalidOperationException($"Already waiting for a console to dial in on port {_acceptPort}. Call disconnect_remote_console first.");
            _host = host;
            _port = port;
            _key = null;
            _salt = null;
            try
            {
                await OpenAsync(normalized, ct);
            }
            catch
            {
                _host = null;
                throw;
            }
            _pingTimer?.Dispose();
            _pingTimer = new Timer(_ => _ = PingAsync(), null, PingInterval, PingInterval);
            return $"Connected to remote console {Target} (PID {RemotePid}, PowerShell.MCP {RemoteVersion}). " +
                   "Commands sent with execute_remote_command run there, in a window the user at that machine can see; file paths are that machine's.";
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> DisconnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_host == null && _acceptListener == null && _peer == null) return "Not connected to a remote console.";
            var target = Target;
            var wasAccepting = _acceptListener != null || _peer != null;
            _pingTimer?.Dispose();
            _pingTimer = null;
            // A console that dialed in redials whenever the connection drops. Tell it this
            // end is deliberate so it stops; without this it keeps calling the closed port.
            // (A listening console is meant to keep listening, so it gets no goodbye.)
            var connected = wasAccepting && _ssl != null;
            var peer = _peer; // kept after a drop; cleared below
            var saidGoodbye = false;
            if (connected)
            {
                try
                {
                    using var timeout = new CancellationTokenSource(PingTimeout);
                    await WriteFrameAsync(_ssl!, JsonSerializer.Serialize(new GoodbyeParams(), RemoteConsoleJsonContext.Default.GoodbyeParams), timeout.Token);
                    saidGoodbye = true;
                }
                catch { /* already gone: it keeps redialing until Disable-MCPRemoteControl */ }
            }
            CloseLocked();
            StopAcceptingLocked();
            _host = null;
            _key = null;
            _salt = null;
            if (!wasAccepting)
                return $"Disconnected from {target}. The remote console keeps listening; connect_remote_console with the same code resumes.";
            if (saidGoodbye)
                return $"Disconnected from {target} and no longer accepting. The console there was told the session ended and stops remote control by itself. accept_remote_console again starts over with a new code.";
            return peer != null
                ? $"Stopped accepting. The console that had dialed in from {peer} was no longer connected, so it could not be told; if it was not ended there with Disable-MCPRemoteControl, it keeps redialing until the user runs that. accept_remote_console again starts over with a new code."
                : $"Stopped accepting; no console had dialed in. If the one-line command was already run, that console keeps dialing until the user runs Disable-MCPRemoteControl there. accept_remote_console again starts over with a new code.";
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Sends one framed request and returns the raw response (header, blank
    /// line, body — as over the pipe). A connection found closed is reopened
    /// before the request goes out; one that breaks after it went out is
    /// reported, since the console may have run the command.
    /// </summary>
    public async Task<string> SendRequestAsync(string json, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_host == null && _acceptListener == null)
                throw new InvalidOperationException(_acceptStopped != null
                    ? $"Not connected: accepting stopped ({_acceptStopped}). Call accept_remote_console again for a new code."
                    : "Not connected to a remote console. Call connect_remote_console (to reach a listening console) or accept_remote_console (to let a console dial in) first.");
            if (_ssl == null)
            {
                if (_acceptListener != null)
                {
                    // The console redials by itself; give it a moment.
                    Task ready;
                    lock (_sessionLock) ready = _sessionReady.Task;
                    using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    waiting.CancelAfter(SessionWait);
                    try { await ready.WaitAsync(waiting.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new InvalidOperationException(
                            $"The remote console has not dialed in on port {_acceptPort}" + (_peer != null ? " since the connection dropped" : "") +
                            ". It redials by itself once Enable-MCPRemoteControl -ConnectTo is running there; " + AcceptInstructions());
                    }
                }
                else
                {
                    await OpenAsync(null, ct);
                }
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(RequestTimeout);
                await WriteFrameAsync(_ssl!, json, timeout.Token);
                return await ReadFrameAsync(_ssl!, timeout.Token);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or AuthenticationException or InvalidDataException
                                       || (ex is OperationCanceledException && !ct.IsCancellationRequested))
            {
                var target = Target;
                CloseLocked();
                if (_acceptListener != null)
                    throw new InvalidOperationException(
                        $"The connection to {target} dropped while the request was in flight ({ex.Message}). The console redials by itself; " +
                        "a command that was running continues there — try again in a moment, then wait_for_remote_completion to collect its output.");
                var reconnected = false;
                try { await OpenAsync(null, ct); reconnected = true; } catch { }
                throw new InvalidOperationException(
                    $"The connection to {target} dropped while the request was in flight ({ex.Message}). " +
                    (reconnected
                        ? "Reconnected. A command that was running continues there; use wait_for_remote_completion to collect its output."
                        : "Reconnecting failed: the console may have stopped listening. Check it, then connect_remote_console again."));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PingAsync()
    {
        // A request in flight is liveness enough; do not queue behind it.
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            if (_ssl == null) return;
            try
            {
                using var timeout = new CancellationTokenSource(PingTimeout);
                await WriteFrameAsync(_ssl, JsonSerializer.Serialize(new PingParams(), RemoteConsoleJsonContext.Default.PingParams), timeout.Token);
                _ = await ReadFrameAsync(_ssl, timeout.Token);
            }
            catch
            {
                CloseLocked();
                // Dial mode reconnects here; in accept mode the console redials.
                if (_acceptListener == null)
                    try { await OpenAsync(null, CancellationToken.None); } catch { }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// TCP, TLS, then the pairing handshake. With a code, derives the key
    /// from the salt the console sends; without one, reuses the cached key
    /// (and refuses if the salt changed, which means a new code).
    /// </summary>
    private async Task OpenAsync(string? code, CancellationToken ct)
    {
        if (code == null && _key == null)
            throw new InvalidOperationException("No pairing key is held. Call connect_remote_console with the code shown on the remote console.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);

        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(_host!, _port, timeout.Token);
        }
        catch (Exception ex)
        {
            tcp.Dispose();
            throw new InvalidOperationException(
                $"Could not reach {Target}: {ex.Message.TrimEnd('.')}. Is Enable-MCPRemoteControl running there, and is inbound TCP {_port} allowed through its firewall?");
        }

        byte[]? thumbprint = null;
        // The certificate is self-signed and made when the listener started;
        // it is accepted as is, and the handshake below is bound to its
        // SHA-256 hash, so an interceptor's certificate makes the proofs fail.
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, _) =>
        {
            if (certificate == null) return false;
            thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA256);
            return true;
        });
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, timeout.Token);

            var clientNonce = RandomNumberGenerator.GetBytes(32);
            await WriteFrameAsync(ssl, JsonSerializer.Serialize(new RemoteHello { ClientNonce = Convert.ToBase64String(clientNonce) }, RemoteConsoleJsonContext.Default.RemoteHello), timeout.Token);

            using var challenge = JsonDocument.Parse(await ReadFrameAsync(ssl, timeout.Token));
            var root = challenge.RootElement;
            if (root.TryGetProperty("ok", out var okElement) && !okElement.GetBoolean())
                throw new InvalidOperationException(ErrorText(root));

            var salt = Convert.FromBase64String(root.GetProperty("salt").GetString() ?? "");
            var iterations = root.GetProperty("iterations").GetInt32();
            if (iterations <= 0 || iterations > MaxIterations)
                throw new InvalidOperationException("The remote console asked for an unreasonable key derivation; it is not a PowerShell.MCP listener.");
            var serverNonce = Convert.FromBase64String(root.GetProperty("server_nonce").GetString() ?? "");
            var serverProof = Convert.FromBase64String(root.GetProperty("server_proof").GetString() ?? "");

            byte[] key;
            if (_key != null && _salt != null && salt.AsSpan().SequenceEqual(_salt))
                key = _key;
            else if (code != null)
                key = DeriveKey(code, salt, iterations);
            else
                throw new InvalidOperationException("The remote console was restarted and shows a new pairing code. Call connect_remote_console again with that code.");

            var expectedServerProof = Proof(key, "srv", clientNonce, serverNonce, thumbprint!);
            if (!CryptographicOperations.FixedTimeEquals(expectedServerProof, serverProof))
                throw new InvalidOperationException(
                    $"{Target} did not prove the pairing code: the code is wrong, or something between here and there is intercepting the connection. Nothing was sent to it. (The listener there counts this as a failed attempt and stops after five.)");

            await WriteFrameAsync(ssl, JsonSerializer.Serialize(new RemoteProof { ClientProof = Convert.ToBase64String(Proof(key, "cli", clientNonce, serverNonce, thumbprint!)) }, RemoteConsoleJsonContext.Default.RemoteProof), timeout.Token);

            using var ack = JsonDocument.Parse(await ReadFrameAsync(ssl, timeout.Token));
            if (!(ack.RootElement.TryGetProperty("ok", out var ackOk) && ackOk.GetBoolean()))
                throw new InvalidOperationException(ErrorText(ack.RootElement));
            RemotePid = ack.RootElement.GetProperty("pid").GetInt32();
            RemoteVersion = ack.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;

            _key = key;
            _salt = salt;
            _tcp = tcp;
            _ssl = ssl;
        }
        catch
        {
            ssl.Dispose();
            tcp.Dispose();
            throw;
        }
    }

    private static string ErrorText(JsonElement root)
    {
        if (root.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } message) return message;
        if (root.TryGetProperty("error", out var e)) return $"The remote console refused the connection: {e.GetString()}.";
        return "The remote console refused the connection.";
    }

    private void CloseLocked()
    {
        lock (_sessionLock)
        {
            try { _ssl?.Dispose(); } catch { }
            try { _tcp?.Dispose(); } catch { }
            _ssl = null;
            _tcp = null;
            if (_sessionReady.Task.IsCompleted)
                _sessionReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void StopAcceptingLocked()
    {
        TcpListener? listener;
        CancellationTokenSource? cts;
        X509Certificate2? certificate;
        lock (_sessionLock)
        {
            listener = _acceptListener;
            cts = _acceptCts;
            certificate = _acceptCertificate;
            _acceptListener = null;
            _acceptCts = null;
            _acceptCertificate = null;
            _acceptKey = [];
            _acceptSalt = [];
            _acceptCode = null;
            _peer = null;
        }
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        certificate?.Dispose();
        cts?.Dispose();
    }

    public void Dispose()
    {
        _pingTimer?.Dispose();
        CloseLocked();
        StopAcceptingLocked();
        _gate.Dispose();
    }

    // ----- accept mode -----

    /// <summary>
    /// Waits for a console to dial in: binds a port (0 = any free one), makes
    /// the pairing code, and returns what the user must run on the remote
    /// machine. Called again while waiting, it repeats the instructions.
    /// </summary>
    public async Task<string> AcceptAsync(int port, string install, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_ssl != null || _host != null)
                throw new InvalidOperationException($"Already connected to {Target}. Call disconnect_remote_console first.");
            if (_acceptListener != null) return AcceptInstructions();

            // What a bare machine needs, served on the same port. Prepared
            // before binding: it may ask GitHub for the latest PowerShell.
            RemoteBootstrap? bootstrap = null;
            try { bootstrap = await RemoteBootstrap.PrepareAsync(install, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException and not ArgumentException) { Console.Error.WriteLine($"[WARN] Bootstrap not prepared: {ex.Message}"); }

            var code = NewPairingCode();
            var salt = RandomNumberGenerator.GetBytes(16);
            var key = DeriveKey(code, salt, KeyIterations);
            var certificate = CreateCertificate();
            var listener = new TcpListener(IPAddress.IPv6Any, port);
            listener.Server.DualMode = true;
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                certificate.Dispose();
                throw new InvalidOperationException($"Could not listen on port {port}: {ex.Message.TrimEnd('.')}.", ex);
            }

            CancellationToken token;
            lock (_sessionLock)
            {
                _acceptListener = listener;
                _acceptCts = new CancellationTokenSource();
                _acceptCertificate = certificate;
                _acceptSalt = salt;
                _acceptKey = key;
                _acceptCode = code;
                _acceptPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                _acceptFailures = 0;
                _acceptStopped = null;
                _peer = null;
                _bootstrap = bootstrap;
                if (_sessionReady.Task.IsCompleted)
                    _sessionReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                token = _acceptCts.Token;
            }
            _ = Task.Run(() => AcceptLoopAsync(listener, token), CancellationToken.None);
            _pingTimer?.Dispose();
            _pingTimer = new Timer(_ => _ = PingAsync(), null, PingInterval, PingInterval);
            return AcceptInstructions();
        }
        finally
        {
            _gate.Release();
        }
    }

    private string AcceptInstructions()
    {
        var host = Dns.GetHostName();
        var code = FormatCode(_acceptCode ?? "");
        var addresses = LocalAddresses();
        var where = addresses.Count > 0 ? $"If the name {host} does not resolve on the remote machine, replace it with an IP address of this machine: {string.Join(", ", addresses)}.\n" : "";
        var text = new StringBuilder();
        text.Append($"Waiting for a remote console to dial in on TCP port {_acceptPort} of this machine.\n\n");
        if (_bootstrap != null)
        {
            text.Append("Give the user this ONE line to paste on the remote machine: into PowerShell 7 if it is installed there, otherwise into Windows PowerShell " +
                        "(opened as Administrator if admin work is needed there). " +
                        "Nothing needs to be installed beforehand: it fetches PowerShell.MCP " + _bootstrap.ModuleVersion +
                        (_bootstrap.PwshVersion != null ? $" and, unless PowerShell 7.4+ is already there, a portable PowerShell {_bootstrap.PwshVersion}" : " (PowerShell 7.4+ must already be installed there: this server could not get a portable one)") +
                        " from this machine and checks their hashes. Pasted into PowerShell 7, remote control starts in that same window; pasted into Windows PowerShell, it opens a PowerShell 7 window that dials in:\n\n");
            text.Append(_bootstrap.OneLiner(host, _acceptPort, code)).Append("\n\n");
            text.Append(_bootstrap.DescribeInstall()).Append(" PowerShell 7.4+ and PowerShell.MCP " + _bootstrap.ModuleVersion +
                        " already there (the same build) are used as they are; another PowerShell.MCP version is left untouched and not loaded.\n\n");
            text.Append("Or, where PowerShell 7 and PowerShell.MCP " + _bootstrap.ModuleVersion + " are already installed, in PowerShell 7:\n");
        }
        else
        {
            text.Append("On the remote machine, in PowerShell 7 with PowerShell.MCP (the same version as here) imported, the user runs:\n");
        }
        // -RequiredVersion: with another version installed beside it, loading
        // by name alone may pick that one, which this proxy would refuse.
        var version = _bootstrap?.ModuleVersion ?? Program.ProxyVersion.TrimEnd('0').TrimEnd('.');
        text.Append($"  Import-Module PowerShell.MCP -RequiredVersion {version}; Enable-MCPRemoteControl -ConnectTo {host}:{_acceptPort} -Code {code}\n\n");
        text.Append(where);
        text.Append($"Inbound TCP {_acceptPort} must be allowed through THIS machine's firewall: on Windows a dialog asks once whether to allow PowerShell.MCP.Proxy.exe (private networks is enough); if it was dismissed, tell the user to allow it or add a rule. ");
        text.Append($"Accepting stops after {MaxAcceptFailures} failed pairing attempts. Once the console has dialed in, execute_remote_command works; disconnect_remote_console stops accepting.");
        return text.ToString();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
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
            _ = Task.Run(() => ServeIncomingAsync(client, ct), CancellationToken.None);
        }
    }

    /// <summary>
    /// One port, two protocols: a TLS ClientHello starts with 0x16, an HTTP
    /// request with a method name. The first byte decides, and is handed on.
    /// </summary>
    private async Task ServeIncomingAsync(TcpClient client, CancellationToken ct)
    {
        var first = new byte[1];
        int n;
        try
        {
            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            waiting.CancelAfter(AcceptHandshakeTimeout);
            n = await client.GetStream().ReadAsync(first, waiting.Token);
        }
        catch
        {
            client.Dispose();
            return;
        }
        if (n == 0) { client.Dispose(); return; }
        var stream = new PrefixedStream(first, client.GetStream());
        if (first[0] is (byte)'G' or (byte)'H')
        {
            var bootstrap = _bootstrap;
            using (client)
            {
                if (bootstrap != null) await bootstrap.ServeHttpAsync(stream, ct);
            }
            return;
        }
        await ServeDialedConsoleAsync(client, stream, ct);
    }

    private async Task ServeDialedConsoleAsync(TcpClient client, Stream stream, CancellationToken ct)
    {
        X509Certificate2? certificate;
        byte[] key, salt;
        lock (_sessionLock)
        {
            certificate = _acceptCertificate;
            key = _acceptKey;
            salt = _acceptSalt;
        }
        if (certificate == null) { client.Dispose(); return; }

        var peer = DescribePeer((client.Client.RemoteEndPoint as IPEndPoint)?.Address);
        client.NoDelay = true;
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        (bool ok, bool countsAsFailure, int pid, string? version) result;
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshake.CancelAfter(AcceptHandshakeTimeout);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, handshake.Token);
            result = await HandshakeAsAcceptorAsync(ssl, certificate, key, salt, handshake.Token);
        }
        catch
        {
            // A console given the wrong code verifies our proof, fails, and
            // closes without sending its own: that is a failed attempt too.
            ssl.Dispose();
            client.Dispose();
            CountAcceptFailure();
            return;
        }
        if (!result.ok)
        {
            ssl.Dispose();
            client.Dispose();
            if (result.countsAsFailure) CountAcceptFailure();
            return;
        }

        bool busy;
        lock (_sessionLock)
        {
            busy = _acceptListener == null || _ssl != null;
            if (!busy)
            {
                _tcp = client;
                _ssl = ssl;
                _peer = peer;
                RemotePid = result.pid;
                RemoteVersion = result.version;
            }
        }
        if (busy)
        {
            try { await WriteFrameAsync(ssl, JsonSerializer.Serialize(new RemoteAck { Ok = false, Error = "busy", Message = "Another console is already connected to this MCP server." }, RemoteConsoleJsonContext.Default.RemoteAck), ct); } catch { }
            ssl.Dispose();
            client.Dispose();
            return;
        }
        try
        {
            await WriteFrameAsync(ssl, JsonSerializer.Serialize(new RemoteAck { Ok = true, Version = Program.ProxyVersion }, RemoteConsoleJsonContext.Default.RemoteAck), ct);
        }
        catch
        {
            CloseLocked();
            return;
        }
        lock (_sessionLock) _sessionReady.TrySetResult(true);
    }

    /// <summary>
    /// The acceptor's half of the pairing, mirroring the module's listener:
    /// check the hello and the version, prove the code first, then verify the
    /// console's proof. A refusal is answered before returning.
    /// </summary>
    private async Task<(bool ok, bool countsAsFailure, int pid, string? version)> HandshakeAsAcceptorAsync(
        SslStream ssl, X509Certificate2 certificate, byte[] key, byte[] salt, CancellationToken ct)
    {
        using var hello = JsonDocument.Parse(await ReadFrameAsync(ssl, ct));
        var root = hello.RootElement;
        if ((root.TryGetProperty("hello", out var h) ? h.GetString() : null) != Hello ||
            (root.TryGetProperty("v", out var v) ? v.GetInt32() : -1) != ProtocolVersion ||
            (root.TryGetProperty("role", out var r) ? r.GetString() : null) != "console")
        {
            await RefuseAsync(ssl, "protocol", "Not a PowerShell.MCP console.", ct);
            return (false, true, 0, null);
        }
        var moduleVersion = root.TryGetProperty("module_version", out var mv) ? mv.GetString() : null;
        if (moduleVersion != Program.ProxyVersion)
        {
            await RefuseAsync(ssl, "version_mismatch",
                $"PowerShell.MCP {moduleVersion} on the remote console does not match this proxy ({Program.ProxyVersion}). Install the same version on both machines.", ct);
            return (false, false, 0, null);
        }
        var pid = root.TryGetProperty("pid", out var p) ? p.GetInt32() : 0;
        var clientNonce = Convert.FromBase64String(root.GetProperty("client_nonce").GetString() ?? "");
        if (clientNonce.Length < 16) throw new InvalidDataException("client nonce too short");
        var serverNonce = RandomNumberGenerator.GetBytes(32);
        var thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA256);

        await WriteFrameAsync(ssl, JsonSerializer.Serialize(new RemoteChallenge
        {
            Salt = Convert.ToBase64String(salt),
            Iterations = KeyIterations,
            ServerNonce = Convert.ToBase64String(serverNonce),
            ServerProof = Convert.ToBase64String(Proof(key, "srv", clientNonce, serverNonce, thumbprint)),
        }, RemoteConsoleJsonContext.Default.RemoteChallenge), ct);

        using var proof = JsonDocument.Parse(await ReadFrameAsync(ssl, ct));
        var clientProof = Convert.FromBase64String(proof.RootElement.GetProperty("client_proof").GetString() ?? "");
        if (!CryptographicOperations.FixedTimeEquals(Proof(key, "cli", clientNonce, serverNonce, thumbprint), clientProof))
        {
            await RefuseAsync(ssl, "bad_code", "The pairing code did not match.", ct);
            return (false, true, 0, null);
        }
        lock (_sessionLock) _acceptFailures = 0;
        return (true, false, pid, moduleVersion);
    }

    private static Task RefuseAsync(SslStream ssl, string error, string message, CancellationToken ct) =>
        WriteFrameAsync(ssl, JsonSerializer.Serialize(new RemoteAck { Ok = false, Error = error, Message = message }, RemoteConsoleJsonContext.Default.RemoteAck), ct);

    private void CountAcceptFailure()
    {
        bool stop;
        lock (_sessionLock)
        {
            if (_acceptListener == null) return;
            stop = ++_acceptFailures >= MaxAcceptFailures;
            if (stop) _acceptStopped = $"{MaxAcceptFailures} failed pairing attempts";
        }
        if (stop) StopAcceptingLocked();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=PowerShell.MCP proxy", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(30));
        // SChannel wants the private key in a key container: PKCS#12 round trip.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }

    internal static string NewPairingCode()
    {
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    public static string FormatCode(string code) => code.Length == 8 ? $"{code[..4]}-{code[4..]}" : code;

    /// <summary>The peer as a person reads it: IPv4 unmapped, loopback as "localhost".</summary>
    internal static string DescribePeer(IPAddress? address)
    {
        if (address == null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IPAddress.IsLoopback(address) ? "localhost" : address.ToString();
    }

    /// <summary>IPv4 addresses of this machine's interfaces that are up, for the instructions.</summary>
    private static List<string> LocalAddresses()
    {
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(unicast.Address)) continue;
                    result.Add(unicast.Address.ToString());
                }
            }
        }
        catch { }
        return result;
    }

    // ----- shared with the module's RemoteConsoleListener; keep identical -----

    public static string NormalizeCode(string code) =>
        new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    internal static byte[] DeriveKey(string normalizedCode, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(normalizedCode), salt, iterations, HashAlgorithmName.SHA256, 32);

    internal static byte[] Proof(byte[] key, string label, byte[] clientNonce, byte[] serverNonce, byte[] thumbprint)
    {
        var data = new byte[label.Length + clientNonce.Length + serverNonce.Length + thumbprint.Length];
        var offset = Encoding.ASCII.GetBytes(label, data);
        clientNonce.CopyTo(data, offset); offset += clientNonce.Length;
        serverNonce.CopyTo(data, offset); offset += serverNonce.Length;
        thumbprint.CopyTo(data, offset);
        return HMACSHA256.HashData(key, data);
    }

    // ----- framing: 4-byte little-endian length + UTF-8 body, as the pipe -----

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(BitConverter.GetBytes(body.Length), ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var lengthBytes = new byte[4];
        await ReadExactAsync(stream, lengthBytes, ct);
        var length = BitConverter.ToInt32(lengthBytes, 0);
        if (length <= 0 || length > MaxMessageLength)
            throw new InvalidDataException($"Invalid frame length {length}.");
        var body = new byte[length];
        await ReadExactAsync(stream, body, ct);
        return Encoding.UTF8.GetString(body);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) throw new IOException("The connection was closed.");
            read += n;
        }
    }
}