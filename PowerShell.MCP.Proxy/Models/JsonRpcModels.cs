using System.Text.Json.Serialization;

namespace PowerShell.MCP.Proxy.Models;

public abstract class PowerShellMcpParams
{
    [JsonPropertyName("proxy_version")]
    public string ProxyVersion { get; } = Program.ProxyVersion;

    [JsonPropertyName("name")]
    public abstract string Name { get; }
}

// Parameter type definitions for PowerShell.MCP module
public class GetCurrentLocationParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "get_current_location";
}

public class ExecuteCommandParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "execute_command";

    [JsonPropertyName("pipeline")]
    public string Pipeline { get; set; } = string.Empty;

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 170;

    [JsonPropertyName("variables")]
    public Dictionary<string, string>? Variables { get; set; }
}

public class GetStatusParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "get_status";
}

public class ConsumeOutputParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "consume_output";
}

// Pushes a tool response back into a console's cache after the MCP client
// cancelled the call it was meant for. See PowerShellTools.StashIfCancelledAsync.
public class CacheOutputParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "cache_output";

    [JsonPropertyName("output")]
    public required string Output { get; set; }
}

// Response type for get_status
public class GetStatusResponse
{
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = PipeStatus.Standby;

    [JsonPropertyName("pipeline")]
    public string? Pipeline { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("output")]
    public string? Output { get; set; }

    [JsonPropertyName("statusLine")]
    public string? StatusLine { get; set; }

    /// <summary>
    /// True when the console's last sample saw unsubmitted text at its prompt —
    /// a human is composing a command there right now. Consumed by
    /// <c>close_console</c>'s human-presence guard. Sampled by the polling
    /// engine and served from the pipe thread, so it stays available even when
    /// the console's runspace is wedged.
    /// </summary>
    [JsonPropertyName("typedText")]
    public bool TypedText { get; set; }

    /// <summary>
    /// Age of the <see cref="TypedText"/> sample in seconds, or -1 when the
    /// console has never reported one. A stale or missing sample means UNKNOWN,
    /// and every consumer must fail open (allow the close) rather than block —
    /// <c>close_console</c> is the escape hatch for a wedged console and a
    /// detection failure must never take it away.
    /// </summary>
    [JsonPropertyName("typedTextAgeSeconds")]
    public double TypedTextAgeSeconds { get; set; } = -1;

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Process-level current working directory of the source console
    /// at the moment this status was captured. Populated by the DLL
    /// (see <c>NamedPipeServer.HandleGetStatus</c>) for every status
    /// response — busy / user_command / mcp_command / standby /
    /// completed. Used by the busy-auto-route path so a freshly-spawned
    /// console can land at the same cwd before re-running the AI's
    /// pipeline. Nullable only because <c>Directory.GetCurrentDirectory</c>
    /// can throw on a disconnected network drive — the version-lockstep
    /// connection guard rules out the "old DLL didn't send the field"
    /// case at the wire level.
    /// </summary>
    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }
}

[JsonSerializable(typeof(GetStatusResponse))]
public partial class GetStatusResponseContext : JsonSerializerContext { }

public class ClaimConsoleParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "claim_console";

    [JsonPropertyName("proxy_pid")]
    public int ProxyPid { get; set; }

    [JsonPropertyName("agent_id")]
    public string? AgentId { get; set; }
}

public class ClaimConsoleResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("new_pipe_name")]
    public string? NewPipeName { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

[JsonSerializable(typeof(ClaimConsoleResponse))]
public partial class ClaimConsoleResponseContext : JsonSerializerContext { }

public class SetWindowTitleParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "set_window_title";

    [JsonPropertyName("title")]
    public required string Title { get; set; }
}

public class ExecuteSilentParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "execute_silent";

    [JsonPropertyName("pipeline")]
    public required string Pipeline { get; set; }
}

public class CancelParams : PowerShellMcpParams
{
    [JsonPropertyName("name")]
    public override string Name { get; } = "cancel";
}

// Ack returned by the cancel handler.
public class CommandAckResponse
{
    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

[JsonSerializable(typeof(CommandAckResponse))]
public partial class CommandAckResponseContext : JsonSerializerContext { }
