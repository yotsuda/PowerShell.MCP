using System.Text;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// Builder for constructing response strings with consistent formatting
/// </summary>
public class ResponseBuilder : IResponseBuilder
{
    private readonly StringBuilder _sb = new();

    public IResponseBuilder AddBusyStatus(string? statusInfo)
    {
        if (!string.IsNullOrEmpty(statusInfo))
        {
            _sb.Append(statusInfo);
            if (!statusInfo.EndsWith('\n'))
                _sb.AppendLine();
        }
        return this;
    }

    public IResponseBuilder AddClosedConsoleMessages(IEnumerable<string>? messages)
    {
        if (messages == null) return this;

        var list = messages.ToList();
        if (list.Count > 0)
        {
            _sb.AppendLine(string.Join("\n", list));
            _sb.AppendLine();
        }
        return this;
    }

    public IResponseBuilder AddAllPipesStatusInfo(string? statusInfo)
    {
        if (!string.IsNullOrEmpty(statusInfo))
        {
            _sb.AppendLine(statusInfo);
            _sb.AppendLine();
        }
        return this;
    }

    public IResponseBuilder AddCompletedOutput(string? output)
    {
        if (!string.IsNullOrEmpty(output))
        {
            _sb.Append(output);
            if (!output.EndsWith('\n'))
                _sb.AppendLine();
        }
        return this;
    }

    public IResponseBuilder AddScopeWarning(string? warning)
    {
        if (!string.IsNullOrEmpty(warning))
        {
            _sb.AppendLine(warning);
            _sb.AppendLine();
        }
        return this;
    }

    public IResponseBuilder AddMessage(string message)
    {
        _sb.Append(message);
        return this;
    }

    public IResponseBuilder AddLocationResult(string locationResult)
    {
        _sb.Append(locationResult);
        return this;
    }

    public IResponseBuilder AddWaitForCompletionHint()
    {
        _sb.AppendLine();
        _sb.Append("Use wait_for_completion tool to wait and retrieve the result.");
        return this;
    }

    public string Build()
    {
        return _sb.ToString();
    }

    public string BuildOrDefault(string defaultMessage)
    {
        return _sb.Length == 0 ? defaultMessage : _sb.ToString();
    }
}
