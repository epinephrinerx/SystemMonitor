public enum DiskLevel: Int, Comparable, Sendable {
    case ok, warm, hot

    public static func < (a: DiskLevel, b: DiskLevel) -> Bool { a.rawValue < b.rawValue }
}

/// Which drives are worth a warning banner. A port of `DiskAlerts.cs`.
///
/// A banner appears when a drive crosses into a worse level, stays until it is
/// dismissed or the drive recovers, and does not come back after a dismissal
/// until the drive gets worse again. Hysteresis keeps a drive sitting on the
/// line from flickering in and out of a level on every sample.
public final class DiskAlertTracker {
    public static let hysteresis = 2

    private var level: [String: DiskLevel] = [:]
    public private(set) var pending: [String: DiskLevel] = [:]

    public init() {}

    public static func classify(_ usage: Int, previous: DiskLevel) -> DiskLevel {
        if usage >= Palette.loadHotAt
            || (previous == .hot && usage >= Palette.loadHotAt - hysteresis) {
            return .hot
        }
        if usage >= Palette.loadWarmAt
            || (previous >= .warm && usage >= Palette.loadWarmAt - hysteresis) {
            return .warm
        }
        return .ok
    }

    /// Network shares are left out: freeing space on a server is not
    /// something this machine's storage settings can help with.
    public func update(_ drives: [(id: String, usage: Int, local: Bool)]) {
        var seen = Set<String>()
        for drive in drives where drive.local {
            seen.insert(drive.id)
            let before = level[drive.id]
            let now = Self.classify(drive.usage, previous: before ?? .ok)
            if now == .ok {
                pending[drive.id] = nil
            } else if before == nil || now > before! {
                pending[drive.id] = now
            } else if pending[drive.id] != nil {
                pending[drive.id] = now   // still waiting; say how bad it is now
            }
            level[drive.id] = now
        }
        for gone in level.keys where !seen.contains(gone) {
            level[gone] = nil
            pending[gone] = nil
        }
    }

    public func dismiss(_ id: String) {
        pending[id] = nil
    }
}
