/// Turning lifetime byte counters into a per-second rate.
///
/// The baseline has to belong to the device's own last successful read.
/// Dividing by a shared tick meant a device whose probe had been skipped for
/// five minutes reported five minutes of accumulated bytes as one interval's
/// worth: 300 MB over 300 s read as 300 MB/s instead of 1 MB/s.
package enum IoRate {
    private static let mb = 1024.0 * 1024.0

    /// Stamps are milliseconds on a monotonic clock.
    package static func perSecond(
        previous: (read: UInt64, written: UInt64), previousStamp: UInt64,
        current: (read: UInt64, written: UInt64), stamp: UInt64
    ) -> (readMb: Double, writeMb: Double) {
        guard stamp > previousStamp else { return (0, 0) }
        let elapsed = Double(stamp - previousStamp) / 1000
        // Counters reset when a device is re-attached; a negative delta is
        // not a negative rate.
        let read = current.read >= previous.read ? current.read - previous.read : 0
        let written = current.written >= previous.written ? current.written - previous.written : 0
        return (Double(read) / elapsed / mb, Double(written) / elapsed / mb)
    }
}
