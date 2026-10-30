import Darwin
import Foundation

/// One running process.
public struct ProcessEntry: Sendable, Equatable {
    public var pid: Int32
    public var name: String
    public var uid: UInt32

    /// Physical footprint, what Activity Monitor calls Memory. Nil for a
    /// process this user is not allowed to read: another user's, or root's.
    public var footprint: UInt64?

    /// The executable, for a process this user can read; otherwise empty.
    public var path: String

    /// When it started, in microseconds since 1970; 0 when not known. With
    /// the pid it names one process: a pid reused by the same name differs here.
    public var started: UInt64

    public init(pid: Int32, name: String, uid: UInt32, footprint: UInt64?, path: String = "", started: UInt64 = 0) {
        self.pid = pid
        self.name = name
        self.uid = uid
        self.footprint = footprint
        self.path = path
        self.started = started
    }
}

/// Every process, from one `KERN_PROC_ALL` sysctl for the list and names, and
/// `proc_pid_rusage` for the memory of the ones this user may read.
package enum ProcessSensor {
    /// Nil when the list could not be read, so the last one is kept rather
    /// than every process vanishing for a tick.
    package static func read() -> [ProcessEntry]? {
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_ALL, 0]
        var size = 0
        guard sysctl(&mib, UInt32(mib.count), nil, &size, nil, 0) == 0, size > 0 else { return nil }
        // Room for processes started between the two calls.
        size += size / 8
        let stride = MemoryLayout<kinfo_proc>.stride
        var procs = [kinfo_proc](repeating: kinfo_proc(), count: size / stride)
        guard sysctl(&mib, UInt32(mib.count), &procs, &size, nil, 0) == 0 else { return nil }
        let count = size / stride

        var result: [ProcessEntry] = []
        result.reserveCapacity(count)
        for kp in procs.prefix(count) {
            let pid = kp.kp_proc.p_pid
            // p_comm is cut at sixteen characters; proc_name gives the full
            // name where this user may ask for it.
            var name = withUnsafeBytes(of: kp.kp_proc.p_comm) {
                String(cString: $0.bindMemory(to: CChar.self).baseAddress!)
            }
            var buffer = [CChar](repeating: 0, count: 256)
            if proc_name(pid, &buffer, UInt32(buffer.count)) > 0 {
                name = String(cString: buffer)
            }
            result.append(ProcessEntry(pid: pid, name: name, uid: kp.kp_eproc.e_ucred.cr_uid,
                                       footprint: footprint(pid), path: path(pid),
                                       started: microseconds(kp.kp_proc.p_starttime)))
        }
        return result
    }

    package static func footprint(_ pid: Int32) -> UInt64? {
        var info = rusage_info_v4()
        let result = withUnsafeMutablePointer(to: &info) {
            $0.withMemoryRebound(to: rusage_info_t?.self, capacity: 1) {
                proc_pid_rusage(pid, RUSAGE_INFO_V4, $0)
            }
        }
        return result == 0 ? info.ri_phys_footprint : nil
    }

    package static func path(_ pid: Int32) -> String {
        var buffer = [CChar](repeating: 0, count: Int(MAXPATHLEN) * 4)
        return proc_pidpath(pid, &buffer, UInt32(buffer.count)) > 0 ? String(cString: buffer) : ""
    }

    /// When the process now holding a pid started, or nil if none does.
    package static func started(_ pid: Int32) -> UInt64? {
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_PID, pid]
        var kp = kinfo_proc()
        var size = MemoryLayout<kinfo_proc>.stride
        guard sysctl(&mib, UInt32(mib.count), &kp, &size, nil, 0) == 0, size > 0 else { return nil }
        return microseconds(kp.kp_proc.p_starttime)
    }

    private static func microseconds(_ t: timeval) -> UInt64 {
        UInt64(max(t.tv_sec, 0)) * 1_000_000 + UInt64(max(t.tv_usec, 0))
    }

    /// The current name of a pid, to check it is still the process the user
    /// was shown before acting on it: pids are reused.
    package static func name(_ pid: Int32) -> String? {
        var buffer = [CChar](repeating: 0, count: 256)
        return proc_name(pid, &buffer, UInt32(buffer.count)) > 0 ? String(cString: buffer) : nil
    }
}

public enum EndResult: Equatable, Sendable {
    case sent
    /// The system, this app, or another user's: never ours to end.
    case protected
    case notPermitted
    /// Already gone, or the pid now belongs to something else.
    case gone
    case failed(Int32)
}

/// Ending a process: SIGTERM, which lets it save and exit, never SIGKILL.
public enum ProcessControl {
    /// Ending any of these logs the user out or stops the machine.
    public static let protectedNames: Set<String> = ["kernel_task", "launchd", "WindowServer", "loginwindow"]

    public static func isProtected(_ p: ProcessEntry, ownPid: Int32 = getpid(), uid: UInt32 = getuid()) -> Bool {
        p.pid <= 1 || p.pid == ownPid || p.uid != uid || protectedNames.contains(p.name)
    }

    public static func end(_ p: ProcessEntry) -> EndResult {
        guard !isProtected(p) else { return .protected }
        guard ProcessSensor.name(p.pid) == p.name else { return .gone }
        // Same name, but a later process: a confirmation left open while the
        // shown one exited and its pid came round again.
        if p.started != 0, ProcessSensor.started(p.pid) != p.started { return .gone }
        if kill(p.pid, SIGTERM) == 0 {
            return .sent
        }
        switch errno {
        case ESRCH: return .gone
        case EPERM: return .notPermitted
        case let code: return .failed(code)
        }
    }
}
