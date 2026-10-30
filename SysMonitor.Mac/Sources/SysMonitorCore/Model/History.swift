/// A fixed-length ring of recent readings, for the graphs.
///
/// Fixed length on purpose: a widget that runs for days must not grow a list
/// for days. The buffer is allocated once and overwritten, so a series costs
/// the same after a week as after a minute.
public final class History {
    private var values: [Double]
    private var next = 0
    public private(set) var count = 0

    /// Incremented on every push, so a renderer can tell it is stale.
    public private(set) var revision = 0

    public init(capacity: Int) {
        precondition(capacity > 0, "a history needs room for one reading")
        values = Array(repeating: 0, count: capacity)
    }

    public var capacity: Int { values.count }

    public func add(_ value: Double) {
        values[next] = value
        next = (next + 1) % values.count
        if count < values.count {
            count += 1
        }
        revision += 1
    }

    /// Copy the readings into `buffer` oldest first, returning how many were
    /// written. The caller owns the buffer, so drawing a graph allocates
    /// nothing.
    @discardableResult
    public func copy(into buffer: inout [Double]) -> Int {
        let take = min(count, buffer.count)
        // Walk back from the newest so a partially filled ring still reads in
        // order, then fill forward.
        let start = (next - take + values.count * 2) % values.count
        for i in 0..<take {
            buffer[i] = values[(start + i) % values.count]
        }
        return take
    }

    public var latest: Double {
        count == 0 ? 0 : values[(next - 1 + values.count) % values.count]
    }

    /// The largest reading still held, or zero.
    public var max: Double {
        var result = 0.0
        for i in 0..<count {
            result = Swift.max(result, values[i])
        }
        return result
    }
}
