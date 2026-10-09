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
}

/// <summary>Third frame: the proxy's proof, sent only after the console proved itself.</summary>
public class RemoteProof
{
    [JsonPropertyName("client_proof")]
    public required string ClientProof { get; set; }
}

// The proxy is published trimmed, so every type that is serialized needs a
// source-generated context. Responses are read with JsonDocument.
[JsonSerializable(typeof(PingParams))]
[JsonSerializable(typeof(RemoteHello))]
[JsonSerializable(typeof(RemoteProof))]
[JsonSourceGenerationOptions(WriteIndented = false)]
public partial class RemoteConsoleJsonContext : JsonSerializerContext
{
}