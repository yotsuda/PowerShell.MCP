using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// What a bare Windows machine needs to dial in, handed out by the proxy over
/// plain HTTP on the accept port: a bootstrap script that runs in PowerShell 7
/// or Windows PowerShell 5.1, the PowerShell.MCP module, and PowerShell 7.
/// </summary>
/// <remarks>
/// <para>
/// Integrity runs through the one line the user pastes: it carries the
/// bootstrap's SHA-256, and the bootstrap carries the SHA-256 of everything
/// else, so nothing served over HTTP is used unless it is what the proxy
/// meant. The pairing code is NOT in the bootstrap — anyone on the network
/// can fetch it — but is an argument on that pasted line.
/// </para>
/// <para>
/// Installing is the user's decision, made before the AI calls
/// accept_remote_console and passed as <see cref="InstallScope"/>: nothing
/// (missing pieces are unpacked into a folder that can be deleted: the user's,
/// or an administrators-only one in an elevated console),
/// this user (no admin), or all users (admin). Whatever is already there at
/// the same build is used and never reinstalled.
/// </para>
/// <para>
/// The module comes from PowerShell Gallery at this proxy's own version, as a
/// .nupkg installed with Install-PSResource — the ordinary, manageable way —
/// when the Gallery's DLL is byte-identical to the one this proxy runs with.
/// A development build (or no Gallery access) falls back to a copy of this
/// proxy's module folder: the same version number with different code would
/// pass the version check and then misbehave.
/// </para>
/// </remarks>
public sealed class RemoteBootstrap
{
    public const string PwshPlatform = "win-x64";
    public static readonly string[] InstallScopes = ["none", "user", "machine"];
    private static readonly HttpClient Http = CreateHttp();
    private static readonly SemaphoreSlim ModuleLock = new(1, 1);
    private static ModuleArtifacts? _module;

    public byte[] Script { get; private init; } = [];
    public string ScriptHash { get; private init; } = "";
    public string ModuleVersion { get; private init; } = "";
    public string InstallScope { get; private init; } = "none";
    /// <summary>True when the module goes out as the Gallery package (Install-PSResource), false for a folder copy.</summary>
    public bool ModuleFromGallery { get; private init; }
    public string? PwshVersion { get; private init; }
    private ModuleArtifacts _moduleArtifacts = null!;
    private Task<string?>? _pwshZip;
    private Task<string?>? _pwshMsi;

    private sealed record ModuleArtifacts(
        string Version, string DllHash,
        byte[] FolderZip, string FolderZipHash,
        string? NupkgPath, string? NupkgHash);

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerShell.MCP-proxy");
        return client;
    }

    /// <summary>
    /// The module folder this proxy belongs to: the one named by
    /// POWERSHELL_MCP_MODULE (development), else two levels above the proxy
    /// exe (Modules\PowerShell.MCP\bin\win-x64\). Null when neither holds a psd1.
    /// </summary>
    public static string? ModuleDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("POWERSHELL_MCP_MODULE");
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
            return Path.GetDirectoryName(Path.GetFullPath(configured));
        var guess = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
        return File.Exists(Path.Combine(guess, "PowerShell.MCP.psd1")) ? guess : null;
    }

    /// <summary>
    /// Builds what one accept hands out for the given install scope. Null
    /// when this proxy cannot find its module folder (then only the "already
    /// installed" line is offered). PowerShell downloads may still be running
    /// when this returns; a request for them waits.
    /// </summary>
    public static async Task<RemoteBootstrap?> PrepareAsync(string installScope, CancellationToken ct)
    {
        if (!InstallScopes.Contains(installScope))
            throw new ArgumentException($"install must be one of: {string.Join(", ", InstallScopes)}.");
        var module = await GetModuleAsync(ct);
        if (module == null) return null;

        var release = await LatestReleaseAsync(ct);
        var (pwshVersion, zipHash, zip) = await ResolveAssetAsync(release, "zip", ct);
        string? msiHash = null;
        Task<string?> msi = Task.FromResult<string?>(null);
        if (installScope == "machine")
        {
            string? msiVersion;
            (msiVersion, msiHash, msi) = await ResolveAssetAsync(release, "msi", ct);
            if (msiVersion != pwshVersion) { msiHash = null; msi = Task.FromResult<string?>(null); }
        }

        var fromGallery = installScope != "none" && module.NupkgHash != null;
        var script = BootstrapTemplate
            .Replace("__INSTALL__", installScope)
            .Replace("__MODULE_VERSION__", module.Version)
            .Replace("__MODULE_DLL_HASH__", module.DllHash)
            .Replace("__MODULE_ZIP_HASH__", module.FolderZipHash)
            .Replace("__MODULE_NUPKG_HASH__", fromGallery ? module.NupkgHash! : "")
            .Replace("__PWSH_VERSION__", pwshVersion ?? "")
            .Replace("__PWSH_ZIP_HASH__", zipHash ?? "")
            .Replace("__PWSH_MSI_HASH__", msiHash ?? "")
            .Replace("\r\n", "\n").Replace("\n", "\r\n");
        var bytes = Encoding.ASCII.GetBytes(script);
        return new RemoteBootstrap
        {
            Script = bytes,
            ScriptHash = Convert.ToHexString(SHA256.HashData(bytes)),
            ModuleVersion = module.Version,
            InstallScope = installScope,
            ModuleFromGallery = fromGallery,
            PwshVersion = pwshVersion,
            _moduleArtifacts = module,
            _pwshZip = zip,
            _pwshMsi = msi,
        };
    }

    /// <summary>
    /// The line the user pastes on the remote machine, into PowerShell 7 or
    /// Windows PowerShell alike. The script is hashed and run from the same
    /// bytes in memory — never a file another process could swap between the
    /// check and the run — as a script block (not a script file, so the
    /// execution policy does not apply), which lets PowerShell 7 start remote
    /// control in the very window it was pasted into.
    /// </summary>
    public string OneLiner(string host, int port, string code) =>
        // One try around it all: when the server cannot be reached, the line
        // says so once instead of going on to hash nothing.
        $"$u='http://{host}:{port}';try{{$b=(iwr \"$u/bootstrap.ps1\" -UseBasicParsing -ErrorAction Stop).RawContentStream.ToArray()}}catch{{$b=$null;\"Could not fetch the bootstrap from ${{u}}: $($_.Exception.Message)\"}};" +
        $"if($b){{if(([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($b))-replace'-')-eq'{ScriptHash}'){{& ([scriptblock]::Create([Text.Encoding]::ASCII.GetString($b))) $u {code}}}else{{'bootstrap.ps1 did not match its hash; not run.'}}}}";

    /// <summary>What this bootstrap will do with the install choice, for the AI to tell the user.</summary>
    public string DescribeInstall()
    {
        var pwsh = PwshVersion != null ? $"PowerShell {PwshVersion}" : "PowerShell 7";
        var module = $"PowerShell.MCP {ModuleVersion}";
        var source = ModuleFromGallery
            ? "from PowerShell Gallery (the same package as on this machine), installed with Install-PSResource"
            : "copied from this machine (it runs a build that is not the PowerShell Gallery package, or the Gallery was unreachable)";
        return InstallScope switch
        {
            "user" => $"install = user (no admin needed; refused in a console run as Administrator, which must not run programs from folders non-admin processes can change): if missing, {pwsh} goes to %LOCALAPPDATA%\\Programs\\PowerShell\\7 and onto the user's PATH, and {module} into the user's PowerShell modules folder, {source}. The script lists what it will install and asks before doing it.",
            "machine" => $"install = machine (PowerShell must be opened as Administrator): if missing, {pwsh} is installed with its MSI into Program Files (on PATH, updated by Microsoft Update), and {module} into Program Files\\PowerShell\\Modules for all users, {source}. The script lists what it will install and asks before doing it.",
            _ => $"install = none: nothing is installed. Missing pieces ({pwsh}, {module}) are unpacked into %LOCALAPPDATA%\\PowerShell.MCP-remote (in a console run as Administrator: %ProgramData%\\PowerShell.MCP-remote, which only administrators can change), reused next time, and removed by deleting that folder.",
        };
    }

    // ----- module: the Gallery package when it is this exact build, else a folder copy -----

    private static async Task<ModuleArtifacts?> GetModuleAsync(CancellationToken ct)
    {
        await ModuleLock.WaitAsync(ct);
        try
        {
            if (_module != null) return _module;
            var dir = ModuleDirectory();
            if (dir == null) return null;
            var psd1 = await File.ReadAllTextAsync(Path.Combine(dir, "PowerShell.MCP.psd1"), ct);
            var match = System.Text.RegularExpressions.Regex.Match(psd1, @"ModuleVersion\s*=\s*'([^']+)'");
            var version = match.Success ? match.Groups[1].Value : Program.ProxyVersion;
            // The DLL's hash identifies the build: what is installed on the
            // remote side, or offered by the Gallery, is used only when it is
            // this exact build, not merely the same version number.
            var dllHash = await HashFileAsync(Path.Combine(dir, "PowerShell.MCP.dll"), ct);

            using var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(dir, file);
                    var name = Path.GetFileName(file);
                    // The proxy binaries are not needed on the remote side, and a
                    // build leaves swapped-out copies that must not travel. The
                    // zip is served to anyone who reaches the port: only the
                    // kinds of file a module consists of, never whatever else
                    // sits in a development folder.
                    if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains(".stash-") || name.Contains(".new-") || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    var extension = Path.GetExtension(name).ToLowerInvariant();
                    var licenseText = extension.Length == 0 && relative.StartsWith("licenses" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    if (!licenseText && extension is not (".dll" or ".psd1" or ".psm1" or ".ps1" or ".ps1xml" or ".xml" or ".md" or ".txt")) continue;
                    var entry = archive.CreateEntry(relative.Replace('\\', '/'), CompressionLevel.Optimal);
                    await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    await using var output = entry.Open();
                    await input.CopyToAsync(output, ct);
                }
            }
            var zip = buffer.ToArray();
            var (nupkgPath, nupkgHash) = await GalleryPackageAsync(version, dllHash, ct);
            _module = new ModuleArtifacts(version, dllHash, zip, Convert.ToHexString(SHA256.HashData(zip)), nupkgPath, nupkgHash);
            return _module;
        }
        finally
        {
            ModuleLock.Release();
        }
    }

    /// <summary>
    /// The Gallery's PowerShell.MCP package at this version, cached, when its
    /// DLL is this proxy's DLL byte for byte; nulls otherwise.
    /// </summary>
    private static async Task<(string? path, string? hash)> GalleryPackageAsync(string version, string dllHash, CancellationToken ct)
    {
        var cached = Path.Combine(CacheDirectory, $"PowerShell.MCP.{version}.nupkg");
        try
        {
            if (!File.Exists(cached))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                Directory.CreateDirectory(CacheDirectory);
                var temp = cached + ".partial-" + Environment.ProcessId;
                using (var response = await Http.GetAsync($"https://www.powershellgallery.com/api/v2/package/PowerShell.MCP/{version}", timeout.Token))
                {
                    if (!response.IsSuccessStatusCode) return (null, null); // not published (a development version)
                    await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(output, timeout.Token);
                }
                File.Move(temp, cached, overwrite: true);
            }
            using (var archive = ZipFile.OpenRead(cached))
            {
                var dll = archive.GetEntry("PowerShell.MCP.dll");
                if (dll == null) return (null, null);
                await using var stream = dll.Open();
                if (Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)) != dllHash) return (null, null);
            }
            return (cached, await HashFileAsync(cached, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[WARN] PowerShell Gallery package not used: {ex.Message}");
            return (null, null);
        }
    }

    // ----- PowerShell 7: latest stable from GitHub, cached under LocalAppData -----

    private static string CacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerShell.MCP", "cache");

    private static string AssetName(string version, string kind) => $"PowerShell-{version}-{PwshPlatform}.{kind}";

    private sealed record Release(string Version, Dictionary<string, (string url, string? digest)> Assets);

    private static async Task<Release?> LatestReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await Http.GetAsync("https://api.github.com/repos/PowerShell/PowerShell/releases/latest", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            // Both go into the bootstrap script and into cache file names: only
            // a plain version and a hex digest, nothing that could break out.
            var version = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            if (version == null || !System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) return null;
            var assets = new Dictionary<string, (string url, string? digest)>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (name == null || url == null) continue;
                string? digest = null;
                if (asset.TryGetProperty("digest", out var d) && d.GetString() is { } s && s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    digest = s["sha256:".Length..].ToUpperInvariant();
                if (digest != null && !System.Text.RegularExpressions.Regex.IsMatch(digest, "^[0-9A-F]{64}$")) digest = null;
                assets[name] = (url, digest);
            }
            return new Release(version, assets);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[WARN] Could not ask GitHub for the latest PowerShell: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The version and SHA-256 to promise in the bootstrap for one asset (zip
    /// or msi), and a task for the file to serve. The GitHub digest gives the
    /// hash before any download; offline, the newest cached file is used;
    /// nulls when there is neither.
    /// </summary>
    private static async Task<(string? version, string? hash, Task<string?> file)> ResolveAssetAsync(Release? release, string kind, CancellationToken ct)
    {
        if (release != null && release.Assets.TryGetValue(AssetName(release.Version, kind), out var asset))
        {
            var cached = Path.Combine(CacheDirectory, AssetName(release.Version, kind));
            if (File.Exists(cached))
            {
                var hash = await HashFileAsync(cached, ct);
                if (asset.digest == null || hash == asset.digest) return (release.Version, hash, Task.FromResult<string?>(cached));
                File.Delete(cached); // a damaged or stale download
            }
            var download = DownloadAsync(asset.url, cached, asset.digest);
            if (asset.digest != null) return (release.Version, asset.digest, download);
            // No digest from GitHub: the hash is known only once the file is here.
            var path = await download;
            return path == null ? (null, null, Task.FromResult<string?>(null)) : (release.Version, await HashFileAsync(path, ct), Task.FromResult<string?>(path));
        }

        if (Directory.Exists(CacheDirectory))
        {
            var suffix = $"-{PwshPlatform}.{kind}";
            var newest = Directory.EnumerateFiles(CacheDirectory, $"PowerShell-*{suffix}")
                .Select(p => (path: p, v: Version.TryParse(Path.GetFileName(p)["PowerShell-".Length..^suffix.Length], out var v) ? v : null))
                .Where(x => x.v != null)
                .OrderByDescending(x => x.v)
                .FirstOrDefault();
            if (newest.path != null)
                return (newest.v!.ToString(), await HashFileAsync(newest.path, ct), Task.FromResult<string?>(newest.path));
        }
        return (null, null, Task.FromResult<string?>(null));
    }

    private static async Task<string?> DownloadAsync(string url, string destination, string? expectedHash)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temp = destination + ".partial-" + Environment.ProcessId;
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output);
            }
            if (expectedHash != null && await HashFileAsync(temp, CancellationToken.None) != expectedHash)
            {
                File.Delete(temp);
                Console.Error.WriteLine($"[WARN] {url} did not match the digest GitHub published; discarded.");
                return null;
            }
            File.Move(temp, destination, overwrite: true);
            return destination;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Could not download {url}: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    // ----- HTTP: GET/HEAD only, a fixed set of paths, one response per connection -----

    /// <summary>
    /// Serves one request on a connection the accept loop identified as HTTP.
    /// Never throws; a malformed request is closed.
    /// </summary>
    public async Task ServeHttpAsync(Stream stream, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var head = await ReadHeadAsync(stream, timeout.Token);
            var requestLine = head?.Split("\r\n", 2)[0].Split(' ');
            if (requestLine is not { Length: 3 } || requestLine[0] is not ("GET" or "HEAD"))
            {
                await WriteStatusAsync(stream, "400 Bad Request", ct);
                return;
            }
            var path = requestLine[1].Split('?')[0];
            var headOnly = requestLine[0] == "HEAD";
            switch (path)
            {
                case "/bootstrap.ps1":
                    await WriteBodyAsync(stream, Script, "text/plain; charset=us-ascii", headOnly, ct);
                    break;
                case "/module.zip":
                    await WriteBodyAsync(stream, _moduleArtifacts.FolderZip, "application/zip", headOnly, ct);
                    break;
                case "/module.nupkg":
                    await WriteFileAsync(stream, ModuleFromGallery ? _moduleArtifacts.NupkgPath : null, headOnly, ct);
                    break;
                case "/pwsh.zip":
                    await WriteFileAsync(stream, _pwshZip == null ? null : await _pwshZip.WaitAsync(TimeSpan.FromMinutes(10), ct), headOnly, ct);
                    break;
                case "/pwsh.msi":
                    await WriteFileAsync(stream, _pwshMsi == null ? null : await _pwshMsi.WaitAsync(TimeSpan.FromMinutes(10), ct), headOnly, ct);
                    break;
                default:
                    await WriteStatusAsync(stream, "404 Not Found", ct);
                    break;
            }
            await stream.FlushAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Bootstrap HTTP request failed: {ex.Message}");
        }
    }

    private static async Task<string?> ReadHeadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (n == 0) return null;
            length += n;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            if (text.Contains("\r\n\r\n")) return text;
        }
        return null;
    }

    private static async Task WriteStatusAsync(Stream stream, string status, CancellationToken ct)
    {
        var body = Encoding.ASCII.GetBytes(status + "\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), ct);
        await stream.WriteAsync(body, ct);
    }

    private static async Task WriteBodyAsync(Stream stream, byte[] body, string contentType, bool headOnly, CancellationToken ct)
    {
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), ct);
        if (!headOnly) await stream.WriteAsync(body, ct);
    }

    private static async Task WriteFileAsync(Stream stream, string? file, bool headOnly, CancellationToken ct)
    {
        if (file == null || !File.Exists(file)) { await WriteStatusAsync(stream, "404 Not Found", ct); return; }
        await using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: {input.Length}\r\nConnection: close\r\n\r\n"), ct);
        if (!headOnly) await input.CopyToAsync(stream, ct);
    }

    // ----- the bootstrap: ASCII only, runs in Windows PowerShell 5.1 or PowerShell 7 -----

    internal const string BootstrapTemplate = """
        # PowerShell.MCP remote console bootstrap, handed out by accept_remote_console.
        # Runs in PowerShell 7 or Windows PowerShell 5.1, as a script block in the
        # session it was pasted into. Uses PowerShell 7.4+ and PowerShell.MCP where
        # they are already installed (the same build); otherwise fetches them from the
        # MCP server that served this file, checks them against the hashes below, and
        # installs or unpacks them as the user chose ($Install). Then, in PowerShell
        # 7.4+, remote control starts in this very window (elevated if it is); in
        # Windows PowerShell, or when another PowerShell.MCP build is already loaded
        # here, a new PowerShell 7 window opens and dials the MCP server.
        #   none    : nothing installed; unpacked under %LOCALAPPDATA%\PowerShell.MCP-remote
        #             (as Administrator: %ProgramData%\PowerShell.MCP-remote, admins only)
        #   user    : PowerShell 7 in %LOCALAPPDATA%\Programs\PowerShell\7 (+ user PATH),
        #             PowerShell.MCP for this user (refused as Administrator)
        #   machine : PowerShell 7 by its MSI, PowerShell.MCP for all users (needs admin)
        # Anything to install is listed and confirmed first. In a console run as
        # Administrator this script takes nothing from, checks nothing in, and
        # unpacks nothing to a folder that non-admin processes of this user can
        # change: such a process could swap a file between its check and its use,
        # and so become administrator. (The console itself, which ran the user's
        # profile, is beyond its reach.)
        param(
            [Parameter(Mandatory = $true)][string]$Base,
            [Parameter(Mandatory = $true)][string]$Code,
            [switch]$Portable
        )
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $Install = '__INSTALL__'
        $ModuleVersion = '__MODULE_VERSION__'
        $ModuleDllHash = '__MODULE_DLL_HASH__'
        $ModuleZipHash = '__MODULE_ZIP_HASH__'
        $ModuleNupkgHash = '__MODULE_NUPKG_HASH__'
        $PwshVersion = '__PWSH_VERSION__'
        $PwshZipHash = '__PWSH_ZIP_HASH__'
        $PwshMsiHash = '__PWSH_MSI_HASH__'

        $Base = $Base.TrimEnd('/')
        $elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $documents = [Environment]::GetFolderPath('MyDocuments')
        $userPwshDir = Join-Path $env:LOCALAPPDATA 'Programs\PowerShell\7'
        # Known folders from the system, not from environment variables: a
        # non-admin process can set per-user ProgramFiles / ProgramData values
        # that an elevated console inherits.
        $programFiles = [Environment]::GetFolderPath('ProgramFiles')
        $programData = [Environment]::GetFolderPath('CommonApplicationData')
        $machinePwsh = Join-Path $programFiles 'PowerShell\7\pwsh.exe'

        # Everything this prints goes through Say: a blank line before the first
        # line and after the last, like the module's own remote control notices.
        # (A hashtable, since this runs as a script block: no script scope.)
        $said = @{ Any = $false }
        function Say([string]$Text, [string]$Color) {
            if (-not $said.Any) { Write-Host ''; $said.Any = $true }
            if ($Color) { Write-Host $Text -ForegroundColor $Color } else { Write-Host $Text }
        }

        $admins = New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'
        $system = New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'

        # Where downloads are verified and unpacked. Not elevated: the user's
        # own folder (whoever can change it already runs as this user). Elevated:
        # a folder under ProgramData that only Administrators and SYSTEM can
        # change, created with that ACL in one step; an existing one that anyone
        # else could change, or a link, is refused rather than trusted.
        function Get-Root {
            if (-not $elevated) {
                $dir = Join-Path $env:LOCALAPPDATA 'PowerShell.MCP-remote'
                New-Item -ItemType Directory -Force $dir | Out-Null
                return $dir
            }
            $dir = Join-Path $programData 'PowerShell.MCP-remote'
            if (-not (Test-Path -LiteralPath $dir)) {
                $acl = New-Object Security.AccessControl.DirectorySecurity
                $acl.SetAccessRuleProtection($true, $false)
                foreach ($sid in $admins, $system) {
                    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')))
                }
                $acl.SetOwner($admins)
                if ($PSVersionTable.PSVersion.Major -ge 6) { [void][IO.FileSystemAclExtensions]::CreateDirectory($acl, $dir) }
                else { [void][IO.Directory]::CreateDirectory($dir, $acl) }
            }
            $item = Get-Item -LiteralPath $dir -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "$dir is a link, not a folder. Delete it and run this again." }
            $current = Get-Acl -LiteralPath $dir
            $owner = $current.GetOwner([Security.Principal.SecurityIdentifier])
            # Any right that lets someone change what is inside: the specific
            # write/delete/ownership rights, and the generic ones (GENERIC_ALL,
            # GENERIC_WRITE, MAXIMUM_ALLOWED) an inherit-only entry may carry.
            $writes = [int][Security.AccessControl.FileSystemRights]'Write, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership' -bor 0x10000000 -bor 0x40000000 -bor 0x02000000
            $others = @($current.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | Where-Object {
                $_.AccessControlType -eq 'Allow' -and (([int]$_.FileSystemRights) -band $writes) -and $_.IdentityReference -ne $admins -and $_.IdentityReference -ne $system })
            if (($owner -ne $admins -and $owner -ne $system) -or $others.Count -gt 0) {
                throw "$dir can be changed by accounts other than Administrators, so its contents cannot be trusted in this administrator console. Delete it and run this again."
            }
            return $dir
        }

        function Get-Sha256([byte[]]$Bytes) {
            $sha = [Security.Cryptography.SHA256]::Create()
            try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '') } finally { $sha.Dispose() }
        }

        # Downloaded into memory and checked there; only verified bytes reach
        # the disk, in $root, under a name nobody could have prepared.
        function Get-Verified([string]$Name, [string]$Hash, [string]$What) {
            Say "Fetching $What from the MCP server ..."
            $bytes = (Invoke-WebRequest "$Base/$Name" -UseBasicParsing).RawContentStream.ToArray()
            if ((Get-Sha256 $bytes) -ne $Hash) { throw "$Name did not match the hash the MCP server promised; not used." }
            $file = Join-Path $root ([Guid]::NewGuid().ToString('N') + '-' + $Name)
            [IO.File]::WriteAllBytes($file, $bytes)
            return $file
        }

        # Unpacked beside its destination and moved into place only when
        # complete, so an interrupted run never leaves a half folder that a
        # later run would take for an installation.
        function Expand-Verified([string]$Name, [string]$Hash, [string]$Dir, [string]$What) {
            $file = Get-Verified $Name $Hash $What
            $zip = [IO.Path]::ChangeExtension($file, '.zip')
            Move-Item -LiteralPath $file -Destination $zip -Force
            $staging = $Dir + '.partial-' + [Guid]::NewGuid().ToString('N')
            try {
                # ZipFile rather than Expand-Archive: no cmdlet autoloaded from
                # a module folder the user (or something running as the user)
                # can change.
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                [IO.Compression.ZipFile]::ExtractToDirectory($zip, $staging)
                if (Test-Path -LiteralPath $Dir) { Remove-Item -LiteralPath $Dir -Recurse -Force }
                Move-Item -LiteralPath $staging -Destination $Dir
            }
            finally {
                Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
                if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
            }
        }

        # Signed by Microsoft: a valid signature, Microsoft's code-signing
        # subject, and a chain that ends at Microsoft Root Certificate
        # Authority 2011 (a subject alone is only a name, which an enterprise
        # or user-added root could issue too).
        function Test-MicrosoftSigned([string]$Path) {
            $signature = Get-AuthenticodeSignature -LiteralPath $Path
            if ($signature.Status -ne 'Valid' -or
                $signature.SignerCertificate.Subject -ne 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US') { return $false }
            $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
            try {
                [void]$chain.Build($signature.SignerCertificate)
                $top = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
                return $top.Thumbprint -eq '8F43288AD272F3103B6FB1428485EA3014C0BCFE'
            }
            finally { $chain.Dispose() }
        }

        # PowerShell.MCP of exactly this build already installed for PowerShell 7
        # (all users, this user, or unpacked by an earlier run). Another build is
        # left alone. As Administrator, not the user's own folders: the DLL hash
        # would match while the .psm1 beside it could have been changed.
        function Find-Module {
            $candidates = @(
                (Join-Path $programFiles "PowerShell\Modules\PowerShell.MCP\$ModuleVersion\PowerShell.MCP.psd1"),
                (Join-Path $programFiles 'PowerShell\7\Modules\PowerShell.MCP\PowerShell.MCP.psd1'),
                (Join-Path $programFiles 'PowerShell\Modules\PowerShell.MCP\PowerShell.MCP.psd1')
            )
            if (-not $elevated) {
                $candidates += (Join-Path $documents "PowerShell\Modules\PowerShell.MCP\$ModuleVersion\PowerShell.MCP.psd1"),
                               (Join-Path $documents 'PowerShell\Modules\PowerShell.MCP\PowerShell.MCP.psd1')
            }
            # A copy an earlier run unpacked counts only when nothing is to be
            # installed: with install=user/machine the user asked for an
            # installation, which that private copy is not.
            if ($Install -eq 'none') { $candidates += (Join-Path $root "PowerShell.MCP-$ModuleVersion\PowerShell.MCP.psd1") }
            foreach ($candidate in $candidates) {
                if (-not (Test-Path -LiteralPath $candidate)) { continue }
                try {
                    $dll = Join-Path (Split-Path $candidate) 'PowerShell.MCP.dll'
                    if ((Import-PowerShellDataFile -LiteralPath $candidate).ModuleVersion -eq $ModuleVersion -and
                        (Test-Path -LiteralPath $dll) -and (Get-Sha256 ([IO.File]::ReadAllBytes($dll))) -eq $ModuleDllHash) { return $candidate }
                } catch { }
            }
            return $null
        }

        # Other PowerShell.MCP versions installed here: left exactly as they are.
        # This window always imports this build by its full path.
        function Get-OtherModuleVersions([string]$ModulesRoot) {
            $dir = Join-Path $ModulesRoot 'PowerShell.MCP'
            $found = @()
            if (-not (Test-Path -LiteralPath $dir)) { return $found }
            foreach ($psd in @(Get-ChildItem -LiteralPath $dir -Filter PowerShell.MCP.psd1 -Recurse -Depth 1 -ErrorAction SilentlyContinue)) {
                try { $v = (Import-PowerShellDataFile -LiteralPath $psd.FullName).ModuleVersion; if ($v -ne $ModuleVersion) { $found += $v } } catch { }
            }
            return $found
        }

        # A usable PowerShell: 7.4 or later and signed by Microsoft. One odd
        # candidate (a Store alias, a shim) is skipped, not fatal.
        function Test-Pwsh([string]$Path) {
            try {
                if (-not (Test-Path -LiteralPath $Path)) { return $false }
                if ([version](Get-Item -LiteralPath $Path).VersionInfo.FileVersion -lt [version]'7.4') { return $false }
                return (Test-MicrosoftSigned $Path)
            }
            catch { return $false }
        }

        # PowerShell 7.4+: on PATH, installed for all users, installed for this
        # user, or unpacked by an earlier run. As Administrator, not from the
        # user's PATH or folders, which a non-admin process could have changed.
        function Find-Pwsh {
            if (-not $Portable) {
                $candidates = @()
                if (-not $elevated) {
                    $onPath = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($onPath) { $candidates += $onPath.Source }
                    $candidates += (Join-Path $userPwshDir 'pwsh.exe')
                }
                $candidates += $machinePwsh
                foreach ($candidate in $candidates) {
                    if (Test-Pwsh $candidate) { return $candidate }
                }
            }
            $unpacked = Join-Path $root "pwsh-$PwshVersion\pwsh.exe"
            if ($Install -eq 'none' -and $PwshVersion -and (Test-Pwsh $unpacked)) { return $unpacked }
            return $null
        }

        try {
            if (-not [Environment]::Is64BitProcess) {
                throw 'This is 32-bit PowerShell; paste the line into the 64-bit Windows PowerShell (or PowerShell 7) instead.'
            }
            if ($Install -eq 'user' -and $elevated) {
                throw 'install=user would put programs this administrator console runs into folders that non-admin processes can change. Ask the AI for install=none or install=machine, or paste the line into a PowerShell that is not run as Administrator.'
            }
            $root = Get-Root

            # In PowerShell 7.4+ remote control starts in this window, so a
            # console the user opened as Administrator is the one the AI drives.
            # Not when another PowerShell.MCP build is already loaded here
            # (Remove-Module does not unload a DLL, and a process cannot load a
            # second build of it): then a new window, as from Windows PowerShell.
            $here = (-not $Portable) -and ($PSVersionTable.PSVersion.Major * 100 + $PSVersionTable.PSVersion.Minor -ge 704)
            $loadedPsd1 = $null
            if ($here) {
                $loaded = [AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'PowerShell.MCP' } | Select-Object -First 1
                if ($loaded) {
                    $loadedHash = try { Get-Sha256 ([IO.File]::ReadAllBytes($loaded.Location)) } catch { '' }
                    if ($loadedHash -eq $ModuleDllHash) { $loadedPsd1 = Join-Path (Split-Path $loaded.Location) 'PowerShell.MCP.psd1' }
                    else {
                        Say "Another PowerShell.MCP build is already loaded in this window (from $(Split-Path $loaded.Location)), and a process cannot load two; remote control runs in a new window instead."
                        $here = $false
                    }
                }
            }

            $psd1 = if ($loadedPsd1) { $loadedPsd1 } else { Find-Module }
            # Loaded here from a private copy, yet asked to install: install
            # (this session keeps the copy it has loaded).
            if ($psd1 -and $Install -ne 'none' -and $psd1.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { $psd1 = $null }
            $pwsh = if ($here) { (Get-Process -Id $PID).Path } else { Find-Pwsh }
            if ($Install -eq 'machine' -and -not $elevated -and (-not $psd1 -or -not $pwsh)) {
                throw 'Installing for all users needs PowerShell opened as Administrator. Open it that way and paste the same line again.'
            }
            if ($Install -eq 'machine' -and -not $pwsh -and -not $PwshMsiHash) {
                throw 'PowerShell 7 is missing here, and the MCP server has no PowerShell installer to hand out, so it cannot be installed for all users. Install PowerShell 7, or ask the AI for install=none.'
            }
            if ($Install -eq 'user' -and -not $pwsh -and (Test-Path -LiteralPath $userPwshDir)) {
                throw "$userPwshDir holds a PowerShell older than 7.4 (or one not signed by Microsoft); it is left as is. Update it, or ask the AI for install=none."
            }

            # Installing is the user's choice, made with the AI; this is where
            # they see what it means here, and can still say no.
            if ($Install -ne 'none' -and (-not $pwsh -or -not $psd1)) {
                Say 'This installs, for later use:'
                if (-not $pwsh) {
                    Say ("  PowerShell $PwshVersion " + $(if ($Install -eq 'machine') { 'for all users (its MSI, into Program Files)' } else { "for this user (into $userPwshDir, added to your PATH)" }))
                }
                if (-not $psd1) {
                    Say ("  PowerShell.MCP $ModuleVersion " + $(if ($Install -eq 'machine') { 'for all users (Program Files\PowerShell\Modules)' } else { "for this user ($documents\PowerShell\Modules)" }))
                }
                $answer = Read-Host 'Continue? [y/N]'
                if ($answer -notmatch '^\s*(y|yes)\s*$') { throw 'Stopped at your request; nothing was installed.' }
            }

            # PowerShell 7
            if (-not $pwsh) {
                if ($Install -eq 'machine') {
                    $msi = Get-Verified 'pwsh.msi' $PwshMsiHash "PowerShell $PwshVersion installer"
                    $msiPath = [IO.Path]::ChangeExtension($msi, '.msi')
                    Move-Item -LiteralPath $msi -Destination $msiPath -Force
                    if (-not (Test-MicrosoftSigned $msiPath)) { Remove-Item -LiteralPath $msiPath -Force; throw 'The PowerShell installer is not signed by Microsoft; not run.' }
                    Say "Installing PowerShell $PwshVersion for all users ..."
                    # msiexec from the system folder, not whatever PATH finds first.
                    $msiexec = Join-Path ([Environment]::SystemDirectory) 'msiexec.exe'
                    $p = Start-Process $msiexec -ArgumentList '/i', ('"' + $msiPath + '"'), '/qn', 'ADD_PATH=1', 'USE_MU=1', 'ENABLE_MU=1', 'REGISTER_MANIFEST=1' -Wait -PassThru
                    Remove-Item -LiteralPath $msiPath -Force
                    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw "The PowerShell installer failed (exit code $($p.ExitCode))." }
                    $pwsh = $machinePwsh
                    if (-not (Test-Pwsh $pwsh)) { throw "After the installer, $pwsh is not a PowerShell 7.4+ signed by Microsoft; not run." }
                }
                elseif ($PwshZipHash) {
                    if ($Install -eq 'user') {
                        Expand-Verified 'pwsh.zip' $PwshZipHash $userPwshDir "PowerShell $PwshVersion (about 100 MB)"
                        # The user PATH as stored (entries like %USERPROFILE%\... stay
                        # unexpanded), or nothing at all on a fresh account.
                        $environmentKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
                        try {
                            $userPath = [string]$environmentKey.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                            if (($userPath -split ';') -notcontains $userPwshDir) {
                                $environmentKey.SetValue('Path', ($userPath.TrimEnd(';') + ';' + $userPwshDir).TrimStart(';'), [Microsoft.Win32.RegistryValueKind]::ExpandString)
                                # Setting any user variable broadcasts the change, so new
                                # windows see the new PATH; this one removes a value that
                                # does not exist.
                                [Environment]::SetEnvironmentVariable('PSMCP_PATH_REFRESH', $null, 'User')
                            }
                        }
                        finally { $environmentKey.Dispose() }
                        Say "Installed PowerShell $PwshVersion for this user in $userPwshDir (added to your PATH)."
                        $pwsh = Join-Path $userPwshDir 'pwsh.exe'
                    }
                    else {
                        $dir = Join-Path $root "pwsh-$PwshVersion"
                        Expand-Verified 'pwsh.zip' $PwshZipHash $dir "PowerShell $PwshVersion (portable, about 100 MB)"
                        $pwsh = Join-Path $dir 'pwsh.exe'
                    }
                    if (-not (Test-Pwsh $pwsh)) { throw "$pwsh is not a PowerShell 7.4+ signed by Microsoft; not run." }
                }
                else {
                    throw 'PowerShell 7.4 or later is not installed here, and the MCP server has no PowerShell to hand out (it could not reach GitHub). Install PowerShell 7 and run this again.'
                }
            }

            # PowerShell.MCP
            if (-not $psd1) {
                $scope = if ($Install -eq 'machine') { 'AllUsers' } else { 'CurrentUser' }
                $modulesRoot = if ($Install -eq 'machine') { Join-Path $programFiles 'PowerShell\Modules' } else { Join-Path $documents 'PowerShell\Modules' }
                $mode = $Install
                $others = @()
                if ($mode -ne 'none') {
                    $others = @(Get-OtherModuleVersions $modulesRoot)
                    # Another version installed WITHOUT a version folder occupies
                    # PowerShell.MCP\ itself: a second one cannot sit beside it
                    # there without breaking both. Leave it; unpack this build
                    # privately for this session instead.
                    $flat = Join-Path $modulesRoot 'PowerShell.MCP\PowerShell.MCP.psd1'
                    if (Test-Path -LiteralPath $flat) {
                        $flatVersion = try { (Import-PowerShellDataFile -LiteralPath $flat).ModuleVersion } catch { 'another version' }
                        Say "PowerShell.MCP $flatVersion is installed in $(Split-Path $flat) without a version folder, so $ModuleVersion cannot be installed beside it; it is left as is, and $ModuleVersion is used from $root for this session."
                        $mode = 'none'
                    }
                }
                if ($mode -ne 'none' -and $ModuleNupkgHash) {
                    # The Gallery package, installed the ordinary way from a
                    # throwaway local repository (PSResourceGet ships with 7.4+).
                    $repo = Join-Path $root 'repo'
                    if (Test-Path -LiteralPath $repo) { Remove-Item -LiteralPath $repo -Recurse -Force }
                    New-Item -ItemType Directory -Path $repo | Out-Null
                    $pkg = Get-Verified 'module.nupkg' $ModuleNupkgHash "PowerShell.MCP $ModuleVersion"
                    Move-Item -LiteralPath $pkg -Destination (Join-Path $repo "PowerShell.MCP.$ModuleVersion.nupkg") -Force
                    # Another version is never replaced: PSResourceGet puts this one
                    # in its own version folder. -Reinstall only replaces a different
                    # build of this same version, which Find-Module already rejected.
                    $installScript = "`$ErrorActionPreference = 'Stop'; `$name = 'PowerShell.MCP.bootstrap'; " +
                        "Register-PSResourceRepository -Name `$name -Uri '" + $repo.Replace("'", "''") + "' -Trusted; " +
                        "try { Install-PSResource -Name PowerShell.MCP -Version '$ModuleVersion' -Repository `$name -Scope $scope -TrustRepository -Reinstall -Quiet } " +
                        "finally { Unregister-PSResourceRepository -Name `$name }"
                    if ($here) {
                        # Already in PowerShell 7: install from here (a Store
                        # PowerShell's real path may not be startable directly).
                        & ([scriptblock]::Create($installScript))
                    }
                    else {
                        & $pwsh -NoProfile -NonInteractive -EncodedCommand ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($installScript)))
                        if ($LASTEXITCODE -ne 0) { throw 'Install-PSResource failed.' }
                    }
                    Remove-Item -LiteralPath $repo -Recurse -Force
                    $psd1 = Join-Path $modulesRoot "PowerShell.MCP\$ModuleVersion\PowerShell.MCP.psd1"
                    Say "Installed PowerShell.MCP $ModuleVersion from PowerShell Gallery ($scope)$(if ($others) { ' beside the existing ' + ($others -join ', ') + ', which is left as is' })."
                }
                elseif ($mode -ne 'none') {
                    $dir = Join-Path $modulesRoot "PowerShell.MCP\$ModuleVersion"
                    Expand-Verified 'module.zip' $ModuleZipHash $dir "PowerShell.MCP $ModuleVersion"
                    $psd1 = Join-Path $dir 'PowerShell.MCP.psd1'
                    Say "Installed PowerShell.MCP $ModuleVersion in $dir (copied from the MCP server's machine)$(if ($others) { ' beside the existing ' + ($others -join ', ') + ', which is left as is' })."
                }
                else {
                    $dir = Join-Path $root "PowerShell.MCP-$ModuleVersion"
                    Expand-Verified 'module.zip' $ModuleZipHash $dir "PowerShell.MCP $ModuleVersion"
                    $psd1 = Join-Path $dir 'PowerShell.MCP.psd1'
                }
            }

            $target = $Base -replace '^https?://', ''
            if ($here) {
                # -Global: imported from a script block, the module would
                # otherwise be visible only until this block returns.
                if (-not (Get-Module PowerShell.MCP)) { Import-Module $psd1 -Global }
                # Its notice starts and ends with a blank line, which also
                # closes what this printed before it.
                $said.Any = $false
                Enable-MCPRemoteControl -ConnectTo $target -Code $Code
            }
            else {
                # The code goes to the new window in an environment variable it
                # reads and deletes at once, not on its command line, which
                # process-creation auditing and EDR tools record.
                $command = "`$c = `$env:PSMCP_REMOTE_CODE; Remove-Item Env:PSMCP_REMOTE_CODE -ErrorAction SilentlyContinue; " +
                    "Import-Module '" + $psd1.Replace("'", "''") + "'; Enable-MCPRemoteControl -ConnectTo '" + $target.Replace("'", "''") + "' -Code `$c"
                # -EncodedCommand: Windows PowerShell drops embedded double quotes
                # when it passes arguments to a native program. -NoProfile: a
                # profile that imports another PowerShell.MCP version would load a
                # second copy of the module's DLL into this window.
                $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
                $env:PSMCP_REMOTE_CODE = $Code
                try { $process = Start-Process -FilePath $pwsh -ArgumentList '-NoExit', '-NoProfile', '-EncodedCommand', $encoded -PassThru }
                finally { Remove-Item Env:PSMCP_REMOTE_CODE -ErrorAction SilentlyContinue }
                # The outcome stands apart from the progress lines, as a failure does.
                if ($said.Any) { Write-Host '' }
                Say "Opened a PowerShell 7 window (PID $($process.Id)) for the MCP server at $target. This window can be closed." Green
            }
        }
        catch {
            if ($said.Any) { Write-Host '' }
            Say "PowerShell.MCP bootstrap failed: $($_.Exception.Message)" Red
        }
        finally {
            if ($said.Any) { Write-Host '' }
        }
        """;
}
