import Foundation
import SysMonitorCore

// Reads the machine and prints what the sampler sees. Passive: it reads
// counters and generates no load of its own.
//
//   sysmon-probe                one snapshot, after a baseline tick
//   sysmon-probe --seconds 60   run the sampler for a minute, one line a tick

func gb(_ value: Double) -> String { String(format: "%.1f", value) }
func mb(_ value: Double) -> String { String(format: "%.2f", value) }

func describe(_ s: Snapshot) {
    let info = s.cpuInfo
    print("CPU   \(info.name) · \(info.architecture) · \(info.physicalCores) cores / \(info.logical) logical"
        + (info.performanceCores > 0 ? " (\(info.performanceCores)P + \(info.efficiencyCores)E)" : ""))
    print("      total \(s.cpuTotal)%  per core: \(s.cores.map { String($0.usage) }.joined(separator: " "))")

    let r = s.ram
    print("RAM   \(gb(r.usedGb)) / \(gb(r.totalGb)) GB (\(r.usage)%) · app \(gb(r.appGb)) · wired \(gb(r.wiredGb))"
        + " · compressed \(gb(r.compressedGb)) · swap \(gb(r.swapUsedGb))/\(gb(r.swapTotalGb)) · pressure \(r.pressure.rawValue)")

    for d in s.disks {
        print("DISK  \(d.title) [\(d.mountPoint)] \(d.fileSystem) \(d.bsdName)"
            + " · \(gb(d.usedGb)) / \(gb(d.totalGb)) GB (\(d.usage)%)"
            + " · R \(mb(d.readMb)) W \(mb(d.writeMb)) MB/s"
            + (d.isNetwork ? " · network" : "") + (d.isRemovable ? " · removable" : ""))
        if let dev = d.device {
            print("      on \(dev.id): \(dev.model.isEmpty ? "?" : dev.model) · \(dev.media)"
                + " · \(dev.bus.isEmpty ? "?" : dev.bus) · \(dev.isInternal ? "internal" : "external")"
                + (dev.revision.isEmpty ? "" : " · fw \(dev.revision)"))
        }
    }

    for g in s.gpus {
        print("GPU   \(g.name) · \(g.cores) cores · \(g.usage)%"
            + (g.rendererUsage.map { " · renderer \($0)%" } ?? "")
            + (g.tilerUsage.map { " · tiler \($0)%" } ?? "")
            + (g.memoryUsedGb.map { " · memory \(gb($0)) GB" } ?? ""))
    }

    if s.adapters.isEmpty {
        print("NET   (no connected Wi-Fi or Ethernet)")
    }
    for a in s.adapters {
        print("NET   \(a.name) [\(a.id)] \(a.wireless ? "wireless" : "wired")"
            + " · link \(a.speedMbps > 0 ? String(format: "%.0f Mbps", a.speedMbps) : "?")"
            + " · down \(mb(a.downMb)) up \(mb(a.upMb)) MB/s (\(a.usage)%)")
    }
    print("ready \(s.ready)")
}

let arguments = CommandLine.arguments
let seconds = arguments.firstIndex(of: "--seconds")
    .flatMap { arguments.indices.contains($0 + 1) ? Double(arguments[$0 + 1]) : nil }

let sampler = Sampler(speed: .balanced)

if let seconds {
    let started = Date()
    sampler.onPublish = { s in
        let elapsed = Date().timeIntervalSince(started)
        let disk = s.disks.first.map { "disk R \(mb($0.readMb)) W \(mb($0.writeMb))" } ?? "no disk"
        let net = s.adapters.first.map { "net ↓\(mb($0.downMb)) ↑\(mb($0.upMb))" } ?? "no net"
        print(String(format: "%6.1fs", elapsed)
            + "  cpu \(s.cpuTotal)%  ram \(s.ram.usage)%  \(disk)  \(net)  ready \(s.ready)")
    }
    sampler.start()
    Thread.sleep(forTimeInterval: seconds)
    sampler.stop()
    print("")
    describe(sampler.current)
} else {
    sampler.sampleNow()
    Thread.sleep(forTimeInterval: RefreshSpeed.balanced.metrics)
    describe(sampler.sampleNow())
}
