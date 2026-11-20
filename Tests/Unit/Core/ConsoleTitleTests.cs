using PowerShell.MCP.Services;
using Xunit;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// On Linux and macOS the status line's window title used to be fetched by
/// sending the runspace a command; while a command ran or waited at a
/// prompt, that request queued behind it and then took its result. The
/// engine's idle tick now remembers the title, and the status line reads that.
/// </summary>
public class ConsoleTitleTests
{
    [Fact]
    public void Remember_KeepsTheLastNonEmptyTitle()
    {
        ConsoleTitle.Remember("AI from 172.27.224.1");
        ConsoleTitle.Remember(null);
        ConsoleTitle.Remember("");

        Assert.Equal("AI from 172.27.224.1", ConsoleTitle.Known);

        ConsoleTitle.Remember("#1234 ____");
        Assert.Equal("#1234 ____", ConsoleTitle.Known);
    }

    // Get-MCPOwner's ConsoleName: the name in the title the proxy names the
    // console with ("#<pid> <name>").
    [Theory]
    [InlineData("#1234 Lithium", "Lithium")]
    [InlineData("Administrator: #1234 Lithium", "Lithium")]
    [InlineData("#1234 Lithium ", "Lithium")]
    [InlineData("#1234 ____", null)]   // the unowned placeholder
    [InlineData("#1234 ", null)]
    [InlineData("#5678 Lithium", null)] // another console's title
    [InlineData("#12345 Lithium", null)]
    [InlineData("AI from 172.27.224.1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseName_ReadsTheNameOfThisConsolesTitle(string? title, string? expected)
    {
        Assert.Equal(expected, ConsoleTitle.ParseName(title, 1234));
    }

    [Fact]
    public void RememberName_KeepsItUntilForgotten()
    {
        try
        {
            ConsoleTitle.RememberName($"#{Environment.ProcessId} Lithium");
            Assert.Equal("Lithium", ConsoleTitle.Name);

            ConsoleTitle.ForgetName();
            Assert.Null(ConsoleTitle.Name);
        }
        finally
        {
            ConsoleTitle.ForgetName();
        }
    }
}
