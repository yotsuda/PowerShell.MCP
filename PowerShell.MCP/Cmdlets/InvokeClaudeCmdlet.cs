using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Sends a prompt to the Anthropic Claude API with streaming.
/// Tokens are displayed on the console as they arrive via Host.UI.Write.
/// Returns an AIResponse object (empty format suppresses Out-Default display).
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "Claude")]
[OutputType(typeof(AIResponse))]
public class InvokeClaudeCmdlet : AIStreamingCmdletBase
{
    [Parameter]
    [ArgumentCompleter(typeof(ClaudeModelCompleter))]
    public new string? Model { get; set; }

    private const string DefaultModel = "claude-sonnet-4-20250514";

    protected override string ProviderName => "Anthropic";

    protected override (string text, string model) CallAPI(string userContent)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new PSInvalidOperationException("ANTHROPIC_API_KEY environment variable is not set.");

        var model = string.IsNullOrEmpty(Model) ? DefaultModel : Model;

        var json = BuildJson(model, userContent);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var text = ReadSSEStream(request, ParseDelta);
        return (text, model);
    }

    private string BuildJson(string model, string userContent)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("model", model);
            w.WriteNumber("max_tokens", MaxTokens);
            w.WriteBoolean("stream", true);

            if (!string.IsNullOrEmpty(SystemPrompt))
                w.WriteString("system", SystemPrompt);

            w.WritePropertyName("messages");
            w.WriteStartArray();
            w.WriteStartObject();
            w.WriteString("role", "user");
            w.WriteString("content", userContent);
            w.WriteEndObject();
            w.WriteEndArray();

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string? ParseDelta(JsonElement root)
    {
        if (root.TryGetProperty("type", out var type) &&
            type.GetString() == "content_block_delta" &&
            root.TryGetProperty("delta", out var delta) &&
            delta.TryGetProperty("text", out var text))
        {
            return text.GetString();
        }

        return null;
    }
}
