using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace PowerShell.MCP.Services;

/// <summary>
/// Tells whether the PowerShell.MCP.dll this console loaded is still the one
/// on disk.
/// </summary>
/// <remarks>
/// <para>
/// A console outlives the build it loaded. Build-AllPlatforms swaps a DLL that
/// a console holds aside and copies the new one into place, so a console from
/// before the deploy keeps running the old code under the same path and the
/// same version number — the version check between proxy and console cannot
/// see the difference. Released when its session ends, such a console is then
/// claimed by the next session's proxy, which silently gets the old behavior.
/// </para>
/// <para>
/// The loaded module's MVID is compared with the MVID in the file now at the
/// path it was loaded from. Builds are deterministic, so a rebuild of the same
/// source keeps the MVID and only a real change counts. The file is re-read
/// only when its timestamp changes; get_status is polled often.
/// </para>
/// </remarks>
internal static class ModuleFreshness
{
    private static readonly object _lock = new();
    private static DateTime _checkedWriteTimeUtc;
    private static bool _outdated;

    /// <summary>
    /// True when the file at the loaded assembly's path holds a different build.
    /// False whenever that cannot be determined (no file, unreadable, mid-copy):
    /// the answer only decides whether a console is claimed, and an unknown
    /// must not take every console out of use.
    /// </summary>
    public static bool IsOutdated
    {
        get
        {
            try
            {
                var assembly = typeof(ModuleFreshness).Assembly;
                var path = assembly.Location;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

                var writeTimeUtc = File.GetLastWriteTimeUtc(path);
                lock (_lock)
                {
                    if (writeTimeUtc != _checkedWriteTimeUtc)
                    {
                        _outdated = ReadMvid(path) is Guid mvid && mvid != assembly.ManifestModule.ModuleVersionId;
                        _checkedWriteTimeUtc = writeTimeUtc;
                    }
                    return _outdated;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    internal static Guid? ReadMvid(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return null;
        var metadata = peReader.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
    }
}