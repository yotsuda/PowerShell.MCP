using System.Diagnostics;

namespace PowerShell.MCP;

public enum McpLogLevel
{
    Info = 1,
    Warning = 2,
    Error = 3
}

public interface IMcpLogger
{
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public class McpLogger : IMcpLogger
{
    public void Info(string message)
    {
        Debug.WriteLine($"[PowerShell.MCP] [INFO] {message}");
    }

    public void Warning(string message)
    {
        Debug.WriteLine($"[PowerShell.MCP] [WARNING] {message}");
    }

    public void Error(string message, Exception? exception = null)
    {
        var logMessage = $"[PowerShell.MCP] [ERROR] {message}";
        if (exception != null)
        {
            logMessage += $" | Exception: {exception.Message}";
        }
        Debug.WriteLine(logMessage);
    }
}
