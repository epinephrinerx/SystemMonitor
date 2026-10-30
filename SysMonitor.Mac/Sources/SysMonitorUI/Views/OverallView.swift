import SwiftUI
import SysMonitorCore

/// The expanded view: a fixed sidebar of settings and a card per device kind.
/// Everything scales with the window (R-009); the sidebar's width scales with
/// it but cannot be dragged (R-008).
public struct OverallView: View {
    @ObservedObject var model: AppModel
    let actions: WindowActions?

    /// False only for offscreen rendering: `ImageRenderer` draws nothing
    /// inside a scroll view, so the tests lay the content out flat.
    var scrollable = true

    public init(model: AppModel, actions: WindowActions?) {
        self.model = model
        self.actions = actions
    }

    public var body: some View {
        GeometryReader { geo in
            let m = Metrics(window: Metrics.overallScale(geo.size), font: model.config.fontScale,
                            text: Metrics.overallText)
            let theme = model.theme
            let radius = m.len(14)

            ZStack {
                PanelBackground(theme: theme, opacity: model.config.opacity, radius: radius)
                HStack(spacing: 0) {
                    Sidebar(model: model, actions: actions, metrics: m, scrollable: scrollable)
                        .frame(width: m.len(196))
                        .background(theme.sidebar.color.opacity(model.config.opacity))
                    content(m, theme, model.strings)
                }
                .clipShape(RoundedRectangle(cornerRadius: radius, style: .continuous))
                ResizeBands(actions: actions)
            }
            .contentShape(Rectangle())
            .windowDrag(actions) { actions?.show(ViewMode.overall.next) }
            .contextMenu { WindowMenu(model: model, actions: actions) }
        }
    }

    private func content(_ m: Metrics, _ theme: Theme, _ t: Strings) -> some View {
        VStack(spacing: m.len(6)) {
            HStack(spacing: m.len(4)) {
                Text(t("title"))
                    .font(.system(size: m.type(Metrics.Font.heading), weight: .semibold))
                    .foregroundStyle(theme.text.color)
                Spacer()
                IconButton(systemName: "chart.xyaxis.line", help: t("full_data"),
                           size: m.len(26), theme: theme) { actions?.show(.full) }
                IconButton(systemName: "rectangle.compress.vertical", help: t("open_widget"),
                           size: m.len(26), theme: theme) { actions?.show(.widget) }
                IconButton(systemName: "xmark", help: t("close"), size: m.len(26), theme: theme) {
                    actions?.closePressed()
                }
            }
            Scrolling(enabled: scrollable) {
                VStack(alignment: .leading, spacing: m.len(10)) {
                    if !model.snapshot.ready {
                        Text(t("connecting")).foregroundStyle(theme.muted.color)
                            .font(.system(size: m.type(Metrics.Font.body)))
                    } else {
                        sections(m, theme, t)
                    }
                    HStack(alignment: .top, spacing: m.len(4)) {
                        Image(systemName: "thermometer.medium")
                        Text(t("temp_legend")).fixedSize(horizontal: false, vertical: true)
                    }
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.label.color)
                }
                .padding(.bottom, m.len(4))
            }
        }
        .padding(.leading, m.len(16))
        .padding(.trailing, m.len(14))
        .padding(.vertical, m.len(12))
    }

    @ViewBuilder
    private func sections(_ m: Metrics, _ theme: Theme, _ t: Strings) -> some View {
        let s = model.snapshot
        let c = model.config

        if c.showCpu {
            Card(title: t("cpu"), note: s.cpuInfo.name, theme: theme, metrics: m,
                 columns: c.cpuMode == "total" ? 1 : 2,
                 temp: c.cpuMode == "total" ? .none : .reading(s.cpuTemp)) {
                if c.cpuMode == "total" {
                    MeterRow(title: t("cpu"), percent: s.cpuTotal, accent: .cpu,
                             detail: cpuDetail(s, t), temp: .reading(s.cpuTemp), theme: theme, metrics: m)
                } else {
                    ForEach(Array(s.cores.enumerated()), id: \.offset) { i, core in
                        MeterRow(title: "C\(i)", percent: core.usage, accent: .cpu,
                                 theme: theme, metrics: m)
                    }
                }
            }
        }

        if c.showRam {
            let r = s.ram
            let swap = r.swapTotalGb > 0 ? "  ·  \(t("swap")) \(Format.gb(r.swapUsedGb)) / \(Format.gb(r.swapTotalGb)) GB" : ""
            Card(title: t("memory"), note: ProcessGroups.topLine(s.processes, t) ?? "", theme: theme, metrics: m) {
                MeterRow(title: t("memory"), percent: r.usage, accent: .ram,
                         detail: "\(Format.gb(r.usedGb)) / \(Format.gb(r.totalGb)) GB  \(t("in_use"))\(swap)",
                         temp: .reading(nil), theme: theme, metrics: m)
            }
        }

        if c.showDisk {
            let disks = WidgetPages.visibleDisks(s, c)
            if !disks.isEmpty {
                Card(title: t("disk"), note: "", theme: theme, metrics: m) {
                    if c.diskMode == "total" {
                        let total = disks.reduce(0) { $0 + $1.totalGb }
                        let used = disks.reduce(0) { $0 + $1.usedGb }
                        let rate = WidgetPages.combinedRate(disks)
                        MeterRow(title: t("all_drives"), percent: total > 0 ? Int((used * 100 / total).rounded()) : 0,
                                 accent: .disk,
                                 detail: "\(Format.gb(used)) / \(Format.gb(total)) GB  ·  \(WidgetPages.rateText(rate.read, rate.write, t))",
                                 temp: .reading(disks.compactMap(\.temp).max()), theme: theme, metrics: m)
                    } else {
                        ForEach(disks, id: \.id) { d in
                            VStack(alignment: .leading, spacing: m.len(4)) {
                                if let level = model.alerts[d.id] {
                                    AlertBanner(text: t(level == .hot ? "disk_hot" : "disk_warn", d.title, String(d.usage)),
                                                level: level, theme: theme, metrics: m,
                                                dismissHelp: t("dismiss"),
                                                open: { actions?.openStorageSettings() },
                                                dismiss: { model.dismissAlert(d.id) })
                                }
                                MeterRow(title: d.title, percent: d.usage, accent: .disk,
                                         detail: diskDetail(d, t), temp: d.isNetwork ? .none : .reading(d.temp),
                                         theme: theme, metrics: m)
                            }
                        }
                    }
                }
            }
        }

        if c.showGpu {
            ForEach(Array(s.gpus.enumerated()), id: \.offset) { _, g in
                Card(title: t("gpu"), note: g.name, theme: theme, metrics: m) {
                    MeterRow(title: t("gpu"), percent: g.usage, accent: .gpu,
                             detail: WidgetPages.gpuDetail(g, t), temp: .reading(g.temp), theme: theme, metrics: m)
                    if let renderer = g.rendererUsage {
                        MeterRow(title: t("renderer"), percent: renderer, accent: .gpu, theme: theme, metrics: m)
                    }
                    if let tiler = g.tilerUsage {
                        MeterRow(title: t("tiler"), percent: tiler, accent: .gpu, theme: theme, metrics: m)
                    }
                }
            }
        }

        if c.showNetwork, !s.adapters.isEmpty {
            Card(title: t("network"), note: "", theme: theme, metrics: m) {
                if c.networkMode == "total" {
                    let down = s.adapters.reduce(0) { $0 + $1.downMb }
                    let up = s.adapters.reduce(0) { $0 + $1.upMb }
                    MeterRow(title: t("all_adapters"), percent: WidgetPages.combinedUsage(s.adapters), accent: .net,
                             detail: WidgetPages.netText(down, up), theme: theme, metrics: m)
                } else {
                    ForEach(s.adapters, id: \.id) { a in
                        MeterRow(title: a.name, percent: a.usage, accent: .net,
                                 detail: adapterDetail(a, t), theme: theme, metrics: m)
                    }
                }
            }
        }
    }

    private func cpuDetail(_ s: Snapshot, _ t: Strings) -> String {
        let info = s.cpuInfo
        let split = info.performanceCores > 0 ? " (\(info.performanceCores)P + \(info.efficiencyCores)E)" : ""
        return "\(s.cores.count) \(t("cores"))\(split)"
    }

    private func diskDetail(_ d: Disk, _ t: Strings) -> String {
        var parts: [String] = []
        if let device = d.device {
            parts.append(device.media)
            if !device.bus.isEmpty { parts.append(device.bus) }
        } else if d.isNetwork {
            parts.append(t("network"))
        }
        parts.append("\(Format.gb(d.usedGb)) / \(Format.gb(d.totalGb)) GB")
        if !d.isNetwork {
            parts.append(WidgetPages.rateText(d.readMb, d.writeMb, t))
        }
        return parts.joined(separator: "  ·  ")
    }

    private func adapterDetail(_ a: Adapter, _ t: Strings) -> String {
        let kind = t(a.wireless ? "wireless" : "wired")
        let speed = a.speedMbps > 0 ? Format.linkSpeed(a.speedMbps) : t("no_link")
        return "\(kind)  ·  \(speed)  ·  \(WidgetPages.netText(a.downMb, a.upMb))"
    }
}

/// One device kind: a heading, an optional note, and its rows.
struct Card<Rows: View>: View {
    var title: String
    var note: String
    var theme: Theme
    var metrics: Metrics
    var columns = 1
    /// A badge beside the note, for a card whose rows carry none of their own.
    var temp = TempDisplay.none
    @ViewBuilder var rows: () -> Rows

    var body: some View {
        let m = metrics
        VStack(alignment: .leading, spacing: m.len(8)) {
            HStack(alignment: .firstTextBaseline) {
                Text(title.uppercased())
                    .font(.system(size: m.type(Metrics.Font.label), weight: .semibold))
                    .foregroundStyle(theme.label.color)
                Spacer()
                Text(note)
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.label.color)
                    .lineLimit(1)
                if case .reading(let celsius) = temp {
                    TempBadge(celsius: celsius, theme: theme, metrics: m)
                }
            }
            LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: m.len(16), alignment: .top),
                                     count: columns),
                      alignment: .leading, spacing: m.len(8)) {
                rows()
            }
        }
        .padding(.horizontal, m.len(12))
        .padding(.vertical, m.len(10))
        .background(theme.control.color, in: RoundedRectangle(cornerRadius: m.len(10)))
    }
}

/// Title, bar, percentage, temperature; a detail line under them.
struct MeterRow: View {
    var title: String
    var percent: Int
    var accent: Accent
    var detail = ""
    var temp = TempDisplay.none
    var theme: Theme
    var metrics: Metrics

    var body: some View {
        let m = metrics
        VStack(alignment: .leading, spacing: m.len(3)) {
            HStack(spacing: m.len(8)) {
                Text(title)
                    .font(.system(size: m.type(Metrics.Font.body)))
                    .foregroundStyle(theme.muted.color)
                    .lineLimit(1)
                    .frame(maxWidth: m.len(150) * m.font * m.text, alignment: .leading)
                    .fixedSize(horizontal: true, vertical: false)
                MeterBar(percent: percent, color: Palette.loadColor(percent, accent).color,
                         track: theme.barEmpty.color, height: m.len(10))
                Text("\(percent)%")
                    .font(.system(size: m.type(Metrics.Font.body), weight: .semibold))
                    .foregroundStyle(theme.text.color)
                    .frame(minWidth: m.len(34) * m.font * m.text, alignment: .trailing)
                if case .reading(let celsius) = temp {
                    TempBadge(celsius: celsius, theme: theme, metrics: m)
                }
            }
            if !detail.isEmpty {
                Text(detail)
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.label.color)
                    .lineLimit(1)
                    .truncationMode(.tail)
            }
        }
    }
}

/// A drive running out of room: amber or red, click to open Storage
/// settings, × to dismiss until it gets worse.
struct AlertBanner: View {
    var text: String
    var level: DiskLevel
    var theme: Theme
    var metrics: Metrics
    var dismissHelp: String
    var open: () -> Void
    var dismiss: () -> Void

    var body: some View {
        let m = metrics
        let color = level == .hot ? Palette.hot : Palette.warm
        HStack(alignment: .top, spacing: m.len(6)) {
            Text(text)
                .font(.system(size: m.type(Metrics.Font.small)))
                .foregroundStyle(theme.text.color)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            Button(action: dismiss) {
                Image(systemName: "xmark").font(.system(size: m.type(Metrics.Font.tiny), weight: .bold))
                    .foregroundStyle(theme.muted.color)
            }
            .buttonStyle(.plain)
            .help(dismissHelp)
        }
        .padding(.horizontal, m.len(8))
        .padding(.vertical, m.len(5))
        .background(color.color.opacity(0x30 / 255.0))
        .overlay(alignment: .leading) { Rectangle().fill(color.color).frame(width: 3) }
        .clipShape(RoundedRectangle(cornerRadius: m.len(4)))
        .contentShape(Rectangle())
        .onTapGesture(perform: open)
    }
}
