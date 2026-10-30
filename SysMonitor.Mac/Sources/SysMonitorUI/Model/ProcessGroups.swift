import Foundation
import SysMonitorCore

/// Processes with one name, counted together: thirty browser helpers are one
/// line that can be opened, not thirty lines.
public struct ProcessGroup: Equatable, Sendable, Identifiable {
    public var id: String { name }
    public var name: String
    /// Largest footprint first; the unreadable ones last.
    public var members: [ProcessEntry]
    public var footprint: UInt64
    /// False when no member's memory could be read: the system's, another
    /// user's.
    public var readable: Bool
    /// True when no member may be ended from here.
    public var protected: Bool

    public var megabytes: Double { Double(footprint) / 1_048_576 }
}

public enum ProcessGroups {
    /// Readable groups by memory, largest first; then the unreadable ones by
    /// name. A filter matches anywhere in the name, ignoring case.
    public static func build(_ processes: [ProcessEntry], filter: String = "",
                             ownPid: Int32 = getpid(), uid: UInt32 = getuid()) -> [ProcessGroup] {
        let needle = filter.trimmingCharacters(in: .whitespaces)
        var byName: [String: [ProcessEntry]] = [:]
        for p in processes where needle.isEmpty || p.name.localizedCaseInsensitiveContains(needle) {
            byName[p.name, default: []].append(p)
        }
        let groups = byName.map { name, members in
            ProcessGroup(
                name: name,
                members: members.sorted { ($0.footprint ?? 0, $1.pid) > ($1.footprint ?? 0, $0.pid) },
                footprint: members.reduce(0) { $0 + ($1.footprint ?? 0) },
                readable: members.contains { $0.footprint != nil },
                protected: members.allSatisfy { ProcessControl.isProtected($0, ownPid: ownPid, uid: uid) })
        }
        return groups.sorted {
            if $0.readable != $1.readable { return $0.readable }
            if $0.footprint != $1.footprint { return $0.footprint > $1.footprint }
            return $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending
        }
    }

    public static func top(_ processes: [ProcessEntry]) -> ProcessGroup? {
        build(processes).first { $0.readable && $0.footprint > 0 }
    }

    /// "Claude Helper 764 MB": the line under the RAM figure.
    public static func topLine(_ processes: [ProcessEntry], _ t: Strings) -> String? {
        top(processes).map { t("mem_top", "\($0.name) \(megabytes($0.megabytes))") }
    }

    public static func megabytes(_ mb: Double) -> String {
        mb >= 1024 ? String(format: "%.1f GB", mb / 1024) : String(format: "%.0f MB", mb)
    }

    public static func historyKey(_ name: String) -> String { "proc.\(name)" }
}
