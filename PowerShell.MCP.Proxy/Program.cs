namespace PowerShell.MCP.Proxy;

internal class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            var server = new McpServer();
            await server.RunAsync();
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }
}
