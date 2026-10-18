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
}
