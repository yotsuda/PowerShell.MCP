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
        // Use type-safe parameters
        var requestParams = new GetCurrentLocationParams();

        // Serialize without reflection using Source Generator
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

    public async Task<string> InvokeExpressionAsync(string pipeline, bool execute_immediately, CancellationToken cancellationToken = default)
    {
        // Use type-safe parameters
        var requestParams = new InvokeExpressionParams
        {
            Pipeline = pipeline,
            ExecuteImmediately = execute_immediately
        };

        // Serialize without reflection using Source Generator
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.InvokeExpressionParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException($"PowerShell.MCP module communication failed for command: {pipeline}");
        }

        return response;
    }

    public async Task<string> StartNewConsoleAsync(CancellationToken cancellationToken = default)
    {
        // Use type-safe parameters
        var requestParams = new StartPowerShellConsoleParams();

        // Serialize without reflection using Source Generator
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.StartPowerShellConsoleParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);
        
        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException("PowerShell.MCP module communication failed for console start");
        }
        
        return response;
    }
}
