using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Net.Http;
using System.Text.Json;

namespace PowerShell.MCP.Cmdlets;

/// <summary>
/// Base class for AI model argument completers.
/// Fetches available models from the API on first use and caches in memory.
/// Falls back to friendly aliases if the API key is not set or the call fails.
/// </summary>
public abstract class AIModelCompleter : IArgumentCompleter
{
    private static readonly HttpClient s_httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private string[]? _cachedModels;

    protected abstract string? GetApiKey();
    protected abstract string[] GetFallbackModels();
    protected abstract string[] FetchModelsFromAPI(string apiKey);

    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters)
    {
        var models = GetModels();
        var pattern = WildcardPattern.Get(
            string.IsNullOrEmpty(wordToComplete) ? "*" : $"{wordToComplete}*",
            WildcardOptions.IgnoreCase);

        foreach (var model in models)
        {
            if (pattern.IsMatch(model))
                yield return new CompletionResult(model);
        }
    }

    private string[] GetModels()
    {
        if (_cachedModels != null)
            return _cachedModels;

        var apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            _cachedModels = GetFallbackModels();
            return _cachedModels;
        }

        try
        {
            _cachedModels = FetchModelsFromAPI(apiKey);
        }
        catch
        {
            _cachedModels = GetFallbackModels();
        }

        return _cachedModels;
    }

    /// <summary>
    /// Helper: sends GET request and returns the response body.
    /// </summary>
    protected static string HttpGet(string url, Action<HttpRequestMessage> configureRequest)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        configureRequest(request);
        var response = s_httpClient.Send(request, HttpCompletionOption.ResponseContentRead);
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Completer for Invoke-Claude -Model parameter.
/// Fetches models from Anthropic GET /v1/models API.
/// </summary>
public class ClaudeModelCompleter : AIModelCompleter
{
    private static readonly string[] s_fallback =
        ["claude-opus-4-0-20250514", "claude-sonnet-4-20250514", "claude-haiku-4-5-20251001"];

    protected override string? GetApiKey()
        => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

    protected override string[] GetFallbackModels() => s_fallback;

    protected override string[] FetchModelsFromAPI(string apiKey)
    {
        var body = HttpGet("https://api.anthropic.com/v1/models?limit=100", req =>
        {
            req.Headers.Add("x-api-key", apiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");
        });

        using var doc = JsonDocument.Parse(body);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var modelId = id.GetString();
                    if (modelId != null)
                        models.Add(modelId);
                }
            }
        }

        return models.Count > 0 ? models.ToArray() : s_fallback;
    }
}

/// <summary>
/// Completer for Invoke-GPT -Model parameter.
/// Fetches models from OpenAI GET /v1/models API.
/// </summary>
public class GPTModelCompleter : AIModelCompleter
{
    private static readonly string[] s_fallback =
        ["gpt-4o", "gpt-4.1", "o3", "o4-mini"];

    protected override string? GetApiKey()
        => Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    protected override string[] GetFallbackModels() => s_fallback;

    protected override string[] FetchModelsFromAPI(string apiKey)
    {
        var body = HttpGet("https://api.openai.com/v1/models", req =>
        {
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
        });

        using var doc = JsonDocument.Parse(body);
        var models = new List<string>();

        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                {
                    var modelId = id.GetString();
                    if (modelId != null)
                        models.Add(modelId);
                }
            }
        }

        return models.Count > 0 ? models.ToArray() : s_fallback;
    }
}
