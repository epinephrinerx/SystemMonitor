using System.Runtime.InteropServices;

namespace SysMonitor.Native;

/// <summary>
/// The performance-counter API, enough of it to read one wildcard counter.
///
/// This is how Task Manager gets its GPU figures: the WDDM driver publishes
/// `\GPU Engine(*)\Utilization Percentage`, and there is no other unelevated
/// way to the number. PDH is in the base OS, so this stays inside the rule
/// that shipped code uses the class library and P/Invoke and nothing else.
///
/// A wildcard query is a single handle held open for the life of the process.
/// Reopening it per sample would re-expand the instance list every time, and
/// on a machine with a thousand GPU engine instances that is the expensive
/// part.
/// </summary>
internal sealed class Pdh : IDisposable
{
    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const uint PdhMoreData = 0x800007D2;

    /// <summary>
    /// `PDH_FMT_COUNTERVALUE_ITEM_W`: a name, then a whole
    /// `PDH_FMT_COUNTERVALUE`, which is a status word *and* the union -- not
    /// the union alone.
    ///
    /// Leaving `Status` out makes the item 16 bytes instead of 24, so every
    /// item after the first is read from the wrong offset and `Name` comes
    /// back as a pointer into the middle of a double. That is an access
    /// violation, and an access violation cannot be caught in .NET, so it
    /// takes the process with it.
    ///
    /// Sequential layout is right on both widths: the double is aligned to 8
    /// either way, so the padding after `Status` appears where it should.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public IntPtr Name;
        public uint Status;
        public double Value;        // the union, read as PDH_FMT_DOUBLE
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? source, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(
        IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(
        IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private IntPtr _counter;

    /// <summary>The buffer PDH fills, kept between samples so it is grown once.</summary>
    private IntPtr _buffer;
    private uint _bufferSize;

    private Pdh(IntPtr query, IntPtr counter)
    {
        _query = query;
        _counter = counter;
    }

    /// <summary>
    /// Open a wildcard counter, or null if it does not exist on this machine.
    ///
    /// The counter is added by its English name, so a Thai or German Windows
    /// resolves the same path: the localised names differ and the English ones
    /// are what the documentation and every example use.
    /// </summary>
    public static Pdh? Open(string path)
    {
        if (PdhOpenQueryW(null, IntPtr.Zero, out IntPtr query) != 0)
        {
            return null;
        }
        if (PdhAddEnglishCounterW(query, path, IntPtr.Zero, out IntPtr counter) != 0)
        {
            PdhCloseQuery(query);
            return null;
        }

        // A rate counter needs two collections before it has a value. This is
        // the first; the caller's first Read is the second.
        PdhCollectQueryData(query);
        return new Pdh(query, counter);
    }

    /// <summary>
    /// Collect once and hand back every instance and its value.
    ///
    /// Instance names repeat -- GPU engines are named per process, and several
    /// processes run on the same engine -- so this returns a list rather than
    /// a dictionary and lets the caller decide how to combine them.
    /// </summary>
    public List<(string Instance, double Value)> Read()
    {
        var values = new List<(string, double)>();
        if (_query == IntPtr.Zero || PdhCollectQueryData(_query) != 0)
        {
            return values;
        }

        uint size = _bufferSize;
        uint count;
        uint status = PdhGetFormattedCounterArrayW(
            _counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out count, _buffer);

        if (status == PdhMoreData)
        {
            // Ask again with the size PDH just asked for, with room to spare:
            // instances come and go between the two calls, and a buffer sized
            // exactly for the first answer can be too small for the second.
            size += size / 4 + 4096;
            Grow(size);
            status = PdhGetFormattedCounterArrayW(
                _counter, PdhFmtDouble | PdhFmtNoCap100, ref size, out count, _buffer);
        }
        if (status != 0 || _buffer == IntPtr.Zero)
        {
            return values;
        }

        int stride = Marshal.SizeOf<CounterItem>();
        for (int i = 0; i < count; i++)
        {
            CounterItem item = Marshal.PtrToStructure<CounterItem>(_buffer + i * stride);

            // 0 is valid data, 1 is new data; anything else means PDH could
            // not compute this instance and the value is not to be believed.
            if (item.Status > 1 || item.Name == IntPtr.Zero)
            {
                continue;
            }
            string? name = Marshal.PtrToStringUni(item.Name);
            if (name is not null)
            {
                values.Add((name, item.Value));
            }
        }
        return values;
    }

    private void Grow(uint size)
    {
        if (size <= _bufferSize && _buffer != IntPtr.Zero)
        {
            return;
        }
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
        }
        _buffer = Marshal.AllocHGlobal((int)size);
        _bufferSize = size;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
            _counter = IntPtr.Zero;
        }
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            _bufferSize = 0;
        }
    }
}
