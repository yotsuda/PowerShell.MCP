using System.Management.Automation;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Sends a prompt to the OpenAI API with streaming.
/// Tokens are displayed on the console as they arrive via Host.UI.Write.
/// Returns an AIResponse object (empty format suppresses Out-Default display).
/// </summary>
[Cmdlet(VerbsLifecycle.Invoke, "GPT")]
[OutputType(typeof(AIResponse))]
public class InvokeGPTCmdlet : AIStreamingCmdletBase
{
    [Parameter]
    [ArgumentCompleter(typeof(GPTModelCompleter))]
    public new string? Model { get; set; }

    private const string DefaultModel = "gpt-4o";

    protected override string ProviderName => "OpenAI";

    protected override (string text, string model) CallAPI(string userContent)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new PSInvalidOperationException("OPENAI_API_KEY environment variable is not set.");

        var model = string.IsNullOrEmpty(Model) ? DefaultModel : Model;

        var json = BuildJson(model, userContent);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Add("Authorization", $"Bearer {apiKey}");
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

            w.WritePropertyName("messages");
            w.WriteStartArray();

            if (!string.IsNullOrEmpty(SystemPrompt))
            {
                w.WriteStartObject();
                w.WriteString("role", "system");
                w.WriteString("content", SystemPrompt);
                w.WriteEndObject();
            }

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
        if (root.TryGetProperty("choices", out var choices) &&
            choices.GetArrayLength() > 0)
        {
            var delta = choices[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var content))
            {
                return content.GetString();
            }
        }

        return null;
    }
}
