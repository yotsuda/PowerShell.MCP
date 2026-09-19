using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PowerShell.MCP.Services;

/// <summary>
/// Recognises the one way a command in this console can hang that looks like
/// nothing at all: a child process queued behind the prompt's console input
/// read.
/// </summary>
/// <remarks>
/// <para>
/// Windows serializes readers of a console input buffer, and PSReadLine holds
/// a blocking read for as long as the prompt is active — which, for an AI
/// command, is the whole run, because the command executes inside the polling
/// engine's timer event on the home thread. A child that touches console input
/// while starting up therefore queues behind PSReadLine and never comes back.
/// It is released only when a key finally arrives, so it looks random, and it
/// looks like the child's own fault.
/// </para>
/// <para>
/// It is rare — of eleven common CLIs measured under this condition (git, node,
/// npm, python, dotnet, curl, tar, ssh, …) none blocked; only a program that
/// genuinely waits for a key, or one that probes the console during startup,
/// does. LilyPond is the known case. Rare and invisible is a bad combination:
/// the project where this surfaced spent three months blaming antivirus, Mark
/// of the Web and DNS. So rather than change how commands run — see
/// docs/Console-Input-Starvation.md for the four fixes that were tried and
/// rejected — this names the condition when a command times out, which costs
/// nothing and requires the reader to know nothing in advance.
/// </para>
/// <para>
/// The fingerprint is distinctive: a process attached to this console, alive
/// for a while, burning no CPU, with threads parked in EventPairLow (the wait
/// an LPC call to the console host shows up as).
/// </para>
/// </remarks>
public static class StalledChild
{
    // A process that has had this long to run and still shows no CPU movement
    // is not merely slow.
    private const double MinimumAgeSeconds = 3.0;

    // Sampling window for the "is it actually running?" check. Long enough
    // that a busy process is certain to advance, short enough to sit inside a
    // timeout response without being noticed.
    private const int SampleMilliseconds = 250;

    // Anything below this over the sample is indistinguishable from idle.
    private const double CpuMovementSeconds = 0.01;

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    /// <summary>
    /// Returns a sentence naming a child of this console that is stalled on
    /// console input, or null when nothing matches. Never throws.
    /// </summary>
    public static string? Describe()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            var candidates = Candidates();
            if (candidates.Count == 0) return null;

            // Sample twice: a process that is working will move its CPU here.
            var before = new Dictionary<int, double>();
            foreach (var p in candidates)
            {
                try { before[p.Id] = p.TotalProcessorTime.TotalSeconds; } catch { }
            }

            Thread.Sleep(SampleMilliseconds);

            foreach (var p in candidates)
            {
                try
                {
                    if (p.HasExited) continue;
                    if (!before.TryGetValue(p.Id, out var cpuBefore)) continue;

                    p.Refresh();
                    if (p.TotalProcessorTime.TotalSeconds - cpuBefore > CpuMovementSeconds) continue;
                    if (!HasConsoleInputWait(p)) continue;

                    var seconds = (DateTime.Now - p.StartTime).TotalSeconds;
                    return $"⚠️ '{p.ProcessName}' (pid {p.Id}) has been running {seconds:F0}s with its CPU idle, "
                         + "waiting on this console's input — it is not slow, it is queued behind the prompt's "
                         + "input read and will not return on its own. Re-run it detached with its stdin from NUL: "
                         + "cmd /d /s /c \"<command> < NUL > out.log 2>&1\"";
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Processes attached to this console, other than this one. A child that
    /// inherited the console is attached to it, which is exactly the population
    /// that can queue behind the prompt's read — and it is one cheap call,
    /// where walking every process on the machine for a parent id is not.
    /// </summary>
    private static List<Process> Candidates()
    {
        var found = new List<Process>();
        try
        {
            var buffer = new uint[64];
            var count = GetConsoleProcessList(buffer, (uint)buffer.Length);
            if (count == 0 || count > buffer.Length) return found;

            var self = Environment.ProcessId;
            for (var i = 0; i < count; i++)
            {
                var pid = (int)buffer[i];
                if (pid == self) continue;
                try
                {
                    var p = Process.GetProcessById(pid);
                    if (p.HasExited) continue;
                    if ((DateTime.Now - p.StartTime).TotalSeconds < MinimumAgeSeconds) continue;
                    found.Add(p);
                }
                catch { }
            }
        }
        catch { }

        return found;
    }

    /// <summary>
    /// True when any thread is parked in EventPairLow — the wait an LPC call to
    /// the console host shows up as, and what every measured instance of this
    /// stall looked like (7 of 11 threads, for LilyPond).
    /// </summary>
    private static bool HasConsoleInputWait(Process p)
    {
        try
        {
            foreach (ProcessThread t in p.Threads)
            {
                try
                {
                    if (t.ThreadState == System.Diagnostics.ThreadState.Wait &&
                        t.WaitReason == ThreadWaitReason.EventPairLow)
                    {
                        return true;
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }
}
