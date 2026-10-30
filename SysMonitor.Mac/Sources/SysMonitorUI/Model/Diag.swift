import Foundation

/// The diagnostics log, `~/Library/Logs/SysMonitor/sysmonitor.log`.
///
/// Timestamps are ISO 8601 in the POSIX locale. Under a Thai locale the
/// default formatter writes Buddhist-era years, which made the Windows build's
/// first log impossible to line up with anything else on the machine.
public enum Diag {
    private static let lock = NSLock()
    private static let maxBytes = 1 << 20
    nonisolated(unsafe) private static var overrideURL: URL?

    private static let formatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.calendar = Calendar(identifier: .gregorian)
        f.dateFormat = "yyyy-MM-dd'T'HH:mm:ss.SSSZZZZZ"
        return f
    }()

    public static var url: URL {
        lock.withLock { overrideURL } ?? FileManager.default
            .urls(for: .libraryDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Logs/SysMonitor/sysmonitor.log")
    }

    /// Tests point the log somewhere disposable.
    public static func redirect(to url: URL?) {
        lock.withLock { overrideURL = url }
    }

    public static func stamp(_ date: Date) -> String {
        formatter.string(from: date)
    }

    public static func write(_ message: String) {
        let file = url
        let line = "\(stamp(Date())) \(message)\n"
        lock.withLock {
            let fm = FileManager.default
            try? fm.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            // One generation of history: past a megabyte the log starts over
            // and the previous one is kept as .1.
            if let size = (try? fm.attributesOfItem(atPath: file.path))?[.size] as? Int, size > maxBytes {
                let old = file.appendingPathExtension("1")
                try? fm.removeItem(at: old)
                try? fm.moveItem(at: file, to: old)
            }
            if let handle = try? FileHandle(forWritingTo: file) {
                defer { try? handle.close() }
                _ = try? handle.seekToEnd()
                try? handle.write(contentsOf: Data(line.utf8))
            } else {
                try? Data(line.utf8).write(to: file)
            }
        }
    }
}
