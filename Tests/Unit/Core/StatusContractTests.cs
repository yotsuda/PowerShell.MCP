using PowerShell.MCP;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// Pins the column contract of PowerShell.MCP.Status. The psm1 functions
/// Get-MCPOwner and Restart-MCPServer construct it via
/// <c>[PowerShell.MCP.Status]@{ EngineReady=...; Owned=...; ... }</c>, so a
/// renamed/removed/added property would break those functions at runtime (only
/// caught by the Pester suite). This fast C# guard fails the moment the property
/// set drifts, prompting the psm1 hashtable to be updated in lockstep.
/// </summary>
public class StatusContractTests
{
    [Fact]
    public void Status_HasExactlyTheExpectedColumns()
    {
        var expected = new Dictionary<string, Type>
        {
            ["EngineReady"] = typeof(bool),
            ["Owned"]       = typeof(bool),
            ["ProxyPid"]    = typeof(int?),
            ["AgentId"]     = typeof(string),
            ["ClientName"]  = typeof(string),
            ["LastError"]   = typeof(string),
        };

        var actual = typeof(Status)
            .GetProperties()
            .ToDictionary(p => p.Name, p => p.PropertyType);

        Assert.Equal(
            expected.OrderBy(kv => kv.Key),
            actual.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void Status_PropertiesAreSettable_ForHashtableConstruction()
    {
        // Mirrors the psm1 construction path: default ctor + property setters.
        var s = new Status
        {
            EngineReady = true,
            Owned = true,
            ProxyPid = 1234,
            AgentId = "default",
            ClientName = "Claude Code",
            LastError = null,
        };

        Assert.True(s.EngineReady);
        Assert.True(s.Owned);
        Assert.Equal(1234, s.ProxyPid);
        Assert.Equal("default", s.AgentId);
        Assert.Equal("Claude Code", s.ClientName);
        Assert.Null(s.LastError);
    }
}
