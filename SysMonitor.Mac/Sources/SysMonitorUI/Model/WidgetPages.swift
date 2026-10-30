import Foundation
import SysMonitorCore

public enum Format {
    /// Rates: whole numbers from 100 up, one decimal below.
    public static func rate(_ mb: Double) -> String {
        mb >= 100 ? String(format: "%.0f", mb) : String(format: "%.1f", mb)
    }

    public static func gb(_ value: Double) -> String { String(format: "%.1f", value) }

    public static func linkSpeed(_ mbps: Double) -> String {
        mbps >= 1000 ? String(format: "%.1f Gbps", mbps / 1000) : String(format: "%.0f Mbps", mbps)
    }
}

/// One bar on a widget page.
public struct WidgetBar: Equatable, Sendable {
    public var label: String
    public var percent: Int
    public var accent: Accent
}

/// What a page's temperature badge shows: none at all (network), or a reading
/// that may be missing (`n/a`).
public enum TempDisplay: Equatable, Sendable {
    case none
    case reading(Int?)
}

/// One page of the mini widget, ready to draw.
public struct WidgetPage: Equatable, Sendable {
    public enum Kind: Equatable, Sendable {
        case message, cpuTotal, cpuBlock(Int), ram, diskAll, disk(String), gpu(Int), netAll, net(String)
    }

    public var kind: Kind
    public var title: String
    public var headline: Int?
    public var bars: [WidgetBar] = []
    public var columns = 1
    public var detail = ""
    public var alert: DiskLevel = .ok
    public var temp: TempDisplay = .none
}

/// Which pages the widget cycles through, from a snapshot and the settings.
/// The same pages as the Windows build, in the same order: CPU (a page per
/// four cores, or one), RAM, Disk (a page per drive, or one), Network (a page
/// per adapter, or one).
public enum WidgetPages {
    public static let coresPerPage = 4

    public static func build(_ s: Snapshot, _ config: AppConfig, _ t: Strings,
                             alerts: [String: DiskLevel] = [:]) -> [WidgetPage] {
        guard s.ready else {
            return [WidgetPage(kind: .message, title: t("connecting"))]
        }
        var pages: [WidgetPage] = []

        if config.showCpu {
            let temp = TempDisplay.reading(s.cpuTemp)
            if config.cpuMode == "total" || s.cores.isEmpty {
                pages.append(WidgetPage(kind: .cpuTotal, title: "\(t("cpu")) (\(t("total")))",
                                        headline: s.cpuTotal,
                                        bars: [WidgetBar(label: "", percent: s.cpuTotal, accent: .cpu)],
                                        detail: "\(s.cores.count) \(t("cores"))", temp: temp))
            } else {
                for start in stride(from: 0, to: s.cores.count, by: coresPerPage) {
                    let end = min(start + coresPerPage, s.cores.count)
                    let bars = (start..<end).map {
                        WidgetBar(label: "C\($0)", percent: s.cores[$0].usage, accent: .cpu)
                    }
                    pages.append(WidgetPage(kind: .cpuBlock(start), title: "\(t("cpu"))  C\(start)-\(end - 1)",
                                            headline: s.cpuTotal, bars: bars,
                                            columns: bars.count > 2 ? 2 : 1, temp: temp))
                }
            }
        }

        if config.showRam {
            let r = s.ram
            pages.append(WidgetPage(kind: .ram, title: t("memory"), headline: r.usage,
                                    bars: [WidgetBar(label: "", percent: r.usage, accent: .ram)],
                                    detail: "\(Format.gb(r.usedGb)) / \(Format.gb(r.totalGb)) GB \(t("in_use"))"
                                        + (ProcessGroups.topLine(s.processes, t).map { "  ·  \($0)" } ?? ""),
                                    temp: .reading(nil)))
        }

        if config.showDisk {
            let disks = visibleDisks(s, config)
            if config.diskMode == "total" {
                if !disks.isEmpty {
                    let total = disks.reduce(0) { $0 + $1.totalGb }
                    let used = disks.reduce(0) { $0 + $1.usedGb }
                    let usage = total > 0 ? Int((used * 100 / total).rounded()) : 0
                    let rate = combinedRate(disks)
                    pages.append(WidgetPage(kind: .diskAll, title: "\(t("disk")) (\(t("all_drives")))",
                                            headline: usage,
                                            bars: [WidgetBar(label: "", percent: usage, accent: .disk)],
                                            detail: rateText(rate.read, rate.write, t),
                                            temp: .reading(disks.compactMap(\.temp).max())))
                }
            } else {
                for d in disks {
                    let level = alerts[d.id] ?? .ok
                    let detail = level == .ok
                        ? rateText(d.readMb, d.writeMb, t)
                        : t(level == .hot ? "disk_hot" : "disk_warn", d.title, String(d.usage))
                    pages.append(WidgetPage(kind: .disk(d.id), title: "\(t("drive")) \(d.title)",
                                            headline: d.usage,
                                            bars: [WidgetBar(label: "", percent: d.usage, accent: .disk)],
                                            detail: detail, alert: level, temp: .reading(d.temp)))
                }
            }
        }

        if config.showGpu {
            for (i, g) in s.gpus.enumerated() {
                pages.append(WidgetPage(kind: .gpu(i), title: s.gpus.count > 1 ? "\(t("gpu")) \(i + 1)" : t("gpu"),
                                        headline: g.usage,
                                        bars: [WidgetBar(label: "", percent: g.usage, accent: .gpu)],
                                        detail: gpuDetail(g, t)))
            }
        }

        if config.showNetwork, !s.adapters.isEmpty {
            if config.networkMode == "total" {
                let usage = combinedUsage(s.adapters)
                let down = s.adapters.reduce(0) { $0 + $1.downMb }
                let up = s.adapters.reduce(0) { $0 + $1.upMb }
                pages.append(WidgetPage(kind: .netAll, title: "\(t("network")) (\(t("all_adapters")))",
                                        headline: usage,
                                        bars: [WidgetBar(label: "", percent: usage, accent: .net)],
                                        detail: netText(down, up)))
            } else {
                for a in s.adapters {
                    pages.append(WidgetPage(kind: .net(a.id), title: a.name, headline: a.usage,
                                            bars: [WidgetBar(label: "", percent: a.usage, accent: .net)],
                                            detail: netText(a.downMb, a.upMb)))
                }
            }
        }

        return pages.isEmpty ? [WidgetPage(kind: .message, title: t("no_data"))] : pages
    }

    /// "Apple M1 · 7 cores · 0.8 GB memory in use", leaving out what the
    /// driver does not report.
    public static func gpuDetail(_ g: Gpu, _ t: Strings) -> String {
        var parts: [String] = []
        if !g.name.isEmpty { parts.append(g.name) }
        if g.cores > 0 { parts.append("\(g.cores) \(t("cores"))") }
        if let memory = g.memoryUsedGb { parts.append(t("gpu_memory", Format.gb(memory))) }
        return parts.joined(separator: " · ")
    }

    public static func visibleDisks(_ s: Snapshot, _ config: AppConfig) -> [Disk] {
        config.includeNetworkDrives ? s.disks : s.disks.filter { !$0.isNetwork }
    }

    /// I/O is counted per physical device, and several volumes can sit on one
    /// device, so a sum over volumes would count the same bytes twice.
    public static func combinedRate(_ disks: [Disk]) -> (read: Double, write: Double) {
        var seen = Set<String>()
        var read = 0.0, write = 0.0
        for d in disks {
            let key = d.device?.id ?? d.id
            guard seen.insert(key).inserted else { continue }
            read += d.readMb
            write += d.writeMb
        }
        return (read, write)
    }

    /// Combined throughput against combined link speed.
    public static func combinedUsage(_ adapters: [Adapter]) -> Int {
        let speed = adapters.reduce(0) { $0 + $1.speedMbps }
        guard speed > 0 else { return 0 }
        let mbps = adapters.reduce(0) { $0 + ($1.downMb + $1.upMb) * 8 }
        return Int(min(max((mbps / speed * 100).rounded(), 0), 100))
    }

    public static func rateText(_ read: Double, _ write: Double, _ t: Strings) -> String {
        "\(t("read")) \(Format.rate(read)) | \(t("write")) \(Format.rate(write)) MB/s"
    }

    public static func netText(_ down: Double, _ up: Double) -> String {
        "↓ \(Format.rate(down)) | ↑ \(Format.rate(up)) MB/s"
    }
}
