using System.Text.Json.Serialization;
using PowerShell.MCP.Proxy.Services;

namespace PowerShell.MCP.Proxy.Models;

/// <summary>
/// Liveness probe of a remote console session. Answered by the console from
/// its pipe thread with {pid, status:"pong"}; never touches the runspace.
/// </summary>
public class PingParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "ping";
}

/// <summary>
/// Sent to a console that dialed in when disconnect_remote_console ends the
/// session on purpose, so it stops instead of redialing as after a drop. Not
/// answered; the proxy closes the connection right after.
/// </summary>
public class GoodbyeParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "goodbye";
}

/// <summary>First frame of the remote handshake, sent by the proxy after TLS.</summary>
public class RemoteHello
{
    [JsonPropertyName("hello")]
    public string Hello { get; set; } = RemoteConsoleClient.Hello;

    [JsonPropertyName("v")]
    public int Version { get; set; } = RemoteConsoleClient.ProtocolVersion;

    [JsonPropertyName("proxy_version")]
    public string ProxyVersion { get; set; } = Program.ProxyVersion;

    [JsonPropertyName("client_nonce")]
    public required string ClientNonce { get; set; }

    /// <summary>True to prove the resume key of an earlier pairing instead of the code.</summary>
    [JsonPropertyName("resume")]
    public bool Resume { get; set; }
}

/// <summary>Third frame: the proxy's proof, sent only after the console proved itself.</summary>
public class RemoteProof
{
    [JsonPropertyName("client_proof")]
    public required string ClientProof { get; set; }
}

/// <summary>Accept mode: the proxy's challenge to a console that dialed in (the proxy proves first).</summary>
public class RemoteChallenge
{
    [JsonPropertyName("salt")]
    public required string Salt { get; set; }

    [JsonPropertyName("iterations")]
    public int Iterations { get; set; }

    [JsonPropertyName("server_nonce")]
    public required string ServerNonce { get; set; }

    [JsonPropertyName("server_proof")]
    public required string ServerProof { get; set; }
}

/// <summary>Accept mode: the proxy's final answer, or a refusal at any step.</summary>
public class RemoteAck
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; set; }

    /// <summary>On success: a fresh random key the console proves when it redials.</summary>
    [JsonPropertyName("resume_key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeKey { get; set; }
}

// The proxy is published trimmed, so every type that is serialized needs a
// source-generated context. Responses are read with JsonDocument.
[JsonSerializable(typeof(PingParams))]
[JsonSerializable(typeof(GoodbyeParams))]
[JsonSerializable(typeof(RemoteHello))]
[JsonSerializable(typeof(RemoteProof))]
[JsonSerializable(typeof(RemoteChallenge))]
[JsonSerializable(typeof(RemoteAck))]
[JsonSourceGenerationOptions(WriteIndented = false)]
public partial class RemoteConsoleJsonContext : JsonSerializerContext
{
}