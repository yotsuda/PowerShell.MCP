using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
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

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private SslStream? _ssl;
    private Timer? _pingTimer;
    private string? _host;
    private int _port;
    private byte[]? _key;
    private byte[]? _salt;

    public bool IsConnected => _ssl != null;
    public string? Host => _host;
    public int RemotePid { get; private set; }
    public string? RemoteVersion { get; private set; }

    /// <summary>"host" or "host:port" as the user gave it, for messages.</summary>
    public string Target => _host == null ? "(not connected)" : _port == DefaultPort ? _host : $"{_host}:{_port}";

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
            if (_host == null) return "Not connected to a remote console.";
            var target = Target;
            _pingTimer?.Dispose();
            _pingTimer = null;
            CloseLocked();
            _host = null;
            _key = null;
            _salt = null;
            return $"Disconnected from {target}. The remote console keeps listening; connect_remote_console with the same code resumes.";
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
            if (_host == null)
                throw new InvalidOperationException("Not connected to a remote console. Call connect_remote_console first.");
            if (_ssl == null)
                await OpenAsync(null, ct);

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
                CloseLocked();
                var reconnected = false;
                try { await OpenAsync(null, ct); reconnected = true; } catch { }
                throw new InvalidOperationException(
                    $"The connection to {Target} dropped while the request was in flight ({ex.Message}). " +
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
        try { _ssl?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _ssl = null;
        _tcp = null;
    }

    public void Dispose()
    {
        _pingTimer?.Dispose();
        CloseLocked();
        _gate.Dispose();
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