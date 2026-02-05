using System.Text.Json;
using System.Text.RegularExpressions;
using PowerShell.MCP.Proxy.Models;
using PowerShell.MCP.Proxy.Helpers;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Service for executing PowerShell commands and parsing results
/// </summary>
public partial class CommandExecutionService : ICommandExecutionService
{
    private readonly IPowerShellService _powerShellService;

    public CommandExecutionService(IPowerShellService powerShellService)
    {
        _powerShellService = powerShellService;
    }

    /// <inheritdoc />
    public async Task<ExecutionResult> ExecuteAsync(
        string pipeName,
        string pipeline,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _powerShellService.InvokeExpressionToPipeAsync(
                pipeName, pipeline, timeoutSeconds, cancellationToken);

            return ParseResponse(result, pipeline);
        }
        catch (Exception ex)
        {
            return new ExecutionResult(
                ExecutionResultType.Error,
                $"Command execution failed: {ex.Message}",
                null, 0, 0, pipeline, null);
        }
    }

    /// <inheritdoc />
    public string? CheckVariableScopeWarning(string pipeline)
    {
        return PipelineHelper.CheckLocalVariableAssignments(pipeline);
    }

    /// <summary>
    /// Parse the response from PowerShell execution
    /// </summary>
    private static ExecutionResult ParseResponse(string result, string pipeline)
    {
        // Parse response: header JSON (first line) + "\n\n" + body
        var separatorIndex = result.IndexOf("\n\n");
        var jsonHeader = separatorIndex >= 0 ? result[..separatorIndex] : result;
        var body = separatorIndex >= 0 ? result[(separatorIndex + 2)..] : "";

        if (!jsonHeader.StartsWith("{"))
        {
            // Not JSON, return as success with raw output
            return new ExecutionResult(
                ExecutionResultType.Success,
                result,
                null, 0, 0, pipeline, null);
        }

        try
        {
            var jsonResponse = JsonSerializer.Deserialize(jsonHeader, GetStatusResponseContext.Default.GetStatusResponse);
            if (jsonResponse == null)
            {
                return new ExecutionResult(
                    ExecutionResultType.Success,
                    result,
                    null, 0, 0, pipeline, null);
            }

            return jsonResponse.Status switch
            {
                "busy" => new ExecutionResult(
                    ExecutionResultType.Busy,
                    body,
                    jsonResponse.StatusLine,
                    jsonResponse.Pid,
                    jsonResponse.Duration ?? 0,
                    jsonResponse.Pipeline,
                    jsonResponse.Reason),

                "timeout" => new ExecutionResult(
                    ExecutionResultType.Timeout,
                    body,
                    jsonResponse.StatusLine,
                    jsonResponse.Pid,
                    jsonResponse.Duration ?? 0,
                    jsonResponse.Pipeline,
                    null),

                "completed" => new ExecutionResult(
                    ExecutionResultType.Completed,
                    body,
                    jsonResponse.StatusLine,
                    jsonResponse.Pid,
                    jsonResponse.Duration ?? 0,
                    jsonResponse.Pipeline,
                    null),

                "success" => new ExecutionResult(
                    ExecutionResultType.Success,
                    body,
                    jsonResponse.StatusLine,
                    jsonResponse.Pid,
                    jsonResponse.Duration ?? 0,
                    jsonResponse.Pipeline,
                    null),

                _ => new ExecutionResult(
                    ExecutionResultType.Success,
                    result,
                    null, 0, 0, pipeline, null)
            };
        }
        catch
        {
            // Not valid JSON or parsing failed, return as-is
            return new ExecutionResult(
                ExecutionResultType.Success,
                result,
                null, 0, 0, pipeline, null);
        }
    }
}