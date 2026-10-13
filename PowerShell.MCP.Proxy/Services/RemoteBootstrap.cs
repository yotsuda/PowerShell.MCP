using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// What a bare Windows machine needs to dial in, handed out by the proxy over
/// plain HTTP on the accept port: a bootstrap script for Windows PowerShell
/// 5.1, the PowerShell.MCP module (zipped from this proxy's own module
/// folder, so the versions match by construction), and a portable
/// PowerShell 7 (the latest stable win-x64 zip, cached on this machine).
/// </summary>
/// <remarks>
/// Integrity runs through the one line the user pastes: it carries the
/// bootstrap's SHA-256, and the bootstrap carries the two zips' SHA-256, so
/// nothing served over HTTP is used unless it is what the proxy meant. The
/// pairing code is NOT in the bootstrap — anyone on the network can fetch
/// it — but is an argument on that pasted line.
/// </remarks>
public sealed class RemoteBootstrap
{
    public const string PwshPlatform = "win-x64";
    private static readonly HttpClient Http = CreateHttp();
    private static readonly SemaphoreSlim ModuleZipLock = new(1, 1);
    private static (byte[] zip, string hash, string version, string dllHash)? _moduleZip;

    public byte[] Script { get; private init; } = [];
    public string ScriptHash { get; private init; } = "";
    public string ModuleVersion { get; private init; } = "";
    public string? PwshVersion { get; private init; }
    private byte[] _moduleZipBytes = [];
    private Task<string?>? _pwshZipPath;

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
    /// Builds what one accept hands out. Null when this proxy cannot find its
    /// module folder (then only the "already installed" line is offered).
    /// The portable PowerShell may still be downloading when this returns;
    /// a request for it waits.
    /// </summary>
    public static async Task<RemoteBootstrap?> PrepareAsync(CancellationToken ct)
    {
        var module = await GetModuleZipAsync(ct);
        if (module == null) return null;
        var (pwshVersion, pwshHash, pwshZip) = await ResolvePwshAsync(ct);

        var script = BootstrapTemplate
            .Replace("__MODULE_VERSION__", module.Value.version)
            .Replace("__MODULE_HASH__", module.Value.hash)
            .Replace("__MODULE_DLL_HASH__", module.Value.dllHash)
            .Replace("__PWSH_VERSION__", pwshVersion ?? "")
            .Replace("__PWSH_HASH__", pwshHash ?? "")
            .Replace("\r\n", "\n").Replace("\n", "\r\n");
        var bytes = Encoding.ASCII.GetBytes(script);
        return new RemoteBootstrap
        {
            Script = bytes,
            ScriptHash = Convert.ToHexString(SHA256.HashData(bytes)),
            ModuleVersion = module.Value.version,
            PwshVersion = pwshVersion,
            _moduleZipBytes = module.Value.zip,
            _pwshZipPath = pwshZip,
        };
    }

    /// <summary>The line the user pastes into Windows PowerShell on the remote machine.</summary>
    public string OneLiner(string host, int port, string code) =>
        $"$u='http://{host}:{port}';$f=\"$env:TEMP\\psmcp-bootstrap.ps1\";iwr \"$u/bootstrap.ps1\" -OutFile $f -UseBasicParsing;" +
        $"if((Get-FileHash $f).Hash -eq '{ScriptHash}'){{powershell -NoProfile -ExecutionPolicy Bypass -File $f $u {code}}}else{{'bootstrap.ps1 did not match its hash; not run.'}}";

    // ----- module zip: this proxy's module folder, minus the proxy binaries -----

    private static async Task<(byte[] zip, string hash, string version, string dllHash)?> GetModuleZipAsync(CancellationToken ct)
    {
        await ModuleZipLock.WaitAsync(ct);
        try
        {
            if (_moduleZip != null) return _moduleZip;
            var dir = ModuleDirectory();
            if (dir == null) return null;
            var psd1 = await File.ReadAllTextAsync(Path.Combine(dir, "PowerShell.MCP.psd1"), ct);
            var match = System.Text.RegularExpressions.Regex.Match(psd1, @"ModuleVersion\s*=\s*'([^']+)'");
            var version = match.Success ? match.Groups[1].Value : Program.ProxyVersion;

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
            var bytes = buffer.ToArray();
            // The DLL's own hash identifies the build: a module installed on the
            // remote side is used instead of a download only when it is this
            // exact build, not merely the same version number.
            var dllHash = await HashFileAsync(Path.Combine(dir, "PowerShell.MCP.dll"), ct);
            _moduleZip = (bytes, Convert.ToHexString(SHA256.HashData(bytes)), version, dllHash);
            return _moduleZip;
        }
        finally
        {
            ModuleZipLock.Release();
        }
    }

    // ----- portable PowerShell: latest stable, cached under LocalAppData -----

    private static string CacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerShell.MCP", "cache");

    private static string CachePath(string version) => Path.Combine(CacheDirectory, $"PowerShell-{version}-{PwshPlatform}.zip");

    /// <summary>
    /// The version and SHA-256 to promise in the bootstrap, and a task for the
    /// file to serve. Asks GitHub for the latest stable release (its asset
    /// digest gives the hash before any download); falls back to the newest
    /// zip already cached; returns nulls when there is neither, and the
    /// bootstrap then requires PowerShell 7 to be installed on the remote side.
    /// </summary>
    private static async Task<(string? version, string? hash, Task<string?> zip)> ResolvePwshAsync(CancellationToken ct)
    {
        string? version = null, url = null, digest = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await Http.GetAsync("https://api.github.com/repos/PowerShell/PowerShell/releases/latest", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            version = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != $"PowerShell-{version}-{PwshPlatform}.zip") continue;
                url = asset.GetProperty("browser_download_url").GetString();
                if (asset.TryGetProperty("digest", out var d) && d.GetString() is { } s && s.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    digest = s["sha256:".Length..].ToUpperInvariant();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            Console.Error.WriteLine($"[WARN] Could not ask GitHub for the latest PowerShell: {ex.Message}");
            version = null;
        }

        if (version != null && url != null)
        {
            var cached = CachePath(version);
            if (File.Exists(cached))
            {
                var hash = await HashFileAsync(cached, ct);
                if (digest == null || hash == digest) return (version, hash, Task.FromResult<string?>(cached));
                File.Delete(cached); // a damaged or stale download
            }
            var download = DownloadAsync(url, cached, digest);
            if (digest != null) return (version, digest, download);
            // No digest from GitHub: the hash is known only once the file is here.
            var path = await download;
            return path == null ? (null, null, Task.FromResult<string?>(null)) : (version, await HashFileAsync(path, ct), Task.FromResult<string?>(path));
        }

        // Offline: the newest cached zip, if any.
        if (Directory.Exists(CacheDirectory))
        {
            var newest = Directory.EnumerateFiles(CacheDirectory, $"PowerShell-*-{PwshPlatform}.zip")
                .Select(p => (path: p, v: Version.TryParse(Path.GetFileName(p)["PowerShell-".Length..^($"-{PwshPlatform}.zip".Length)], out var v) ? v : null))
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

    // ----- HTTP: GET only, three paths, one response per connection -----

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
                    await WriteBodyAsync(stream, _moduleZipBytes, "application/zip", headOnly, ct);
                    break;
                case "/pwsh.zip":
                    var file = _pwshZipPath == null ? null : await _pwshZipPath.WaitAsync(TimeSpan.FromMinutes(10), ct);
                    if (file == null || !File.Exists(file)) { await WriteStatusAsync(stream, "404 Not Found", ct); break; }
                    await using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    {
                        var header = $"HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Length: {input.Length}\r\nConnection: close\r\n\r\n";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
                        if (!headOnly) await input.CopyToAsync(stream, ct);
                    }
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

    // ----- the bootstrap: ASCII only, runs in Windows PowerShell 5.1 -----

    internal const string BootstrapTemplate = """
        # PowerShell.MCP remote console bootstrap, handed out by accept_remote_console.
        # Runs in Windows PowerShell 5.1 (or PowerShell 7). Fetches the PowerShell.MCP
        # module and, unless PowerShell 7.4+ is installed, a portable PowerShell 7 from
        # the MCP server that served this file, checks both against the hashes below,
        # unpacks them under %LOCALAPPDATA%\PowerShell.MCP-remote, and opens a new
        # PowerShell 7 window that dials the MCP server. Nothing is installed or
        # registered; delete that folder to remove it all.
        param(
            [Parameter(Mandatory = $true)][string]$Base,
            [Parameter(Mandatory = $true)][string]$Code,
            [switch]$Portable
        )
        $ErrorActionPreference = 'Stop'
        $ProgressPreference = 'SilentlyContinue'
        $ModuleVersion = '__MODULE_VERSION__'
        $ModuleHash = '__MODULE_HASH__'
        $ModuleDllHash = '__MODULE_DLL_HASH__'
        $PwshVersion = '__PWSH_VERSION__'
        $PwshHash = '__PWSH_HASH__'

        $Base = $Base.TrimEnd('/')
        $root = Join-Path $env:LOCALAPPDATA 'PowerShell.MCP-remote'
        New-Item -ItemType Directory -Force $root | Out-Null

        function Get-Verified([string]$Name, [string]$Hash, [string]$Dir, [string]$What) {
            $zip = Join-Path $root "$Name.download.zip"
            Write-Host "Fetching $What from the MCP server ..."
            Invoke-WebRequest "$Base/$Name.zip" -OutFile $zip -UseBasicParsing
            if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $Hash) {
                Remove-Item $zip -Force
                throw "$Name.zip did not match the hash the MCP server promised; not used."
            }
            Write-Host "Unpacking $Name.zip ..."
            if (Test-Path $Dir) { Remove-Item $Dir -Recurse -Force }
            Expand-Archive -Path $zip -DestinationPath $Dir
            Remove-Item $zip -Force
        }

        # A PowerShell.MCP of exactly this version already installed for
        # PowerShell 7 (all users or this user; with or without a version
        # folder). Another version is left alone: the server's copy is used.
        function Find-InstalledModule {
            $documents = [Environment]::GetFolderPath('MyDocuments')
            $roots = @(
                (Join-Path $env:ProgramFiles 'PowerShell\7\Modules'),
                (Join-Path $env:ProgramFiles 'PowerShell\Modules'),
                (Join-Path $documents 'PowerShell\Modules')
            )
            foreach ($r in $roots) {
                foreach ($candidate in @((Join-Path $r "PowerShell.MCP\$ModuleVersion\PowerShell.MCP.psd1"), (Join-Path $r 'PowerShell.MCP\PowerShell.MCP.psd1'))) {
                    if (-not (Test-Path $candidate)) { continue }
                    try {
                        $dll = Join-Path (Split-Path $candidate) 'PowerShell.MCP.dll'
                        if ((Import-PowerShellDataFile $candidate).ModuleVersion -eq $ModuleVersion -and
                            (Test-Path $dll) -and (Get-FileHash $dll -Algorithm SHA256).Hash -eq $ModuleDllHash) { return $candidate }
                    } catch { }
                }
            }
            return $null
        }

        try {
            # The module: installed at this version, else unpacked by an earlier
            # run, else fetched. Only a fetch says anything.
            $psd1 = Find-InstalledModule
            if (-not $psd1) {
                $moduleDir = Join-Path $root "PowerShell.MCP-$ModuleVersion"
                $psd1 = Join-Path $moduleDir 'PowerShell.MCP.psd1'
                if (-not (Test-Path $psd1)) { Get-Verified 'module' $ModuleHash $moduleDir "PowerShell.MCP $ModuleVersion" }
            }

            # PowerShell 7: installed (7.4+), else unpacked by an earlier run,
            # else fetched.
            $pwsh = $null
            if (-not $Portable) {
                $installed = Get-Command pwsh -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($installed -and $installed.Version -ge [version]'7.4') { $pwsh = $installed.Source }
            }
            if (-not $pwsh) {
                $pwshDir = Join-Path $root "pwsh-$PwshVersion"
                if ($PwshHash -and -not (Test-Path (Join-Path $pwshDir 'pwsh.exe'))) { Get-Verified 'pwsh' $PwshHash $pwshDir "PowerShell $PwshVersion (portable, about 100 MB)" }
                if (-not $PwshHash -or -not (Test-Path (Join-Path $pwshDir 'pwsh.exe'))) {
                    throw 'PowerShell 7.4 or later is not installed here, and the MCP server has no portable PowerShell to hand out (it could not reach GitHub). Install PowerShell 7 and run this again.'
                }
                $pwsh = Join-Path $pwshDir 'pwsh.exe'
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
        """;
}