using PowerShell.MCP.Proxy.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Proxy;

/// <summary>
/// POWERSHELL_MCP_PIPE_NAME lets a development server use its own pipe
/// namespace, so it and the production server never claim each other's
/// consoles. Each test reads DefaultPipeName first, so the static is pinned
/// to the default before the variable is set and other tests are unaffected.
/// </summary>
public class PipeBaseNameTests
{
    private const string Variable = "POWERSHELL_MCP_PIPE_NAME";

    private static string ResolveWith(string? value)
    {
        _ = ConsoleSessionManager.DefaultPipeName;
        var saved = Environment.GetEnvironmentVariable(Variable);
        try
        {
            Environment.SetEnvironmentVariable(Variable, value);
            return ConsoleSessionManager.ResolvePipeBaseName();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, saved);
        }
    }

    [Fact]
    public void Unset_IsPSMCP() => Assert.Equal("PSMCP", ResolveWith(null));

    [Theory]
    [InlineData("PSMCPDEV")]
    [InlineData("psmcp_dev-2")]
    public void ValidName_IsUsed(string name) => Assert.Equal(name, ResolveWith(name));

    [Theory]
    [InlineData("PSMCP.DEV")]   // '.' separates the owner and PID segments
    [InlineData("PS MCP")]
    [InlineData("PSMCP*")]
    public void InvalidName_FallsBackToPSMCP(string name) => Assert.Equal("PSMCP", ResolveWith(name));
}