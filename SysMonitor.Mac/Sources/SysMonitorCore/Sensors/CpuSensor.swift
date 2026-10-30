import Darwin

/// One processor's lifetime tick counters, as the kernel keeps them.
package struct CpuTicks: Equatable, Sendable {
    package var user: UInt32
    package var system: UInt32
    package var idle: UInt32
    package var nice: UInt32

    package init(user: UInt32, system: UInt32, idle: UInt32, nice: UInt32) {
        self.user = user
        self.system = system
        self.idle = idle
        self.nice = nice
    }
}

package enum CpuMath {
    /// How busy a processor was between two readings, 0...100.
    ///
    /// The counters are 32-bit and wrap after a few months of uptime per
    /// core, so the deltas use wrapping subtraction: one wrap between two
    /// readings a few seconds apart still gives the right answer.
    package static func usage(from a: CpuTicks, to b: CpuTicks) -> Int {
        let (busy, total) = delta(a, b)
        return percent(busy, total)
    }

    /// The whole machine: every processor's ticks summed before dividing, so
    /// the total weighs each core by the time it actually reported.
    package static func total(from a: [CpuTicks], to b: [CpuTicks]) -> Int {
        var busy: UInt64 = 0
        var total: UInt64 = 0
        for (x, y) in zip(a, b) {
            let d = delta(x, y)
            busy += d.busy
            total += d.total
        }
        return percent(busy, total)
    }

    private static func delta(_ a: CpuTicks, _ b: CpuTicks) -> (busy: UInt64, total: UInt64) {
        let busy = UInt64(b.user &- a.user) + UInt64(b.system &- a.system) + UInt64(b.nice &- a.nice)
        return (busy, busy + UInt64(b.idle &- a.idle))
    }

    private static func percent(_ busy: UInt64, _ total: UInt64) -> Int {
        guard total > 0 else { return 0 }
        return min(100, Int((Double(busy) * 100 / Double(total)).rounded()))
    }
}

package enum CpuSensor {
    /// `mach_host_self` hands out a new send right on every call; one is enough.
    private static let host = mach_host_self()

    package static func readTicks() -> [CpuTicks]? {
        var processors: natural_t = 0
        var info: processor_info_array_t?
        var infoCount: mach_msg_type_number_t = 0
        guard host_processor_info(host, PROCESSOR_CPU_LOAD_INFO,
                                  &processors, &info, &infoCount) == KERN_SUCCESS,
              let info else { return nil }
        defer {
            vm_deallocate(mach_task_self_, vm_address_t(UInt(bitPattern: info)),
                          vm_size_t(Int(infoCount) * MemoryLayout<integer_t>.stride))
        }

        var ticks: [CpuTicks] = []
        ticks.reserveCapacity(Int(processors))
        for cpu in 0..<Int(processors) {
            let base = cpu * Int(CPU_STATE_MAX)
            func read(_ state: Int32) -> UInt32 { UInt32(bitPattern: info[base + Int(state)]) }
            ticks.append(CpuTicks(user: read(CPU_STATE_USER), system: read(CPU_STATE_SYSTEM),
                                  idle: read(CPU_STATE_IDLE), nice: read(CPU_STATE_NICE)))
        }
        return ticks
    }

    package static func info() -> CpuInfo {
        var info = CpuInfo()
        info.name = Sysctl.string("machdep.cpu.brand_string") ?? ""
        info.physicalCores = Sysctl.int("hw.physicalcpu") ?? 0
        info.logical = Sysctl.int("hw.logicalcpu") ?? 0
        #if arch(arm64)
        info.architecture = "arm64"
        #else
        info.architecture = "x86_64"
        #endif
        // perflevel0 is the fastest level. A machine with a single level has
        // no split worth reporting.
        if (Sysctl.int("hw.nperflevels") ?? 1) > 1 {
            info.performanceCores = Sysctl.int("hw.perflevel0.logicalcpu") ?? 0
            info.efficiencyCores = Sysctl.int("hw.perflevel1.logicalcpu") ?? 0
        }
        return info
    }
}
