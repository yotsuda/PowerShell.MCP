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

        return response;
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

        return response;
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
            return JsonSerializer.Deserialize(response, GetStatusResponseContext.Default.GetStatusResponse);
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
            return JsonSerializer.Deserialize(response, GetStatusResponseContext.Default.GetStatusResponse);
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

        return await _namedPipeClient.SendRequestToAsync(pipeName, jsonRequest);
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
