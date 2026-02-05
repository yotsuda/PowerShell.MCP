namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Builder for constructing response strings
/// </summary>
public interface IResponseBuilder
{
    /// <summary>
    /// Add busy status information
    /// </summary>
    IResponseBuilder AddBusyStatus(string? statusInfo);

    /// <summary>
    /// Add closed console messages
    /// </summary>
    IResponseBuilder AddClosedConsoleMessages(IEnumerable<string>? messages);

    /// <summary>
    /// Add all pipes status information
    /// </summary>
    IResponseBuilder AddAllPipesStatusInfo(string? statusInfo);

    /// <summary>
    /// Add completed output from cached results
    /// </summary>
    IResponseBuilder AddCompletedOutput(string? output);

    /// <summary>
    /// Add scope warning message
    /// </summary>
    IResponseBuilder AddScopeWarning(string? warning);

    /// <summary>
    /// Add a message line
    /// </summary>
    IResponseBuilder AddMessage(string message);

    /// <summary>
    /// Add location result
    /// </summary>
    IResponseBuilder AddLocationResult(string locationResult);

    /// <summary>
    /// Add wait for completion hint
    /// </summary>
    IResponseBuilder AddWaitForCompletionHint();

    /// <summary>
    /// Build the final response string
    /// </summary>
    string Build();

    /// <summary>
    /// Build the final response string with a default message if empty
    /// </summary>
    string BuildOrDefault(string defaultMessage);
}