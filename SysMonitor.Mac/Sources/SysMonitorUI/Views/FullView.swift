import SwiftUI

/// The Full view: a rail of devices down the left, the selected one's facts
/// and graphs on the right. The rail is vertical because a machine with
/// several drives and adapters has more names than fit across a 640-wide
/// window.
///
/// It does not scale with the window the way the Overall view does: a large
/// window here means more room for the graphs, not bigger type.
public struct FullView: View {
    @ObservedObject var model: AppModel
    let actions: WindowActions?

    /// False only for offscreen rendering; see `OverallView.scrollable`.
    var scrollable = true

    public init(model: AppModel, actions: WindowActions?) {
        self.model = model
        self.actions = actions
    }

    public var body: some View {
        let m = Metrics(window: 1, font: model.config.fontScale)
        let theme = model.theme
        let t = model.strings
        let tabs = FullTabs.build(model.snapshot, t)
        let selected = tabs.first { $0.id == model.config.fullTab } ?? tabs.first

        ZStack {
            PanelBackground(theme: theme, opacity: model.config.opacity, radius: 14)
            VStack(alignment: .leading, spacing: m.len(10)) {
                header(selected, m, theme, t)
                if tabs.isEmpty {
                    Text(t("connecting"))
                        .font(.system(size: m.type(Metrics.Font.body)))
                        .foregroundStyle(theme.muted.color)
                    Spacer()
                } else {
                    HStack(alignment: .top, spacing: m.len(14)) {
                        rail(tabs, selected?.id, m, theme)
                            .frame(width: m.len(190) * m.font)
                        if let selected {
                            Scrolling(enabled: scrollable) {
                                panel(selected, m, theme)
                            }
                        }
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 14)
            ResizeBands(actions: actions)
        }
        .contentShape(Rectangle())
        .windowDrag(actions) { actions?.show(.widget) }
        .contextMenu { WindowMenu(model: model, actions: actions) }
    }

    private func header(_ tab: FullTab?, _ m: Metrics, _ theme: Theme, _ t: Strings) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: m.len(10)) {
            Text(tab?.title ?? t("full_data"))
                .font(.system(size: m.type(Metrics.Font.heading) * 1.3, weight: .semibold))
                .foregroundStyle(theme.text.color)
            Text(tab?.hardware ?? "")
                .font(.system(size: m.type(Metrics.Font.body)))
                .foregroundStyle(theme.label.color)
                .lineLimit(1)
            Spacer()
            IconButton(systemName: "rectangle.split.2x1", help: t("show_overall"), size: 26, theme: theme) {
                actions?.show(.overall)
            }
            IconButton(systemName: "rectangle.compress.vertical", help: t("open_widget"), size: 26, theme: theme) {
                actions?.show(.widget)
            }
            IconButton(systemName: "xmark", help: t("close"), size: 26, theme: theme) {
                actions?.closePressed()
            }
        }
    }

    private func rail(_ tabs: [FullTab], _ selected: String?, _ m: Metrics, _ theme: Theme) -> some View {
        Scrolling(enabled: scrollable) {
            VStack(alignment: .leading, spacing: m.len(4)) {
                ForEach(tabs) { tab in
                    RailButton(tab: tab, selected: tab.id == selected, theme: theme, metrics: m) {
                        model.config.fullTab = tab.id
                    }
                }
            }
        }
    }

    private func panel(_ tab: FullTab, _ m: Metrics, _ theme: Theme) -> some View {
        let revision = model.historyRevision
        return VStack(alignment: .leading, spacing: m.len(14)) {
            ForEach(tab.charts) { spec in
                ChartView(spec: spec, histories: model.histories, revision: revision, theme: theme,
                          metrics: m, height: m.len(170), showLegend: true)
            }
            if !tab.smallCharts.isEmpty {
                LazyVGrid(columns: [GridItem(.adaptive(minimum: m.len(150) * m.font), spacing: m.len(12))],
                          alignment: .leading, spacing: m.len(12)) {
                    ForEach(tab.smallCharts) { spec in
                        ChartView(spec: spec, histories: model.histories, revision: revision, theme: theme,
                                  metrics: m, height: m.len(64))
                    }
                }
            }
            FactsBox(facts: tab.facts, theme: theme, metrics: m)
        }
        .padding(.trailing, m.len(4))
        .padding(.bottom, m.len(8))
    }
}

/// A device on the rail: its name, what it is, and its figure now.
struct RailButton: View {
    var tab: FullTab
    var selected: Bool
    var theme: Theme
    var metrics: Metrics
    var action: () -> Void

    @State private var hover = false

    var body: some View {
        let m = metrics
        Button(action: action) {
            VStack(alignment: .leading, spacing: m.len(2)) {
                HStack(alignment: .firstTextBaseline) {
                    Text(tab.title)
                        .font(.system(size: m.type(Metrics.Font.body), weight: .semibold))
                        .foregroundStyle(theme.text.color)
                        .lineLimit(1)
                    Spacer(minLength: m.len(4))
                    Text(tab.summary)
                        .font(.system(size: m.type(Metrics.Font.body)))
                        .foregroundStyle(theme.muted.color)
                        .fixedSize()
                }
                Text(tab.detail)
                    .font(.system(size: m.type(Metrics.Font.small)))
                    .foregroundStyle(theme.label.color)
                    .lineLimit(2)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(.horizontal, m.len(10))
            .padding(.vertical, m.len(7))
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(selected ? theme.control.color : hover ? theme.chip.color.opacity(0.6) : .clear,
                        in: RoundedRectangle(cornerRadius: m.len(7)))
            .overlay(alignment: .leading) {
                if selected {
                    RoundedRectangle(cornerRadius: 1.5).fill(Accent.cpu.rgb.color)
                        .frame(width: 3).padding(.vertical, m.len(6))
                }
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}

/// Name and value pairs, flowing into as many columns as the width allows.
struct FactsBox: View {
    var facts: [Fact]
    var theme: Theme
    var metrics: Metrics

    var body: some View {
        let m = metrics
        LazyVGrid(columns: [GridItem(.adaptive(minimum: m.len(200) * m.font), spacing: m.len(16), alignment: .topLeading)],
                  alignment: .leading, spacing: m.len(8)) {
            ForEach(Array(facts.enumerated()), id: \.offset) { _, fact in
                VStack(alignment: .leading, spacing: m.len(1)) {
                    Text(fact.name)
                        .font(.system(size: m.type(Metrics.Font.small)))
                        .foregroundStyle(theme.label.color)
                    Text(fact.value)
                        .font(.system(size: m.type(Metrics.Font.body)))
                        .foregroundStyle(theme.text.color)
                        .textSelection(.enabled)
                        .fixedSize(horizontal: false, vertical: true)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding(.horizontal, m.len(12))
        .padding(.vertical, m.len(10))
        .background(theme.control.color, in: RoundedRectangle(cornerRadius: m.len(10)))
    }
}
