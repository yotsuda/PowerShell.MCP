using System.Reflection;
using Xunit;
using PowerShell.MCP.Services;

namespace PowerShell.MCP.Tests.Unit.Core;

/// <summary>
/// Tests the check that keeps an unowned console running a superseded build of
/// PowerShell.MCP.dll from being claimed (see ModuleFreshness).
/// </summary>
public class ModuleFreshnessTests
{
    private static readonly Assembly Module = typeof(ModuleFreshness).Assembly;

    [Fact]
    public void ReadMvid_OfTheLoadedFile_MatchesTheLoadedModule()
    {
        Assert.Equal(Module.ManifestModule.ModuleVersionId, ModuleFreshness.ReadMvid(Module.Location));
    }

    [Fact]
    public void IsOutdated_WhileTheLoadedFileIsUnchanged_IsFalse()
    {
        Assert.False(ModuleFreshness.IsOutdated);
    }

    [Fact]
    public void ReadMvid_OfAnotherBuild_Differs()
    {
        // Any other assembly stands in for "a different build at the same path"
        var other = typeof(Xunit.Assert).Assembly.Location;
        Assert.NotEqual(Module.ManifestModule.ModuleVersionId, ModuleFreshness.ReadMvid(other));
    }

    [Fact]
    public void ReadMvid_OfANonPeFile_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a PE file");
            Assert.ThrowsAny<Exception>(() => ModuleFreshness.ReadMvid(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}