namespace PowerShell.MCP.Proxy.Services;

/// <summary>
/// The remote consoles of this MCP server: one RemoteConsoleClient per
/// console, at most <see cref="MaxConsoles"/> at a time. Each client is one
/// session as before (one connection, one pairing, its own accept port); this
/// only keeps them apart and picks the one a tool call names.
/// </summary>
/// <remarks>
/// A call that does not name a console is accepted only while there is just
/// one: with several, guessing (say, the last one used) is how a command ends
/// up on the wrong machine. The limit is low on purpose for the same reason —
/// every console is a machine someone is watching, and each one more is one
/// more the AI can mix up; fleets are for Invoke-Command and the like.
/// </remarks>
public sealed class RemoteConsoleRegistry : IDisposable
{
    /// <summary>At most this many remote consoles at once. Settable for tests.</summary>
    internal static int MaxConsoles { get; set; } = 4;

    // Connect, accept and disconnect, one at a time (a connect can take
    // seconds); requests only read the list, under _lock, and never wait here.
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly object _lock = new();
    private readonly List<RemoteConsoleClient> _consoles = [];
    // Why the consoles dropped by Prune ended, for the next "not connected".
    private readonly List<string> _endedReasons = [];

    public async Task<string> ConnectAsync(string target, string code, CancellationToken ct)
    {
        RemoteConsoleClient.ParseTarget(target); // reject a bad host before anything else
        await _changes.WaitAsync(ct);
        try
        {
            Prune();
            // The same host again: that session, whose ConnectAsync refuses
            // while it is connected and pairs anew once it lost its key.
            RemoteConsoleClient? existing;
            lock (_lock) existing = _consoles.FirstOrDefault(c => c.IsDialMode && c.Matches(target));
            if (existing != null)
                return await existing.ConnectAsync(target, code, ct);

            CheckRoom();
            var client = new RemoteConsoleClient();
            try
            {
                var notice = await client.ConnectAsync(target, code, ct);
                Add(client);
                return notice + Others(client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _changes.Release();
        }
    }

    public async Task<string> AcceptAsync(int port, string install, CancellationToken ct)
    {
        await _changes.WaitAsync(ct);
        try
        {
            Prune();
            // One console waiting to dial in at a time: called again, it
            // repeats that one's instructions, as it always has.
            RemoteConsoleClient? waiting;
            lock (_lock) waiting = _consoles.FirstOrDefault(c => c.IsWaitingForDialIn);
            if (waiting != null)
                return await waiting.AcceptAsync(port, install, ct);

            CheckRoom();
            var client = new RemoteConsoleClient();
            try
            {
                var instructions = await client.AcceptAsync(port, install, ct);
                Add(client);
                return instructions + Others(client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
        finally
        {
            _changes.Release();
        }
    }

    public async Task<string> DisconnectAsync(string? remote, CancellationToken ct)
    {
        await _changes.WaitAsync(ct);
        try
        {
            var client = Resolve(remote);
            var result = await client.DisconnectAsync(ct);
            lock (_lock) _consoles.Remove(client);
            client.Dispose();
            List<RemoteConsoleClient> left;
            lock (_lock) left = [.. _consoles];
            return left.Count switch
            {
                0 => result,
                1 => result + $"\n\nStill connected: {left[0].Target}; remote tool calls go there without naming it.",
                _ => result + $"\n\nStill connected: {string.Join(", ", left.Select(c => $"{c.Target} (remote: \"{c.Handle}\")"))}; remote tool calls must name one in the remote parameter.",
            };
        }
        finally
        {
            _changes.Release();
        }
    }

    /// <summary>
    /// The console <paramref name="remote"/> names, or the only one when it is
    /// omitted. Throws, listing the consoles, when that is not exactly one.
    /// </summary>
    public RemoteConsoleClient Resolve(string? remote)
    {
        Prune();
        List<RemoteConsoleClient> all;
        string? ended;
        lock (_lock)
        {
            all = [.. _consoles];
            ended = _endedReasons.Count > 0 ? string.Join("; ", _endedReasons) : null;
        }

        if (all.Count == 0)
            throw new InvalidOperationException(ended != null
                ? $"Not connected to a remote console: accepting stopped ({ended}). Call accept_remote_console again for a new code."
                : "Not connected to a remote console. Call connect_remote_console (to reach a listening console) or accept_remote_console (to let a console dial in) first.");

        if (string.IsNullOrWhiteSpace(remote))
        {
            if (all.Count == 1) return all[0];
            throw new InvalidOperationException(
                $"{all.Count} remote consoles are connected, so the remote parameter must say which one; nothing was sent. {List(all)}");
        }

        var matches = all.Where(c => c.Matches(remote)).ToList();
        if (matches.Count == 1) return matches[0];
        throw new InvalidOperationException(matches.Count == 0
            ? $"No remote console matches '{remote}'; nothing was sent. {List(all)}"
            : $"'{remote}' matches more than one remote console; nothing was sent. Name one by its port. {List(matches)}");
    }

    /// <summary>The consoles as the AI names them, for messages.</summary>
    private static string List(IEnumerable<RemoteConsoleClient> consoles) =>
        "Remote consoles: " + string.Join(", ", consoles.Select(c => $"{c.Target} (remote: \"{c.Handle}\")")) + ".";

    private void Add(RemoteConsoleClient client)
    {
        lock (_lock)
        {
            _consoles.Add(client);
            _endedReasons.Clear();
        }
    }

    /// <summary>After a change, the other consoles still connected, so the AI keeps track.</summary>
    private string Others(RemoteConsoleClient? self)
    {
        List<RemoteConsoleClient> others;
        lock (_lock) others = _consoles.Where(c => c != self).ToList();
        if (others.Count == 0) return "";
        var note = self != null ? $" (this one: \"{self.Handle}\")" : "";
        return $"\n\nAlso connected: {string.Join(", ", others.Select(c => $"{c.Target} (remote: \"{c.Handle}\")"))}. " +
               $"While more than one remote console is connected, every remote tool call must name its console in the remote parameter{note}.";
    }

    private void CheckRoom()
    {
        List<RemoteConsoleClient> all;
        lock (_lock) all = [.. _consoles];
        if (all.Count >= MaxConsoles)
            throw new InvalidOperationException(
                $"Already {MaxConsoles} remote consoles, the most this server keeps at once (each is a machine the AI must not mix up). " +
                $"disconnect_remote_console one that is no longer needed first. {List(all)}");
    }

    /// <summary>Drops sessions that have ended by themselves (accepting expired or locked out with no console).</summary>
    private void Prune()
    {
        List<RemoteConsoleClient> ended;
        lock (_lock)
        {
            ended = _consoles.Where(c => c.IsEnded).ToList();
            if (ended.Count == 0) return;
            _endedReasons.Clear();
            foreach (var c in ended)
            {
                _consoles.Remove(c);
                if (c.EndedReason != null) _endedReasons.Add(c.EndedReason);
            }
        }
        foreach (var c in ended) c.Dispose();
    }

    public void Dispose()
    {
        List<RemoteConsoleClient> all;
        lock (_lock)
        {
            all = [.. _consoles];
            _consoles.Clear();
        }
        foreach (var c in all) c.Dispose();
    }
}
