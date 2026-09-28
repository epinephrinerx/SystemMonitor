namespace SysMonitor.Model;

/// <summary>
/// A fixed-length ring of recent readings, for the graphs.
///
/// Fixed length on purpose: a widget that runs for days must not grow a list
/// for days. The buffer is allocated once and overwritten, so a series costs
/// the same after a week as after a minute.
/// </summary>
public sealed class History
{
    private readonly double[] _values;
    private int _next;
    private int _count;

    public History(int capacity)
    {
        _values = new double[capacity];
    }

    public int Capacity => _values.Length;
    public int Count => _count;

    /// <summary>Incremented on every push, so a renderer can tell it is stale.</summary>
    public int Revision { get; private set; }

    public void Add(double value)
    {
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;
        if (_count < _values.Length)
        {
            _count++;
        }
        Revision++;
    }

    /// <summary>
    /// Copy the readings into <paramref name="into"/> oldest first, returning
    /// how many were written. The caller owns the buffer, so drawing a graph
    /// allocates nothing.
    /// </summary>
    public int CopyTo(double[] into)
    {
        int take = Math.Min(_count, into.Length);
        // Walk back from the newest so a partially filled ring still reads in
        // order, then fill forward.
        int start = (_next - take + _values.Length * 2) % _values.Length;
        for (int i = 0; i < take; i++)
        {
            into[i] = _values[(start + i) % _values.Length];
        }
        return take;
    }

    public double Latest => _count == 0
        ? 0
        : _values[(_next - 1 + _values.Length) % _values.Length];

    public double Max
    {
        get
        {
            double max = 0;
            for (int i = 0; i < _count; i++)
            {
                max = Math.Max(max, _values[i]);
            }
            return max;
        }
    }
}
