using PowerShell.MCP.Proxy.Models;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

public class PowerShellService : IPowerShellService
{
    //private readonly ILocationTracker _locationTracker;
    private readonly NamedPipeClient _namedPipeClient;

    //public PowerShellService(ILocationTracker locationTracker, NamedPipeClient namedPipeClient)
    public PowerShellService(NamedPipeClient namedPipeClient)
    {
        //_locationTracker = locationTracker;
        _namedPipeClient = namedPipeClient;
    }

    public async Task<string> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[DEBUG] PowerShellService.GetCurrentLocationAsync - communicating with PowerShell.MCP module");
        
        // 型安全なパラメータ使用
        var requestParams = new GetCurrentLocationParams();

        // Source Generator を使用してリフレクション不要でシリアライズ
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.GetCurrentLocationParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);
        
        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException("PowerShell.MCP module communication failed - no response received");
        }
        
        Console.Error.WriteLine("[DEBUG] Response received from PowerShell.MCP module");
      
        return response;
    }

    public async Task<string> InvokeExpressionAsync(string pipeline, bool execute_immediately, CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine($"[DEBUG] PowerShellService.ExecuteCommandAsync - communicating with PowerShell.MCP module for: {pipeline}");
        
        // 型安全なパラメータ使用
        var requestParams = new InvokeExpressionParams
        {
            Pipeline = pipeline,
            ExecuteImmediately = execute_immediately
        };

        // Source Generator を使用してリフレクション不要でシリアライズ
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.InvokeExpressionParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);

        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException($"PowerShell.MCP module communication failed for command: {pipeline}");
        }
        
        Console.Error.WriteLine("[DEBUG] Response received from PowerShell.MCP module");

        return response;
    }

    public async Task<string> StartNewConsoleAsync(CancellationToken cancellationToken = default)
    {
        Console.Error.WriteLine("[DEBUG] PowerShellService.StartNewConsoleAsync - communicating with PowerShell.MCP module");
        
        // 型安全なパラメータ使用
        var requestParams = new StartPowerShellConsoleParams();

        // Source Generator を使用してリフレクション不要でシリアライズ
        var jsonRequest = JsonSerializer.Serialize(requestParams, PowerShellJsonRpcContext.Default.StartPowerShellConsoleParams);

        var response = await _namedPipeClient.SendRequestAsync(jsonRequest);
        
        if (string.IsNullOrEmpty(response))
        {
            throw new InvalidOperationException("PowerShell.MCP module communication failed for console start");
        }
        
        return response;
    }
}
