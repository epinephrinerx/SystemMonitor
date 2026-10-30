import SwiftUI

/// The mini widget: one page at a time, rotating every five seconds.
public struct WidgetView: View {
    @ObservedObject var model: AppModel
    let actions: WindowActions?

    @State private var hovering = false

    /// Fixed screen sizes for the controls: they stay clickable however small
    /// the widget is drawn, as in the Windows build.
    private let stepSize: CGFloat = 20
    private let buttonSize: CGFloat = 18
    private let sideRoom: CGFloat = 26

    public init(model: AppModel, actions: WindowActions?) {
        self.model = model
        self.actions = actions
    }

    public var body: some View {
        GeometryReader { geo in
            let m = Metrics(window: Metrics.widgetScale(geo.size), font: model.config.fontScale)
            let theme = model.theme
            let t = model.strings

            ZStack {
                PanelBackground(theme: theme, opacity: model.config.opacity, radius: m.len(14))

                page(model.currentPage, m, theme, t)
                    .padding(.horizontal, sideRoom)
                    .padding(.vertical, m.len(8))

                HStack {
                    stepButton("chevron.left", t("previous"), theme) { model.step(-1) }
                    Spacer()
                    stepButton("chevron.right", t("next"), theme) { model.step(1) }
                }
                .padding(.horizontal, 3)

                ResizeBands(actions: actions)
            }
            .contentShape(Rectangle())
            .windowDrag(actions) { actions?.show(.overall) }
            .onHover { hovering = $0 }
            .contextMenu { WindowMenu(model: model, actions: actions) }
        }
    }

    @ViewBuilder
    private func page(_ page: WidgetPage, _ m: Metrics, _ theme: Theme, _ t: Strings) -> some View {
        VStack(alignment: .leading, spacing: m.len(5)) {
            HStack(spacing: m.len(6)) {
                if model.paused {
                    Text(t("paused"))
                        .font(.system(size: m.type(Metrics.Font.tiny), weight: .semibold))
                        .foregroundStyle(Palette.warm.color)
                        .padding(.horizontal, m.len(4))
                        .background(Palette.tempBadge(Palette.warmAt, theme).color,
                                    in: RoundedRectangle(cornerRadius: m.len(4)))
                        .fixedSize()
                }
                Text(page.kind == .message ? "" : page.title.uppercased())
                    .font(.system(size: m.type(Metrics.Font.label), weight: .semibold))
                    .foregroundStyle(theme.label.color)
                    .lineLimit(1)
                Spacer(minLength: 0)
                if case .reading(let celsius) = page.temp {
                    TempBadge(celsius: celsius, theme: theme, metrics: m)
                }
                if let headline = page.headline {
                    Text("\(headline)%")
                        .font(.system(size: m.type(Metrics.Font.value), weight: .bold))
                        .foregroundStyle(theme.text.color)
                        .fixedSize()
                }
                IconButton(systemName: model.paused ? "play.fill" : "pause.fill",
                           help: t(model.paused ? "resume_rotation" : "pause_rotation"),
                           size: buttonSize, theme: theme) { model.paused.toggle() }
                IconButton(systemName: "xmark", help: t("close"), size: buttonSize, theme: theme) {
                    actions?.closePressed()
                }
                .opacity(hovering ? 1 : 0.35)
            }

            if page.kind == .message {
                Spacer(minLength: 0)
                Text(page.title)
                    .font(.system(size: m.type(Metrics.Font.body)))
                    .foregroundStyle(theme.muted.color)
                    .frame(maxWidth: .infinity)
                Spacer(minLength: 0)
            } else {
                bars(page, m, theme)
                Spacer(minLength: 0)
                detail(page, m, theme)
            }
        }
    }

    @ViewBuilder
    private func bars(_ page: WidgetPage, _ m: Metrics, _ theme: Theme) -> some View {
        let grid = Array(repeating: GridItem(.flexible(), spacing: m.len(10)), count: page.columns)
        LazyVGrid(columns: grid, alignment: .leading, spacing: m.len(4)) {
            ForEach(Array(page.bars.enumerated()), id: \.offset) { _, bar in
                HStack(spacing: m.len(4)) {
                    if !bar.label.isEmpty {
                        Text(bar.label)
                            .font(.system(size: m.type(Metrics.Font.small)))
                            .foregroundStyle(theme.muted.color)
                            .frame(width: m.len(18) * m.font, alignment: .leading)
                    }
                    MeterBar(percent: bar.percent, color: Palette.loadColor(bar.percent, bar.accent).color,
                             track: theme.barEmpty.color, height: m.len(page.bars.count > 1 ? 6 : 10))
                }
            }
        }
    }

    @ViewBuilder
    private func detail(_ page: WidgetPage, _ m: Metrics, _ theme: Theme) -> some View {
        let color = page.alert == .hot ? Palette.hot : page.alert == .warm ? Palette.warm : theme.muted
        Text(page.detail)
            .font(.system(size: m.type(Metrics.Font.small)))
            .foregroundStyle(color.color)
            .lineLimit(1)
            .truncationMode(.tail)
            .onTapGesture {
                if page.alert != .ok { actions?.openStorageSettings() }
            }
    }

    private func stepButton(_ glyph: String, _ help: String, _ theme: Theme,
                            _ action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Image(systemName: glyph)
                .font(.system(size: 9, weight: .bold))
                .frame(width: stepSize, height: stepSize)
                .foregroundStyle(theme.muted.color)
                .background(theme.chip.color, in: RoundedRectangle(cornerRadius: 6))
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .help(help)
    }
}

/// The right-click menu, shared by both views.
struct WindowMenu: View {
    @ObservedObject var model: AppModel
    let actions: WindowActions?

    var body: some View {
        let t = model.strings
        if model.mode == .widget {
            Button(t("show_overall")) { actions?.show(.overall) }
        } else {
            Button(t("collapse")) { actions?.show(.widget) }
        }
        Button(t("settings")) { actions?.openSettings() }
        Button(t("reset_size")) { actions?.resetSize() }
        Divider()
        Button(t("close")) { actions?.closePressed() }
    }
}
