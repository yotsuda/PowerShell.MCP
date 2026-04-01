using System.Management.Automation;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Base class for AI streaming cmdlets.
/// Handles pipeline input accumulation, SSE streaming with Host.UI.Write,
/// and outputs AIResponse (whose custom format suppresses Out-Default display).
/// </summary>
public abstract class AIStreamingCmdletBase : PSCmdlet
{
    internal static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>
    /// Holds the text output from the last Invoke-Claude or Invoke-GPT call.
    /// The MCP polling engine clears this before each command execution and
    /// reads it afterward, so only MCP-triggered results are included in the response.
    /// </summary>
    public static string? LastResponse { get; set; }

    private readonly List<string> _promptLines = [];

    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string Prompt { get; set; } = null!;

    [Parameter(Position = 1)]
    public string? SystemPrompt { get; set; }

    [Parameter]
    public string? Model { get; set; }

    [Parameter]
    [ValidateRange(1, 128000)]
    public int MaxTokens { get; set; } = 4096;

    /// <summary>
    /// Provider name for AIResponse metadata (e.g., "Anthropic", "OpenAI").
    /// </summary>
    protected abstract string ProviderName { get; }

    protected override void ProcessRecord()
    {
        _promptLines.Add(Prompt);
    }

    protected override void EndProcessing()
    {
        var userContent = string.Join("\n", _promptLines);

        // Always stream to console via Host.UI.Write.
        // Always WriteObject(AIResponse) for pipeline/variable capture.
        // AIResponse has a custom format (empty view) so Out-Default shows nothing,
        // avoiding double display. Piped or assigned, the object carries the full text.
        var (text, resolvedModel) = CallAPI(userContent);

        LastResponse = text;
        WriteObject(new AIResponse(text, resolvedModel, ProviderName));
    }

    /// <summary>
    /// Implemented by derived classes to call the provider-specific API.
    /// Returns (responseText, resolvedModelName).
    /// </summary>
    protected abstract (string text, string model) CallAPI(string userContent);

    /// <summary>
    /// Reads an SSE stream, extracts text deltas via the provided parser,
    /// streams each chunk to the console via Host.UI.Write,
    /// and returns the full accumulated text.
    /// </summary>
    protected string ReadSSEStream(HttpRequestMessage request, Func<JsonElement, string?> parseDelta)
    {
        var response = s_httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response);

        using var stream = response.Content.ReadAsStream();
        using var reader = new StreamReader(stream);

        var result = new StringBuilder();
        bool wroteAny = false;

        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("data: ", StringComparison.Ordinal))
                continue;

            var data = line.AsSpan(6);
            if (data.SequenceEqual("[DONE]"))
                break;

            try
            {
                using var doc = JsonDocument.Parse(data.ToString());
                var chunk = parseDelta(doc.RootElement);

                if (chunk != null)
                {
                    result.Append(chunk);
                    Host.UI.Write(chunk);
                    wroteAny = true;
                }
            }
            catch (JsonException)
            {
                // Skip malformed SSE chunks rather than aborting the entire stream.
            }
        }

        // No Host.UI.WriteLine() here — the formatter's output provides the trailing newline.
        return result.ToString();
    }

    protected void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var cmdletName = MyInvocation.MyCommand.Name;
            throw new HttpRequestException($"{cmdletName}: API returned {(int)response.StatusCode}: {body}");
        }
    }
}
