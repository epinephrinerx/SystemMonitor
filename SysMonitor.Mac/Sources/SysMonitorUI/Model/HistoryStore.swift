import SysMonitorCore

/// Every series the Full view graphs, pushed on every snapshot whatever view
/// is on screen -- so opening the Full view shows the last few minutes
/// rather than an empty box.
public final class HistoryStore {
    /// Seventy-two points is three minutes at the balanced cadence.
    public static let points = 72

    private var series: [String: History] = [:]

    public init() {}

    public func history(_ key: String) -> History? {
        series[key]
    }

    public var keys: Set<String> { Set(series.keys) }

    /// Add this snapshot's values. A device that has gone takes its history
    /// with it; one that comes back starts again from empty.
    public func push(_ s: Snapshot) {
        guard s.ready else { return }
        let values = Self.values(s)
        for (key, value) in values {
            let history = series[key] ?? {
                let new = History(capacity: Self.points)
                series[key] = new
                return new
            }()
            history.add(value)
        }
        let present = Set(values.map(\.key))
        series = series.filter { present.contains($0.key) }
    }

    /// The key each series is filed under, and its value now.
    public static func values(_ s: Snapshot) -> [(key: String, value: Double)] {
        var values: [(key: String, value: Double)] = [("cpu", Double(s.cpuTotal))]
        for (i, core) in s.cores.enumerated() {
            values.append(("core\(i)", Double(core.usage)))
        }
        values.append(("ram", Double(s.ram.usage)))

        var devices = Set<String>()
        for d in s.disks {
            values.append((Key.used(d.id), Double(d.usage)))
            guard let device = d.device, devices.insert(device.id).inserted else { continue }
            values.append((Key.read(device.id), d.readMb))
            values.append((Key.write(device.id), d.writeMb))
        }
        for (i, g) in s.gpus.enumerated() {
            values.append(("gpu\(i)", Double(g.usage)))
            if let renderer = g.rendererUsage { values.append(("gpu\(i).renderer", Double(renderer))) }
            if let tiler = g.tilerUsage { values.append(("gpu\(i).tiler", Double(tiler))) }
        }
        for a in s.adapters {
            values.append((Key.down(a.id), a.downMb))
            values.append((Key.up(a.id), a.upMb))
        }
        return values
    }

    public enum Key {
        public static func used(_ mount: String) -> String { "used.\(mount)" }
        public static func read(_ device: String) -> String { "io.read.\(device)" }
        public static func write(_ device: String) -> String { "io.write.\(device)" }
        public static func down(_ adapter: String) -> String { "net.down.\(adapter)" }
        public static func up(_ adapter: String) -> String { "net.up.\(adapter)" }
    }
}
