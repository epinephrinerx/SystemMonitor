import Darwin

/// The page counts that memory usage is built from.
package struct VmCounts: Equatable, Sendable {
    package var internalPages: UInt64 = 0
    package var purgeablePages: UInt64 = 0
    package var wiredPages: UInt64 = 0
    package var compressedPages: UInt64 = 0
    package var pageSize: UInt64 = 0

    package init() {}
}

package enum MemoryMath {
    private static let gb = 1024.0 * 1024.0 * 1024.0

    /// "Memory Used" the way Activity Monitor adds it up: app memory (anonymous
    /// pages that cannot be purged), plus wired, plus what the compressor holds.
    /// File cache is not in it -- the kernel hands that back the moment
    /// anything asks, so counting it would make every Mac look full.
    package static func ram(_ c: VmCounts, totalBytes: UInt64) -> Ram {
        let app = (c.internalPages - min(c.purgeablePages, c.internalPages)) * c.pageSize
        let wired = c.wiredPages * c.pageSize
        let compressed = c.compressedPages * c.pageSize
        let used = min(app + wired + compressed, totalBytes)

        var ram = Ram()
        ram.totalGb = Double(totalBytes) / gb
        ram.usedGb = Double(used) / gb
        ram.appGb = Double(app) / gb
        ram.wiredGb = Double(wired) / gb
        ram.compressedGb = Double(compressed) / gb
        ram.usage = totalBytes == 0 ? 0 : Int((Double(used) * 100 / Double(totalBytes)).rounded())
        return ram
    }

    /// `kern.memorystatus_vm_pressure_level`: 1, 2 and 4.
    package static func pressure(_ level: Int?) -> MemoryPressure {
        switch level {
        case 1: return .normal
        case 2: return .warning
        case 4: return .critical
        default: return .unknown
        }
    }
}

package enum MemorySensor {
    private static let host = mach_host_self()
    private static let gb = 1024.0 * 1024.0 * 1024.0

    package static func read() -> Ram? {
        guard let total = Sysctl.int("hw.memsize"), let pageSize = Sysctl.int("hw.pagesize") else {
            return nil
        }
        var stats = vm_statistics64()
        var count = mach_msg_type_number_t(
            MemoryLayout<vm_statistics64_data_t>.stride / MemoryLayout<integer_t>.stride)
        let result = withUnsafeMutablePointer(to: &stats) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(host, HOST_VM_INFO64, $0, &count)
            }
        }
        guard result == KERN_SUCCESS else { return nil }

        var counts = VmCounts()
        counts.internalPages = UInt64(stats.internal_page_count)
        counts.purgeablePages = UInt64(stats.purgeable_count)
        counts.wiredPages = UInt64(stats.wire_count)
        counts.compressedPages = UInt64(stats.compressor_page_count)
        counts.pageSize = UInt64(pageSize)

        var ram = MemoryMath.ram(counts, totalBytes: UInt64(total))
        if let swap = Sysctl.value("vm.swapusage", as: xsw_usage.self) {
            ram.swapUsedGb = Double(swap.xsu_used) / gb
            ram.swapTotalGb = Double(swap.xsu_total) / gb
        }
        ram.pressure = MemoryMath.pressure(Sysctl.int("kern.memorystatus_vm_pressure_level"))
        return ram
    }
}
