using System.Runtime.InteropServices;

namespace PowerShell.MCP.Services;

/// <summary>
/// Parent links of every process, from one Toolhelp snapshot (Windows), and
/// the descendants of a process read from them.
/// </summary>
internal static class ProcessTree
{
    /// <summary>pid → (parent pid, image name) for every process; empty when the snapshot fails.</summary>
    public static Dictionary<uint, (uint Parent, string Name)> Snapshot()
    {
        var map = new Dictionary<uint, (uint, string)>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == InvalidHandle) return map;
        try
        {
            var e = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref e)) return map;
            do { map[e.th32ProcessID] = (e.th32ParentProcessID, e.szExeFile); }
            while (Process32NextW(snap, ref e));
        }
        finally { CloseHandle(snap); }
        return map;
    }

    /// <summary>
    /// The processes below <paramref name="root"/> in <paramref name="snapshot"/>,
    /// breadth first, at most <paramref name="max"/>. Windows keeps a process's
    /// parent id after the parent exits, and a new process may take that id;
    /// so a process that started before its recorded parent is not that
    /// parent's child and is left out, with what hangs below it.
    /// <paramref name="startTime"/> gives a start time, or null when it cannot
    /// be read (then the link is trusted).
    /// </summary>
    public static List<uint> Descendants(uint root, IReadOnlyDictionary<uint, (uint Parent, string Name)> snapshot,
                                         Func<uint, DateTime?> startTime, int max = 256)
    {
        var children = new Dictionary<uint, List<uint>>();
        foreach (var (pid, entry) in snapshot)
        {
            if (pid == entry.Parent) continue; // the Idle process is its own parent
            if (!children.TryGetValue(entry.Parent, out var list)) children[entry.Parent] = list = [];
            list.Add(pid);
        }

        var found = new List<uint>();
        var seen = new HashSet<uint> { root };
        var queue = new Queue<uint>();
        queue.Enqueue(root);
        while (queue.Count > 0 && found.Count < max)
        {
            var parent = queue.Dequeue();
            if (!children.TryGetValue(parent, out var kids)) continue;
            var parentStart = startTime(parent);
            foreach (var child in kids)
            {
                if (found.Count >= max) break;
                if (!seen.Add(child)) continue;
                var childStart = startTime(child);
                if (parentStart != null && childStart != null && childStart < parentStart) continue;
                found.Add(child);
                queue.Enqueue(child);
            }
        }
        return found;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
