import Darwin

/// Reading named kernel values, and the clock everything is stamped with.
package enum Sysctl {
    /// An integer of whatever width the kernel stores it in.
    package static func int(_ name: String) -> Int? {
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0 else { return nil }
        switch size {
        case 4:
            var value: Int32 = 0
            guard sysctlbyname(name, &value, &size, nil, 0) == 0 else { return nil }
            return Int(value)
        case 8:
            var value: Int64 = 0
            guard sysctlbyname(name, &value, &size, nil, 0) == 0 else { return nil }
            return Int(value)
        default:
            return nil
        }
    }

    package static func string(_ name: String) -> String? {
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return nil }
        var buffer = [CChar](repeating: 0, count: size)
        guard sysctlbyname(name, &buffer, &size, nil, 0) == 0 else { return nil }
        return String(cString: buffer)
    }

    package static func value<T>(_ name: String, as _: T.Type) -> T? {
        var size = MemoryLayout<T>.size
        let pointer = UnsafeMutablePointer<T>.allocate(capacity: 1)
        defer { pointer.deallocate() }
        guard sysctlbyname(name, pointer, &size, nil, 0) == 0,
              size == MemoryLayout<T>.size else { return nil }
        return pointer.pointee
    }
}

package enum Clock {
    /// Milliseconds on a monotonic clock that keeps counting through sleep,
    /// so a rate measured across a sleep is the true average over it.
    package static func ms() -> UInt64 {
        clock_gettime_nsec_np(CLOCK_MONOTONIC_RAW) / 1_000_000
    }
}
