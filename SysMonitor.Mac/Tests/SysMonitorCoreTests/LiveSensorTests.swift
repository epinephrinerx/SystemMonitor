import Foundation
import Testing
@testable import SysMonitorCore

/// The real sensors on the machine running the tests. Passive reads only:
/// nothing here generates CPU, memory or disk load.
struct LiveSensorTests {
    @Test func onePairOfTicksPerLogicalProcessor() throws {
        let ticks = try #require(CpuSensor.readTicks())
        #expect(ticks.count == Sysctl.int("hw.logicalcpu"))
    }

    @Test func cpuInfoNamesTheChip() {
        let info = CpuSensor.info()
        #expect(!info.name.isEmpty)
        #expect(info.logical == ProcessInfo.processInfo.processorCount)
        #expect(info.performanceCores + info.efficiencyCores == 0
            || info.performanceCores + info.efficiencyCores == info.logical)
    }

    @Test func memoryTotalIsInstalledMemory() throws {
        let ram = try #require(MemorySensor.read())
        let installed = Double(ProcessInfo.processInfo.physicalMemory) / Double(1 << 30)
        #expect(ram.totalGb == installed)
        #expect(ram.usedGb > 0 && ram.usedGb <= ram.totalGb)
        #expect(ram.pressure != .unknown)
    }

    @Test func rootVolumeIsListedWithItsDevice() throws {
        let root = try #require(DiskSensor.volumes().first { $0.mountPoint == "/" })
        #expect(root.totalGb > 0)
        #expect(!root.bsdName.isEmpty)
        #expect(root.fileSystem == "apfs")

        let device = try #require(BlockStorage.device(forBSDName: root.bsdName))
        #expect(device.id.hasPrefix("disk"))
        #expect(device.driverID != 0)
        #expect(device.isInternal)
        #expect(BlockStorage.counters(driverID: device.driverID) != nil)
    }

    @Test func unknownDeviceIsNilNotACrash() {
        #expect(BlockStorage.device(forBSDName: "disk999") == nil)
        #expect(BlockStorage.device(forBSDName: "") == nil)
        #expect(BlockStorage.counters(driverID: 0) == nil)
    }

    @Test func loopbackHasCountersButIsNotAnAdapter() {
        #expect(NetworkSensor.counters()["lo0"] != nil)
        #expect(!NetworkSensor.hardware().contains { $0.bsdName == "lo0" })
    }

    @Test func samplerIsReadyAfterItsSecondTick() {
        let sampler = Sampler(speed: .fast)
        #expect(!sampler.sampleNow().ready)
        Thread.sleep(forTimeInterval: 0.5)
        let second = sampler.sampleNow()
        #expect(second.ready)
        #expect(second.cores.count == ProcessInfo.processInfo.processorCount)
        #expect(second.disks.contains { $0.mountPoint == "/" && $0.device != nil })
        #expect(sampler.current.ready)
    }
}
