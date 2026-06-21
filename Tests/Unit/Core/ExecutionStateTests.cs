using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// Serializes the test classes that mutate the process-global ExecutionState /
/// PowerShellCommunication statics so they never run in parallel with each other.
/// </summary>
[CollectionDefinition("McpServerState")]
public class McpServerStateCollection { }

/// <summary>
/// Tests ExecutionState's status derivation, output cache, the should-cache flag,
/// and heartbeat/cwd snapshots. State is global static, so each test resets the
/// pieces it touches via the existing public mutators.
/// </summary>
[Collection("McpServerState")]
public class ExecutionStateTests
{
    private static void Reset()
    {
        ExecutionState.CompleteExecution();   // _isBusy = false
        ExecutionState.ConsumeCachedOutputs(); // clear cache
    }

    [Fact]
    public void Status_IsStandby_WhenIdleWithNoCache()
    {
        Reset();
        Assert.Equal("standby", ExecutionState.Status);
    }

    [Fact]
    public void SetBusy_MakesStatusBusy_AndExposesPipeline()
    {
        Reset();
        ExecutionState.SetBusy("Get-Process");
        try
        {
            Assert.Equal("busy", ExecutionState.Status);
            Assert.Equal("Get-Process", ExecutionState.CurrentPipeline);
        }
        finally
        {
            ExecutionState.CompleteExecution();
        }
    }

    [Fact]
    public void Status_IsCompleted_AfterCacheWhileIdle()
    {
        Reset();
        ExecutionState.SetBusy("x");
        ExecutionState.AddToCache("output-1");
        ExecutionState.CompleteExecution();
        try
        {
            Assert.Equal("completed", ExecutionState.Status);
            Assert.True(ExecutionState.HasCachedOutput);
        }
        finally
        {
            ExecutionState.ConsumeCachedOutputs();
        }
    }

    [Fact]
    public void Peek_IsNonDestructive_Consume_Clears()
    {
        Reset();
        ExecutionState.AddToCache("a");
        ExecutionState.AddToCache("b");

        Assert.Equal(new[] { "a", "b" }, ExecutionState.PeekCachedOutputs());
        Assert.True(ExecutionState.HasCachedOutput); // peek did not clear

        Assert.Equal(new[] { "a", "b" }, ExecutionState.ConsumeCachedOutputs());
        Assert.False(ExecutionState.HasCachedOutput); // consume cleared
    }

    [Fact]
    public void MarkForCaching_SetsFlag_AddToCache_ClearsIt()
    {
        Reset();
        ExecutionState.MarkForCaching();
        Assert.True(ExecutionState.ShouldCacheOutput);

        ExecutionState.AddToCache("x");
        Assert.False(ExecutionState.ShouldCacheOutput);

        ExecutionState.ConsumeCachedOutputs();
    }

    [Fact]
    public void SetBusy_ClearsShouldCacheFlag()
    {
        Reset();
        ExecutionState.MarkForCaching();
        ExecutionState.SetBusy("y");
        try
        {
            Assert.False(ExecutionState.ShouldCacheOutput);
        }
        finally
        {
            ExecutionState.CompleteExecution();
        }
    }

    [Fact]
    public void Heartbeat_MakesRunspaceAvailable()
    {
        ExecutionState.Heartbeat();
        Assert.True(ExecutionState.IsRunspaceAvailable);
    }

    [Fact]
    public void SetCurrentAiCwd_RoundTrips()
    {
        ExecutionState.SetCurrentAiCwd(@"C:\Work\Proj");
        Assert.Equal(@"C:\Work\Proj", ExecutionState.CurrentAiCwd);
    }
}
