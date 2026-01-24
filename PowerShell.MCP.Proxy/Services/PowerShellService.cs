using PowerShell.MCP.Proxy.Models;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

public class PowerShellService : IPowerShellService
{
    private readonly NamedPipeClient _namedPipeClient;

    public PowerShellService(NamedPipeClient namedPipeClient)
    {
        _namedPipeClient = namedPipeClient;
    }

    public async Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        var requestParams = new GetCurrentLocationParams();
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.GetCurrentLocationParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException("PowerShell.MCP module communication failed - no response received");
        }

        // Parse new format: JSON header + "\n\n" + body
        var separatorIndex = response.IndexOf("\n\n");
        return separatorIndex >= 0 ? response.Substring(separatorIndex + 2) : response;
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

        // Parse new format: JSON header + "\n\n" + body
        var separatorIndex = response.IndexOf("\n\n");
        return separatorIndex >= 0 ? response.Substring(separatorIndex + 2) : response;
    }

    public async Task<GetStatusResponse?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var requestParams = new GetStatusParams();
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.GetStatusParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            return null;
        }

        try
        {
            // Parse new format: JSON header + "\n\n" + body (body is empty for get_status)
            var separatorIndex = response.IndexOf("\n\n");
            var jsonHeader = separatorIndex >= 0 ? response.Substring(0, separatorIndex) : response;
            return JsonSerializer.Deserialize(jsonHeader, GetStatusResponseContext.Default.GetStatusResponse);
        }
        catch
        {
            // Failed to parse - return null
            return null;
        }
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
            // Parse new format: JSON header + "\n\n" + body (body is empty for get_status)
            var separatorIndex = response.IndexOf("\n\n");
            var jsonHeader = separatorIndex >= 0 ? response.Substring(0, separatorIndex) : response;
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

        // Parse new format: JSON header + "\n\n" + body
        var separatorIndex = response.IndexOf("\n\n");
        return separatorIndex >= 0 ? response.Substring(separatorIndex + 2) : response;
    }

    public async Task<string> InvokeExpressionAsync(string pipeline, int timeoutSeconds = 170, CancellationToken cancellationToken = default)
    {
        var requestParams = new InvokeExpressionParams
        {
            Pipeline = pipeline,
            TimeoutSeconds = timeoutSeconds
        };

        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.InvokeExpressionParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException($"PowerShell.MCP module communication failed for command: {pipeline}");
        }

        return response;
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

    public async Task<string> StartNewConsoleAsync(CancellationToken cancellationToken = default)
    {
        var requestParams = new StartPowerShellConsoleParams();
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.StartPowerShellConsoleParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException("PowerShell.MCP module communication failed for console start");
        }

        return response;
    }
}
