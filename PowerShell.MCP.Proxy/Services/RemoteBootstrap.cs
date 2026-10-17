using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// What a bare Windows machine needs to dial in, handed out by the proxy over
/// plain HTTP on the accept port: a bootstrap script for Windows PowerShell
/// 5.1, the PowerShell.MCP module, and PowerShell 7.
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
/// (missing pieces are unpacked into a per-user folder that can be deleted),
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

    /// <summary>The line the user pastes into Windows PowerShell on the remote machine.</summary>
    public string OneLiner(string host, int port, string code) =>
        $"$u='http://{host}:{port}';$f=\"$env:TEMP\\psmcp-bootstrap.ps1\";iwr \"$u/bootstrap.ps1\" -OutFile $f -UseBasicParsing;" +
        $"if((Get-FileHash $f).Hash -eq '{ScriptHash}'){{powershell -NoProfile -ExecutionPolicy Bypass -File $f $u {code}}}else{{'bootstrap.ps1 did not match its hash; not run.'}}";

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
            "user" => $"install = user (no admin needed): if missing, {pwsh} goes to %LOCALAPPDATA%\\Programs\\PowerShell\\7 and onto the user's PATH, and {module} into the user's PowerShell modules folder, {source}.",
            "machine" => $"install = machine (Windows PowerShell must be opened as Administrator): if missing, {pwsh} is installed with its MSI into Program Files (on PATH, updated by Microsoft Update), and {module} into Program Files\\PowerShell\\Modules for all users, {source}.",
            _ => $"install = none: nothing is installed. Missing pieces ({pwsh}, {module}) are unpacked into %LOCALAPPDATA%\\PowerShell.MCP-remote, reused next time, and removed by deleting that folder.",
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
                    // build leaves swapped-out copies that must not travel.
                    if (relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Contains(".stash-") || name.Contains(".new-") || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
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
            var version = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            if (version == null) return null;
            var assets = new Dictionary<string, (string url, string? digest)>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (name == null || url == null) continue;
                string? digest = null;
                if (asset.TryGetProperty("digest", out var d) && d.GetString() is { } s && s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    digest = s["sha256:".Length..].ToUpperInvariant();
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

    // ----- the bootstrap: ASCII only, runs in Windows PowerShell 5.1 -----

    internal const string BootstrapTemplate = """
        # PowerShell.MCP remote console bootstrap, handed out by accept_remote_console.
        # Runs in Windows PowerShell 5.1 (or PowerShell 7). Uses PowerShell 7.4+ and
        # PowerShell.MCP where they are already installed (the same build); otherwise
        # fetches them from the MCP server that served this file, checks them against
        # the hashes below, and installs or unpacks them as the user chose ($Install),
        # then opens a PowerShell 7 window that dials the MCP server.
        #   none    : nothing installed; unpacked under %LOCALAPPDATA%\PowerShell.MCP-remote
        #   user    : PowerShell 7 in %LOCALAPPDATA%\Programs\PowerShell\7 (+ user PATH),
        #             PowerShell.MCP for this user
        #   machine : PowerShell 7 by its MSI, PowerShell.MCP for all users (needs admin)
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
        $root = Join-Path $env:LOCALAPPDATA 'PowerShell.MCP-remote'
        New-Item -ItemType Directory -Force $root | Out-Null
        $documents = [Environment]::GetFolderPath('MyDocuments')
        $userPwshDir = Join-Path $env:LOCALAPPDATA 'Programs\PowerShell\7'
        $machinePwsh = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'

        function Get-Verified([string]$Name, [string]$Hash, [string]$What) {
            $file = Join-Path $root ("download-" + $Name)
            Write-Host "Fetching $What from the MCP server ..."
            Invoke-WebRequest "$Base/$Name" -OutFile $file -UseBasicParsing
            if ((Get-FileHash $file -Algorithm SHA256).Hash -ne $Hash) {
                Remove-Item $file -Force
                throw "$Name did not match the hash the MCP server promised; not used."
            }
            return $file
        }

        function Expand-Verified([string]$Name, [string]$Hash, [string]$Dir, [string]$What) {
            $file = Get-Verified $Name $Hash $What
            $zip = [IO.Path]::ChangeExtension($file, '.zip')
            Move-Item $file $zip -Force
            if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
            Expand-Archive -Path $zip -DestinationPath $Dir
            Remove-Item $zip -Force
        }

        # PowerShell.MCP of exactly this build already installed for PowerShell 7
        # (all users, this user, or unpacked by an earlier run). Another build is
        # left alone.
        function Find-Module {
            $candidates = @(
                (Join-Path $env:ProgramFiles "PowerShell\Modules\PowerShell.MCP\$ModuleVersion\PowerShell.MCP.psd1"),
                (Join-Path $env:ProgramFiles 'PowerShell\7\Modules\PowerShell.MCP\PowerShell.MCP.psd1'),
                (Join-Path $env:ProgramFiles 'PowerShell\Modules\PowerShell.MCP\PowerShell.MCP.psd1'),
                (Join-Path $documents "PowerShell\Modules\PowerShell.MCP\$ModuleVersion\PowerShell.MCP.psd1"),
                (Join-Path $documents 'PowerShell\Modules\PowerShell.MCP\PowerShell.MCP.psd1'),
                (Join-Path $root "PowerShell.MCP-$ModuleVersion\PowerShell.MCP.psd1")
            )
            foreach ($candidate in $candidates) {
                if (-not (Test-Path $candidate)) { continue }
                try {
                    $dll = Join-Path (Split-Path $candidate) 'PowerShell.MCP.dll'
                    if ((Import-PowerShellDataFile $candidate).ModuleVersion -eq $ModuleVersion -and
                        (Test-Path $dll) -and (Get-FileHash $dll -Algorithm SHA256).Hash -eq $ModuleDllHash) { return $candidate }
                } catch { }
            }
            return $null
        }

        # Other PowerShell.MCP versions installed here: left exactly as they are.
        # This window always imports this build by its full path.
        function Get-OtherModuleVersions([string]$ModulesRoot) {
            $dir = Join-Path $ModulesRoot 'PowerShell.MCP'
            $found = @()
            if (-not (Test-Path $dir)) { return $found }
            foreach ($psd in @(Get-ChildItem $dir -Filter PowerShell.MCP.psd1 -Recurse -Depth 1 -ErrorAction SilentlyContinue)) {
                try { $v = (Import-PowerShellDataFile $psd.FullName).ModuleVersion; if ($v -ne $ModuleVersion) { $found += $v } } catch { }
            }
            return $found
        }

        # PowerShell 7.4+: on PATH, installed for all users, installed for this
        # user, or unpacked by an earlier run.
        function Find-Pwsh {
            if (-not $Portable) {
                $candidates = @()
                $onPath = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($onPath) { $candidates += $onPath.Source }
                $candidates += $machinePwsh, (Join-Path $userPwshDir 'pwsh.exe')
                foreach ($candidate in $candidates) {
                    if ((Test-Path $candidate) -and [version](Get-Item $candidate).VersionInfo.FileVersion -ge [version]'7.4') { return $candidate }
                }
            }
            $unpacked = Join-Path $root "pwsh-$PwshVersion\pwsh.exe"
            if ($PwshVersion -and (Test-Path $unpacked)) { return $unpacked }
            return $null
        }

        # A blank line before and after everything this prints, like the
        # module's own remote control notices.
        Write-Host ''
        try {
            $elevated =([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            $psd1 = Find-Module
            $pwsh = Find-Pwsh
            if ($Install -eq 'machine' -and -not $elevated -and (-not $psd1 -or -not $pwsh)) {
                throw 'Installing for all users needs Windows PowerShell opened as Administrator. Open it that way and paste the same line again.'
            }

            # PowerShell 7
            if (-not $pwsh) {
                if ($Install -eq 'machine' -and $PwshMsiHash) {
                    $msi = Get-Verified 'pwsh.msi' $PwshMsiHash "PowerShell $PwshVersion installer"
                    $msiPath = [IO.Path]::ChangeExtension($msi, '.msi')
                    Move-Item $msi $msiPath -Force
                    Write-Host "Installing PowerShell $PwshVersion for all users ..."
                    $p = Start-Process msiexec.exe -ArgumentList '/i', ('"' + $msiPath + '"'), '/qn', 'ADD_PATH=1', 'USE_MU=1', 'ENABLE_MU=1', 'REGISTER_MANIFEST=1' -Wait -PassThru
                    Remove-Item $msiPath -Force
                    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) { throw "The PowerShell installer failed (exit code $($p.ExitCode))." }
                    $pwsh = $machinePwsh
                }
                elseif ($PwshZipHash) {
                    if ($Install -eq 'user') {
                        Expand-Verified 'pwsh.zip' $PwshZipHash $userPwshDir "PowerShell $PwshVersion (about 100 MB)"
                        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
                        if (($userPath -split ';') -notcontains $userPwshDir) {
                            [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $userPwshDir).TrimStart(';'), 'User')
                        }
                        Write-Host "Installed PowerShell $PwshVersion for this user in $userPwshDir (added to your PATH)."
                        $pwsh = Join-Path $userPwshDir 'pwsh.exe'
                    }
                    else {
                        $dir = Join-Path $root "pwsh-$PwshVersion"
                        Expand-Verified 'pwsh.zip' $PwshZipHash $dir "PowerShell $PwshVersion (portable, about 100 MB)"
                        $pwsh = Join-Path $dir 'pwsh.exe'
                    }
                }
                else {
                    throw 'PowerShell 7.4 or later is not installed here, and the MCP server has no PowerShell to hand out (it could not reach GitHub). Install PowerShell 7 and run this again.'
                }
            }

            # PowerShell.MCP
            if (-not $psd1) {
                $scope = if ($Install -eq 'machine') { 'AllUsers' } else { 'CurrentUser' }
                $modulesRoot = if ($Install -eq 'machine') { Join-Path $env:ProgramFiles 'PowerShell\Modules' } else { Join-Path $documents 'PowerShell\Modules' }
                $mode = $Install
                $others = @()
                if ($mode -ne 'none') {
                    $others = @(Get-OtherModuleVersions $modulesRoot)
                    # Another version installed WITHOUT a version folder occupies
                    # PowerShell.MCP\ itself: a second one cannot sit beside it
                    # there without breaking both. Leave it; unpack this build
                    # privately for this session instead.
                    $flat = Join-Path $modulesRoot 'PowerShell.MCP\PowerShell.MCP.psd1'
                    if (Test-Path $flat) {
                        $flatVersion = try { (Import-PowerShellDataFile $flat).ModuleVersion } catch { 'another version' }
                        Write-Host "PowerShell.MCP $flatVersion is installed in $(Split-Path $flat) without a version folder, so $ModuleVersion cannot be installed beside it; it is left as is, and $ModuleVersion is used from $root for this session."
                        $mode = 'none'
                    }
                }
                if ($mode -ne 'none' -and $ModuleNupkgHash) {
                    # The Gallery package, installed the ordinary way from a
                    # throwaway local repository (PSResourceGet ships with 7.4+).
                    $repo = Join-Path $root 'repo'
                    if (Test-Path $repo) { Remove-Item $repo -Recurse -Force }
                    New-Item -ItemType Directory $repo | Out-Null
                    $pkg = Get-Verified 'module.nupkg' $ModuleNupkgHash "PowerShell.MCP $ModuleVersion"
                    Move-Item $pkg (Join-Path $repo "PowerShell.MCP.$ModuleVersion.nupkg") -Force
                    # -Reinstall is not passed: another version is never replaced;
                    # PSResourceGet puts this one in its own version folder.
                    $installScript = "`$ErrorActionPreference = 'Stop'; `$name = 'PowerShell.MCP.bootstrap'; " +
                        "Register-PSResourceRepository -Name `$name -Uri '" + $repo.Replace("'", "''") + "' -Trusted; " +
                        "try { Install-PSResource -Name PowerShell.MCP -Version '$ModuleVersion' -Repository `$name -Scope $scope -TrustRepository -Reinstall -Quiet } " +
                        "finally { Unregister-PSResourceRepository -Name `$name }"
                    & $pwsh -NoProfile -NonInteractive -EncodedCommand ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($installScript)))
                    if ($LASTEXITCODE -ne 0) { throw 'Install-PSResource failed.' }
                    Remove-Item $repo -Recurse -Force
                    $psd1 = Join-Path $modulesRoot "PowerShell.MCP\$ModuleVersion\PowerShell.MCP.psd1"
                    Write-Host "Installed PowerShell.MCP $ModuleVersion from PowerShell Gallery ($scope)$(if ($others) { ' beside the existing ' + ($others -join ', ') + ', which is left as is' })."
                }
                elseif ($mode -ne 'none') {
                    $dir = Join-Path $modulesRoot "PowerShell.MCP\$ModuleVersion"
                    Expand-Verified 'module.zip' $ModuleZipHash $dir "PowerShell.MCP $ModuleVersion"
                    $psd1 = Join-Path $dir 'PowerShell.MCP.psd1'
                    Write-Host "Installed PowerShell.MCP $ModuleVersion in $dir (copied from the MCP server's machine)$(if ($others) { ' beside the existing ' + ($others -join ', ') + ', which is left as is' })."
                }
                else {
                    $dir = Join-Path $root "PowerShell.MCP-$ModuleVersion"
                    Expand-Verified 'module.zip' $ModuleZipHash $dir "PowerShell.MCP $ModuleVersion"
                    $psd1 = Join-Path $dir 'PowerShell.MCP.psd1'
                }
            }

            $target = $Base -replace '^https?://', ''
            $command = "Import-Module '" + $psd1.Replace("'", "''") + "'; Enable-MCPRemoteControl -ConnectTo '" + $target + "' -Code '" + $Code.Replace("'", "''") + "'"
            # -EncodedCommand: Windows PowerShell drops embedded double quotes
            # when it passes arguments to a native program. -NoProfile: a
            # profile that imports another PowerShell.MCP version would load a
            # second copy of the module's DLL into this window.
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
            $process = Start-Process -FilePath $pwsh -ArgumentList '-NoExit', '-NoProfile', '-EncodedCommand', $encoded -PassThru
            Write-Host "Opened a PowerShell 7 window (PID $($process.Id)) for the MCP server at $target. This window can be closed." -ForegroundColor Green
        }
        catch {
            Write-Host ''
            Write-Host "PowerShell.MCP bootstrap failed: $($_.Exception.Message)" -ForegroundColor Red
        }
        Write-Host ''
        """;
}
