import Foundation
import SysMonitorCore

/// A name and a value in a tab's facts box.
public struct Fact: Equatable, Sendable {
    public var name: String
    public var value: String
}

public enum ChartUnit: Equatable, Sendable {
    /// A fixed ceiling of 100%.
    case percent
    /// No ceiling: the graph scales to its own peak and says what that is.
    case megabytesPerSecond
    /// Memory, scaled to its own peak like a rate.
    case megabytes
}

/// One line on a graph: which history, what to call it, what colour.
public struct SeriesSpec: Equatable, Sendable {
    public var key: String
    public var label: String
    public var color: Rgb
}

public struct ChartSpec: Equatable, Sendable, Identifiable {
    public var id: String { series.map(\.key).joined(separator: "+") }
    public var title: String
    public var unit: ChartUnit
    public var series: [SeriesSpec]
}

/// One tab of the Full view: a device kind, or a single physical disk or
/// adapter, with what the rail says about it and what the panel shows.
public struct FullTab: Equatable, Sendable, Identifiable {
    public var id: String
    public var title: String
    /// Under the title on the rail: what it is, "SSD · Apple Fabric".
    public var detail: String
    /// The figure on the rail, so the tabs not selected still report.
    public var summary: String
    /// The model line heading the panel.
    public var hardware: String
    public var facts: [Fact] = []
    public var charts: [ChartSpec] = []
    /// A grid of smaller graphs under the main ones: cores, GPU stages,
    /// volumes.
    public var smallCharts: [ChartSpec] = []
}

/// Which tabs the Full view has, in the Overall view's order: CPU, Memory,
/// a disk per physical device, GPU, an adapter each.
public enum FullTabs {
    /// The read line is the disk's green; writes need a second colour that is
    /// not a traffic light.
    static let writeColor = Rgb(0x8b5cf6)
    static let upColor = Rgb(0xf472b6)

    public static func build(_ s: Snapshot, _ t: Strings) -> [FullTab] {
        guard s.ready else { return [] }
        var tabs: [FullTab] = [cpu(s, t), memory(s, t)]
        tabs += disks(s, t)
        for (i, g) in s.gpus.enumerated() {
            tabs.append(gpu(g, i, count: s.gpus.count, t))
        }
        tabs += s.adapters.map { adapter($0, t) }
        return tabs
    }

    static func cpu(_ s: Snapshot, _ t: Strings) -> FullTab {
        let info = s.cpuInfo
        var facts = [Fact(name: t("chip"), value: info.name)]
        if info.performanceCores > 0 {
            facts.append(Fact(name: t("performance_cores"), value: "\(info.performanceCores)"))
            facts.append(Fact(name: t("efficiency_cores"), value: "\(info.efficiencyCores)"))
        } else if info.physicalCores > 0 {
            facts.append(Fact(name: t("cores_label"), value: "\(info.physicalCores)"))
        }
        facts.append(Fact(name: t("logical_processors"), value: "\(info.logical)"))
        facts.append(Fact(name: t("architecture"), value: info.architecture))
        facts.append(Fact(name: t("temperature"), value: s.cpuTemp.map { "\($0)°C" } ?? "n/a"))

        return FullTab(
            id: "cpu", title: t("cpu"), detail: "\(s.cores.count) \(t("cores"))", summary: "\(s.cpuTotal)%",
            hardware: info.name, facts: facts,
            charts: [ChartSpec(title: t("utilization"), unit: .percent,
                               series: [SeriesSpec(key: "cpu", label: t("cpu"), color: Accent.cpu.rgb)])],
            smallCharts: s.cores.indices.map {
                ChartSpec(title: "C\($0)", unit: .percent,
                          series: [SeriesSpec(key: "core\($0)", label: "C\($0)", color: Accent.cpu.rgb)])
            })
    }

    static func memory(_ s: Snapshot, _ t: Strings) -> FullTab {
        let r = s.ram
        let facts = [
            Fact(name: t("in_use_label"), value: "\(Format.gb(r.usedGb)) / \(Format.gb(r.totalGb)) GB"),
            Fact(name: t("app_memory"), value: "\(Format.gb(r.appGb)) GB"),
            Fact(name: t("wired_memory"), value: "\(Format.gb(r.wiredGb)) GB"),
            Fact(name: t("compressed"), value: "\(Format.gb(r.compressedGb)) GB"),
            Fact(name: t("swap_label"), value: "\(Format.gb(r.swapUsedGb)) / \(Format.gb(r.swapTotalGb)) GB"),
            Fact(name: t("pressure"), value: t("pressure_\(r.pressure.rawValue)")),
        ]
        return FullTab(
            id: "ram", title: t("memory"), detail: "\(Format.gb(r.totalGb)) GB", summary: "\(r.usage)%",
            hardware: "\(Format.gb(r.totalGb)) GB", facts: facts,
            charts: [ChartSpec(title: t("utilization"), unit: .percent,
                               series: [SeriesSpec(key: "ram", label: t("memory"), color: Accent.ram.rgb)])])
    }

    /// One tab per physical disk, holding every volume on it. A network share
    /// has no device and gets a tab of its own.
    static func disks(_ s: Snapshot, _ t: Strings) -> [FullTab] {
        var order: [String] = []
        var groups: [String: [Disk]] = [:]
        for d in s.disks {
            let key = d.device?.id ?? d.id
            if groups[key] == nil { order.append(key) }
            groups[key, default: []].append(d)
        }
        return order.map { key in
            let volumes = groups[key]!
            guard let device = volumes[0].device else { return share(volumes[0], t) }
            return physical(device, volumes, t)
        }
    }

    private static func physical(_ device: PhysicalDevice, _ volumes: [Disk], _ t: Strings) -> FullTab {
        let number = device.id.hasPrefix("disk") ? String(device.id.dropFirst(4)) : device.id
        let kind = [device.media, device.bus].filter { !$0.isEmpty }.joined(separator: " · ")
        let read = volumes[0].readMb, write = volumes[0].writeMb

        var facts = [
            Fact(name: t("model"), value: device.model.isEmpty ? "—" : device.model),
            Fact(name: t("media"), value: device.media),
            Fact(name: t("interface"), value: device.bus.isEmpty ? "—" : device.bus),
            Fact(name: t("location"), value: t(device.isInternal ? "internal" : "external")),
            Fact(name: t("bsd_name"), value: device.id),
        ]
        if !device.serial.isEmpty { facts.append(Fact(name: t("serial"), value: device.serial)) }
        if !device.revision.isEmpty { facts.append(Fact(name: t("firmware"), value: device.revision)) }
        for v in volumes {
            facts.append(Fact(name: v.title,
                              value: "\(Format.gb(v.usedGb)) / \(Format.gb(v.totalGb)) GB (\(v.usage)%)  ·  \(v.fileSystem)  ·  \(v.mountPoint)"))
        }

        return FullTab(
            id: "disk.\(device.id)", title: "\(t("disk_n")) \(number)",
            detail: ([kind] + volumes.map(\.title)).filter { !$0.isEmpty }.joined(separator: " · "),
            summary: "\(Format.rate(read + write)) MB/s",
            hardware: device.model, facts: facts,
            charts: [ChartSpec(title: t("activity"), unit: .megabytesPerSecond, series: [
                SeriesSpec(key: HistoryStore.Key.read(device.id), label: t("read_long"), color: Accent.disk.rgb),
                SeriesSpec(key: HistoryStore.Key.write(device.id), label: t("write_long"), color: writeColor),
            ])],
            smallCharts: volumes.map {
                ChartSpec(title: "\($0.title) · \(t("space_used"))", unit: .percent,
                          series: [SeriesSpec(key: HistoryStore.Key.used($0.id), label: $0.title, color: Accent.disk.rgb)])
            })
    }

    private static func share(_ v: Disk, _ t: Strings) -> FullTab {
        FullTab(
            id: "share.\(v.id)", title: v.title, detail: t("network"), summary: "\(v.usage)%",
            hardware: v.mountPoint,
            facts: [
                Fact(name: t("in_use_label"), value: "\(Format.gb(v.usedGb)) / \(Format.gb(v.totalGb)) GB (\(v.usage)%)"),
                Fact(name: t("file_system"), value: v.fileSystem),
                Fact(name: t("mount_point"), value: v.mountPoint),
            ],
            charts: [ChartSpec(title: t("space_used"), unit: .percent,
                               series: [SeriesSpec(key: HistoryStore.Key.used(v.id), label: v.title, color: Accent.disk.rgb)])])
    }

    static func gpu(_ g: Gpu, _ i: Int, count: Int, _ t: Strings) -> FullTab {
        var facts = [Fact(name: t("model"), value: g.name.isEmpty ? "—" : g.name)]
        if g.cores > 0 { facts.append(Fact(name: t("cores_label"), value: "\(g.cores)")) }
        if let memory = g.memoryUsedGb { facts.append(Fact(name: t("gpu_memory_label"), value: "\(Format.gb(memory)) GB")) }

        var small: [ChartSpec] = []
        if g.rendererUsage != nil {
            small.append(ChartSpec(title: t("renderer"), unit: .percent,
                                   series: [SeriesSpec(key: "gpu\(i).renderer", label: t("renderer"), color: Accent.gpu.rgb)]))
        }
        if g.tilerUsage != nil {
            small.append(ChartSpec(title: t("tiler"), unit: .percent,
                                   series: [SeriesSpec(key: "gpu\(i).tiler", label: t("tiler"), color: Accent.gpu.rgb)]))
        }
        return FullTab(
            id: "gpu\(i)", title: count > 1 ? "\(t("gpu")) \(i + 1)" : t("gpu"), detail: g.name,
            summary: "\(g.usage)%", hardware: g.name, facts: facts,
            charts: [ChartSpec(title: t("utilization"), unit: .percent,
                               series: [SeriesSpec(key: "gpu\(i)", label: t("gpu"), color: Accent.gpu.rgb)])],
            smallCharts: small)
    }

    static func adapter(_ a: Adapter, _ t: Strings) -> FullTab {
        let kind = t(a.wireless ? "wireless" : "wired")
        let facts = [
            Fact(name: t("type"), value: kind),
            Fact(name: t("link_speed"), value: a.speedMbps > 0 ? Format.linkSpeed(a.speedMbps) : t("no_link")),
            Fact(name: t("bsd_name"), value: a.id),
            Fact(name: t("received"), value: total(a.totalReceived)),
            Fact(name: t("sent"), value: total(a.totalSent)),
        ]
        return FullTab(
            id: "net.\(a.id)", title: a.name, detail: kind,
            summary: "\(Format.rate(a.downMb + a.upMb)) MB/s", hardware: a.id, facts: facts,
            charts: [ChartSpec(title: t("throughput"), unit: .megabytesPerSecond, series: [
                SeriesSpec(key: HistoryStore.Key.down(a.id), label: t("receive"), color: Accent.net.rgb),
                SeriesSpec(key: HistoryStore.Key.up(a.id), label: t("send"), color: upColor),
            ])])
    }

    /// Lifetime byte counts, in the largest unit that keeps them readable.
    static func total(_ bytes: UInt64) -> String {
        let gb = Double(bytes) / 1e9
        return gb >= 1 ? String(format: "%.2f GB", gb) : String(format: "%.1f MB", Double(bytes) / 1e6)
    }
}

/// The arithmetic of drawing a graph, apart from drawing it.
public enum ChartMath {
    /// The smallest ceiling a throughput graph uses, so an idle link does not
    /// stretch its noise to fill the box.
    public static let minimumRate = 0.1

    public static func ceiling(_ unit: ChartUnit, peak: Double) -> Double {
        switch unit {
        case .percent: return 100
        case .megabytesPerSecond: return max(peak, minimumRate)
        case .megabytes: return max(peak, 1)
        }
    }

    public static func ceilingLabel(_ unit: ChartUnit, peak: Double) -> String {
        switch unit {
        case .percent: return "100%"
        case .megabytesPerSecond: return "\(Format.rate(ceiling(unit, peak: peak))) MB/s"
        case .megabytes: return ProcessGroups.megabytes(ceiling(unit, peak: peak))
        }
    }

    /// Where each reading goes, newest at the right edge and older ones
    /// spaced to the left as if the history were full, so a graph that has
    /// only just started fills in from the right the way Task Manager's does.
    public static func points(_ values: ArraySlice<Double>, capacity: Int, ceiling: Double,
                              size: CGSize) -> [CGPoint] {
        guard capacity > 1, ceiling > 0 else { return [] }
        let step = size.width / CGFloat(capacity - 1)
        let start = capacity - values.count
        return values.enumerated().map { i, value in
            let fraction = min(max(value / ceiling, 0), 1)
            return CGPoint(x: CGFloat(start + i) * step, y: size.height * (1 - CGFloat(fraction)))
        }
    }
}
