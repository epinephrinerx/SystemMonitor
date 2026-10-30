import Testing
@testable import SysMonitorCore

struct CpuMathTests {
    @Test func busyShareOfTicks() {
        let a = CpuTicks(user: 100, system: 50, idle: 800, nice: 50)
        let b = CpuTicks(user: 160, system: 70, idle: 900, nice: 70)
        // busy 60 + 20 + 20 = 100 of 200
        #expect(CpuMath.usage(from: a, to: b) == 50)
    }

    @Test func counterWrapIsNotANegativeLoad() {
        let a = CpuTicks(user: UInt32.max - 9, system: 0, idle: UInt32.max - 29, nice: 0)
        let b = CpuTicks(user: 10, system: 0, idle: 30, nice: 0)
        // 20 busy ticks across the wrap, 60 idle
        #expect(CpuMath.usage(from: a, to: b) == 25)
    }

    @Test func noTicksIsIdleNotADivisionByZero() {
        let a = CpuTicks(user: 5, system: 5, idle: 5, nice: 5)
        #expect(CpuMath.usage(from: a, to: a) == 0)
        #expect(CpuMath.total(from: [a], to: [a]) == 0)
    }

    @Test func totalWeighsCoresByTicksNotByCount() {
        let idleBefore = CpuTicks(user: 0, system: 0, idle: 0, nice: 0)
        let busyCore = CpuTicks(user: 300, system: 0, idle: 0, nice: 0)
        let idleCore = CpuTicks(user: 0, system: 0, idle: 100, nice: 0)
        // 300 busy of 400: not the 50% an average of 100% and 0% would give
        #expect(CpuMath.total(from: [idleBefore, idleBefore], to: [busyCore, idleCore]) == 75)
    }

    @Test func neverAboveOneHundred() {
        let a = CpuTicks(user: 0, system: 0, idle: 0, nice: 0)
        let b = CpuTicks(user: 10, system: 10, idle: 0, nice: 10)
        #expect(CpuMath.usage(from: a, to: b) == 100)
    }
}

struct MemoryMathTests {
    private func counts(anonymous: UInt64, purgeable: UInt64 = 0, wired: UInt64 = 0,
                        compressed: UInt64 = 0) -> VmCounts {
        var c = VmCounts()
        c.internalPages = anonymous
        c.purgeablePages = purgeable
        c.wiredPages = wired
        c.compressedPages = compressed
        c.pageSize = 16384
        return c
    }

    @Test func usedIsAppPlusWiredPlusCompressed() {
        let gib: UInt64 = 1 << 30
        let pagesPerGib = gib / 16384
        let ram = MemoryMath.ram(
            counts(anonymous: 3 * pagesPerGib, purgeable: pagesPerGib, wired: pagesPerGib, compressed: pagesPerGib),
            totalBytes: 8 * gib)
        #expect(ram.appGb == 2)
        #expect(ram.wiredGb == 1)
        #expect(ram.compressedGb == 1)
        #expect(ram.usedGb == 4)
        #expect(ram.totalGb == 8)
        #expect(ram.usage == 50)
    }

    @Test func purgeableBeyondInternalDoesNotUnderflow() {
        let ram = MemoryMath.ram(counts(anonymous: 10, purgeable: 50), totalBytes: 1 << 30)
        #expect(ram.appGb == 0)
    }

    @Test func usedNeverExceedsInstalled() {
        let ram = MemoryMath.ram(counts(anonymous: 1_000_000), totalBytes: 1 << 30)
        #expect(ram.usage == 100)
        #expect(ram.usedGb == 1)
    }

    @Test func pressureLevels() {
        #expect(MemoryMath.pressure(1) == .normal)
        #expect(MemoryMath.pressure(2) == .warning)
        #expect(MemoryMath.pressure(4) == .critical)
        #expect(MemoryMath.pressure(nil) == .unknown)
        #expect(MemoryMath.pressure(3) == .unknown)
    }
}

struct IoRateTests {
    private let mb: UInt64 = 1024 * 1024

    @Test func rateIsOverTheDevicesOwnElapsedTime() {
        // 300 MB accumulated while the probe was shelved for 300 s is
        // 1 MB/s, not 300.
        let rate = IoRate.perSecond(previous: (0, 0), previousStamp: 0,
                                    current: (300 * mb, 150 * mb), stamp: 300_000)
        #expect(rate.readMb == 1)
        #expect(rate.writeMb == 0.5)
    }

    @Test func counterResetIsNotANegativeRate() {
        let rate = IoRate.perSecond(previous: (500 * mb, 500 * mb), previousStamp: 1000,
                                    current: (10 * mb, 600 * mb), stamp: 2000)
        #expect(rate.readMb == 0)
        #expect(rate.writeMb == 100)
    }

    @Test func noElapsedTimeIsNoRate() {
        let rate = IoRate.perSecond(previous: (0, 0), previousStamp: 5000,
                                    current: (mb, mb), stamp: 5000)
        #expect(rate.readMb == 0 && rate.writeMb == 0)
    }
}

struct HistoryTests {
    @Test func readsOldestFirst() {
        let h = History(capacity: 4)
        [1.0, 2, 3].forEach(h.add)
        var buffer = [Double](repeating: -1, count: 4)
        #expect(h.copy(into: &buffer) == 3)
        #expect(Array(buffer.prefix(3)) == [1, 2, 3])
        #expect(h.latest == 3)
    }

    @Test func wrapsManyTimesOver() {
        let h = History(capacity: 3)
        (1...10).forEach { h.add(Double($0)) }
        var buffer = [Double](repeating: 0, count: 3)
        #expect(h.copy(into: &buffer) == 3)
        #expect(buffer == [8, 9, 10])
        #expect(h.count == 3)
        #expect(h.revision == 10)
    }

    @Test func smallerBufferGetsTheNewest() {
        let h = History(capacity: 5)
        (1...5).forEach { h.add(Double($0)) }
        var buffer = [Double](repeating: 0, count: 2)
        #expect(h.copy(into: &buffer) == 2)
        #expect(buffer == [4, 5])
    }

    @Test func maxCoversOnlyWhatIsStillHeld() {
        let h = History(capacity: 2)
        [9.0, 1, 2].forEach(h.add)
        #expect(h.max == 2)
    }

    @Test func emptyHistoryReadsZero() {
        let h = History(capacity: 2)
        #expect(h.latest == 0)
        #expect(h.max == 0)
    }
}

struct ModelTests {
    @Test func adapterUsageIsBothDirectionsAgainstLinkRate() {
        var a = Adapter(id: "en0")
        a.speedMbps = 100
        a.downMb = 5
        a.upMb = 1.25
        // 6.25 MB/s = 50 Mbps of 100
        #expect(a.usage == 50)
    }

    @Test func adapterWithoutLinkSpeedHasNoPercentage() {
        var a = Adapter(id: "en0")
        a.downMb = 50
        #expect(a.usage == 0)
    }

    @Test func adapterUsageIsCapped() {
        var a = Adapter(id: "en0")
        a.speedMbps = 10
        a.downMb = 100
        #expect(a.usage == 100)
    }

    @Test func diskSpaceInDecimalGigabytes() {
        let d = DiskSensor.space(Disk(mountPoint: "/"), total: 500_000_000_000, available: 125_000_000_000)
        #expect(d.totalGb == 500)
        #expect(d.usedGb == 375)
        #expect(d.usage == 75)
    }

    @Test func availableBeyondTotalIsAnEmptyVolume() {
        let d = DiskSensor.space(Disk(mountPoint: "/"), total: 100, available: 150)
        #expect(d.usage == 0)
        #expect(d.usedGb == 0)
    }

    @Test func diskTitleFallsBackToMountPoint() {
        var d = Disk(mountPoint: "/Volumes/X")
        #expect(d.title == "/Volumes/X")
        d.name = "Backup"
        #expect(d.title == "Backup")
    }

    @Test func mediaAndBusNames() {
        #expect(BlockStorage.media("Solid State") == "SSD")
        #expect(BlockStorage.media("Rotational") == "HDD")
        #expect(BlockStorage.media(nil) == "Disk")
        #expect(BlockStorage.bus("PCI-Express") == "PCIe")
        #expect(BlockStorage.bus("Virtual Interface") == "Disk Image")
        #expect(BlockStorage.bus("USB") == "USB")
        #expect(BlockStorage.bus(nil) == "")
    }

    @Test func cadencesMatchTheWindowsBuild() {
        #expect(RefreshSpeed.eco.metrics == 5 && RefreshSpeed.eco.space == 30 && RefreshSpeed.eco.temps == 60)
        #expect(RefreshSpeed.balanced.metrics == 2.5 && RefreshSpeed.balanced.space == 15)
        #expect(RefreshSpeed.fast.metrics == 1 && RefreshSpeed.fast.space == 10)
    }
}
