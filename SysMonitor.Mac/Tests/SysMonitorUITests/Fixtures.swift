import Foundation
import SysMonitorCore
@testable import SysMonitorUI

/// A synthetic machine: eight cores, one internal SSD, one network share, one
/// Wi-Fi adapter. No real hardware is read.
func sampleSnapshot(cores: Int = 8, diskUsage: Int = 50) -> Snapshot {
    var s = Snapshot()
    s.ready = true
    s.cores = (0..<cores).map { Core(usage: $0 * 10) }
    s.cpuTotal = 42
    s.cpuInfo.name = "Apple M1"
    s.cpuInfo.logical = cores
    s.ram.usage = 61
    s.ram.usedGb = 4.9
    s.ram.totalGb = 8

    var ssd = PhysicalDevice(id: "disk0")
    ssd.media = "SSD"
    ssd.bus = "Apple Fabric"
    var system = Disk(mountPoint: "/")
    system.name = "Macintosh HD"
    system.usage = diskUsage
    system.usedGb = 250
    system.totalGb = 500
    system.readMb = 3
    system.writeMb = 1
    system.device = ssd
    var data = Disk(mountPoint: "/Volumes/Data")
    data.name = "Data"
    data.usage = 10
    data.usedGb = 50
    data.totalGb = 500
    data.readMb = 3
    data.writeMb = 1
    data.device = ssd
    var share = Disk(mountPoint: "/Volumes/Share")
    share.name = "Share"
    share.isNetwork = true
    share.usage = 95
    share.totalGb = 1000
    share.usedGb = 950
    s.disks = [system, data, share]

    var wifi = Adapter(id: "en0")
    wifi.name = "Wi-Fi"
    wifi.wireless = true
    wifi.speedMbps = 400
    wifi.downMb = 5
    wifi.upMb = 0
    s.adapters = [wifi]

    var gpu = Gpu()
    gpu.name = "Apple M1"
    gpu.cores = 7
    gpu.usage = 30
    gpu.rendererUsage = 29
    gpu.tilerUsage = 8
    gpu.memoryUsedGb = 0.8
    gpu.temp = 59
    s.cpuTemp = 82
    s.gpus = [gpu]
    return s
}

func temporaryDirectory() -> URL {
    let url = FileManager.default.temporaryDirectory
        .appendingPathComponent("sysmonitor-tests-\(UUID().uuidString)", isDirectory: true)
    try? FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    return url
}
