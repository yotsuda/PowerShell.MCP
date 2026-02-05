using PowerShell.MCP.Proxy.Models;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

public class PowerShellService : IPowerShellService
{
    private const string ResponseSeparator = "\n\n";

    private readonly NamedPipeClient _namedPipeClient;

    public PowerShellService(NamedPipeClient namedPipeClient)
    {
        _namedPipeClient = namedPipeClient;
    }

    public async Task<string> GetCurrentLocationFromPipeAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        var requestParams = new GetCurrentLocationParams();
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.GetCurrentLocationParams);

        var response = await _namedPipeClient.SendRequestToAsync(pipeName, jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException($"PowerShell.MCP module communication to pipe '{pipeName}' failed - no response received");
        }

        return ExtractResponseBody(response);
    }

    public async Task<GetStatusResponse?> GetStatusFromPipeAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        var requestParams = new GetStatusParams();
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.GetStatusParams);

        var response = await _namedPipeClient.SendRequestToAsync(pipeName, jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            return null;
        }

        try
        {
            var jsonHeader = ExtractResponseHeader(response);
            return JsonSerializer.Deserialize(jsonHeader, GetStatusResponseContext.Default.GetStatusResponse);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> ConsumeOutputFromPipeAsync(string pipeName, CancellationToken cancellationToken = default)
    {
        var requestParams = new ConsumeOutputParams();
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.ConsumeOutputParams);

        var response = await _namedPipeClient.SendRequestToAsync(pipeName, jsonRequest);

        return ExtractResponseBody(response);
    }

    public async Task<string> InvokeExpressionToPipeAsync(string pipeName, string pipeline, int timeoutSeconds = 170, CancellationToken cancellationToken = default)
    {
        var requestParams = new InvokeExpressionParams
        {
            Pipeline = pipeline,
            TimeoutSeconds = timeoutSeconds
        };

        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.InvokeExpressionParams);

        var response = await _namedPipeClient.SendRequestToAsync(pipeName, jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException($"PowerShell.MCP module communication to pipe '{pipeName}' failed for command: {pipeline}");
        }

        return response;
    }

    public async Task<ClaimConsoleResponse?> ClaimConsoleAsync(string pipeName, int proxyPid, string agentId, CancellationToken cancellationToken = default)
    {
        var requestParams = new ClaimConsoleParams { ProxyPid = proxyPid, AgentId = agentId };
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.ClaimConsoleParams);

        try
        {
            var response = await _namedPipeClient.SendRequestToAsync(pipeName, jsonRequest);

            if (string.IsNullOrEmpty(response))
            {
                return null;
            }

            return JsonSerializer.Deserialize(response, ClaimConsoleResponseContext.Default.ClaimConsoleResponse);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SetWindowTitleAsync(string pipeName, string title, CancellationToken cancellationToken = default)
    {
        var request = new SetWindowTitleParams { Title = title };
        var jsonRequest = JsonSerializer.Serialize(request, PowerShellJsonRpcContext.Default.SetWindowTitleParams);

        try
        {
            await _namedPipeClient.SendRequestToAsync(pipeName, jsonRequest);
        }
        catch
        {
            // Ignore errors - title setting is best effort
        }
    }

    /// <summary>
    /// Extracts the body part from a response (content after "\n\n" separator)
    /// </summary>
    private static string ExtractResponseBody(string response)
    {
        var separatorIndex = response.IndexOf(ResponseSeparator);
        return separatorIndex >= 0 ? response.Substring(separatorIndex + ResponseSeparator.Length) : response;
    }

    /// <summary>
    /// Extracts the header part from a response (content before "\n\n" separator)
    /// </summary>
    private static string ExtractResponseHeader(string response)
    {
        var separatorIndex = response.IndexOf(ResponseSeparator);
        return separatorIndex >= 0 ? response.Substring(0, separatorIndex) : response;
    }
}