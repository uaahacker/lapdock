using System.Runtime.InteropServices;

namespace LapDock.Services;

/// <summary>
/// One wildcard Windows performance counter read through PDH, e.g. <c>\Process(*)\Working Set - Private</c>
/// (Task Manager's per-process memory) or <c>\GPU Engine(*engtype_3D)\Utilization Percentage</c>.
/// PDH expands the wildcard on every read, so processes coming and going need no bookkeeping.
/// </summary>
public sealed class PdhCounter : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x200, PDH_FMT_NOCAP100 = 0x8000, PDH_MORE_DATA = 0x800007D2;
    // PDH_FMT_COUNTERVALUE_ITEM_W: name pointer, then the value struct 8-aligned (CStatus @8, double @16), 24 bytes.
    private const int ItemSize = 24, StatusOffset = 8, ValueOffset = 16;

    private readonly IntPtr _query, _counter;

    private PdhCounter(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    /// <summary>null when the counter doesn't exist here (no GPU counters in many VMs, for example).</summary>
    public static PdhCounter? TryOpen(string englishPath)
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out var query) != 0) return null;
        if (PdhAddEnglishCounter(query, englishPath, IntPtr.Zero, out var counter) != 0)
        {
            PdhCloseQuery(query);
            return null;
        }
        PdhCollectQueryData(query); // rate counters (utilisation) need a first sample to diff against
        return new PdhCounter(query, counter);
    }

    /// <summary>Samples now. Rate counters return nothing until they have two samples.</summary>
    public List<(string Instance, double Value)> Read()
    {
        var result = new List<(string, double)>();
        if (PdhCollectQueryData(_query) != 0) return result;
        uint size = 0;
        if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero) != PDH_MORE_DATA)
            return result;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArray(_counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, buffer) != 0)
                return result;
            for (int i = 0; i < count; i++)
            {
                var item = buffer + i * ItemSize;
                if (Marshal.ReadInt32(item, StatusOffset) is not (0 or 1)) continue; // PDH_CSTATUS_VALID_DATA / NEW_DATA
                var name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                result.Add((name, BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, ValueOffset))));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    public void Dispose() => PdhCloseQuery(_query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
}
