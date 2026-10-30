import SwiftUI

/// The Overall view's sidebar: navigation first (R-007), the four window
/// settings that are also in the Settings window (R-015), what to display,
/// and the Settings button. Every label wraps rather than widening the
/// sidebar, Thai included (R-008).
struct Sidebar: View {
    @ObservedObject var model: AppModel
    let actions: WindowActions?
    var metrics: Metrics
    var scrollable = true

    var body: some View {
        let m = metrics
        let t = model.strings
        let theme = model.theme

        Scrolling(enabled: scrollable) {
            VStack(alignment: .leading, spacing: m.len(6)) {
                NavButton(systemName: "chart.xyaxis.line", title: t("full_data"), metrics: m) {
                    actions?.show(.full)
                }
                NavButton(systemName: "rectangle.compress.vertical", title: t("open_widget"), metrics: m) {
                    actions?.show(.widget)
                }

                Heading(text: t("window_settings"), theme: theme, metrics: m)
                WindowSettings(model: model, metrics: m)

                Heading(text: t("display_settings"), theme: theme, metrics: m)
                check(t("show_cpu"), \.showCpu)
                check(t("per_core"), mode: \.cpuMode, enabled: model.config.showCpu)
                check(t("show_ram"), \.showRam)
                check(t("show_disk"), \.showDisk)
                check(t("per_drive"), mode: \.diskMode, enabled: model.config.showDisk)
                check(t("show_network_drives"), \.includeNetworkDrives, enabled: model.config.showDisk, indent: true)
                check(t("show_gpu"), \.showGpu)
                check(t("show_network"), \.showNetwork)
                check(t("per_adapter"), mode: \.networkMode, enabled: model.config.showNetwork)

                NavButton(systemName: "gearshape", title: t("settings"), metrics: m) {
                    actions?.openSettings()
                }
                .padding(.top, m.len(8))
            }
            .padding(m.len(16))
        }
        .foregroundStyle(theme.text.color)
    }

    private func check(_ title: String, _ key: WritableKeyPath<AppConfig, Bool>,
                       enabled: Bool = true, indent: Bool = false) -> some View {
        WrappingToggle(title: title, metrics: metrics, isOn: Binding(
            get: { model.config[keyPath: key] },
            set: { model.config[keyPath: key] = $0 }))
            .disabled(!enabled)
            .padding(.leading, indent ? metrics.len(16) : 0)
    }

    /// "Per core" and friends are a checkbox over a two-valued mode.
    private func check(_ title: String, mode key: WritableKeyPath<AppConfig, String>,
                       enabled: Bool) -> some View {
        WrappingToggle(title: title, metrics: metrics, isOn: Binding(
            get: { model.config[keyPath: key] == "separated" },
            set: { model.config[keyPath: key] = $0 ? "separated" : "total" }))
            .disabled(!enabled)
            .padding(.leading, metrics.len(16))
    }
}

/// The four settings shown both in the sidebar and in the Settings window,
/// bound to the same config so the two stay in step.
struct WindowSettings: View {
    @ObservedObject var model: AppModel
    var metrics: Metrics
    var includeSnap = false

    var body: some View {
        let t = model.strings
        VStack(alignment: .leading, spacing: metrics.len(6)) {
            WrappingToggle(title: t("always_on_top"), metrics: metrics, isOn: $model.config.alwaysOnTop)
            if includeSnap {
                WrappingToggle(title: t("snap"), metrics: metrics, isOn: $model.config.snap)
            }
            WrappingToggle(title: t("light_mode"), metrics: metrics, isOn: Binding(
                get: { !model.config.isDark },
                set: { model.config.theme = $0 ? "light" : "dark" }))
            LabelledSlider(title: t("opacity"), value: $model.config.opacity, range: 0.35...1,
                           theme: model.theme, metrics: metrics)
            LabelledSlider(title: t("font_size"), value: $model.config.fontScale, range: 0.8...1.6,
                           theme: model.theme, metrics: metrics)
        }
    }
}

struct WrappingToggle: View {
    var title: String
    var metrics: Metrics
    @Binding var isOn: Bool

    var body: some View {
        Toggle(isOn: $isOn) {
            Text(title)
                .font(.system(size: metrics.type(Metrics.Font.body)))
                .fixedSize(horizontal: false, vertical: true)
        }
        .toggleStyle(.checkbox)
    }
}

/// A slider that says where it is: "Opacity  85%".
struct LabelledSlider: View {
    var title: String
    @Binding var value: Double
    var range: ClosedRange<Double>
    var theme: Theme
    var metrics: Metrics

    var body: some View {
        VStack(alignment: .leading, spacing: metrics.len(2)) {
            HStack {
                Text(title).fixedSize(horizontal: false, vertical: true)
                Spacer()
                Text("\(Int((value * 100).rounded()))%").foregroundStyle(theme.muted.color)
            }
            .font(.system(size: metrics.type(Metrics.Font.body)))
            Slider(value: $value, in: range).controlSize(.small)
        }
    }
}

struct Heading: View {
    var text: String
    var theme: Theme
    var metrics: Metrics

    var body: some View {
        Text(text.uppercased())
            .font(.system(size: metrics.type(Metrics.Font.label), weight: .semibold))
            .foregroundStyle(theme.label.color)
            .fixedSize(horizontal: false, vertical: true)
            .padding(.top, metrics.len(10))
    }
}

/// The sidebar's blue outlined buttons. The glyph and the label both wrap.
struct NavButton: View {
    var systemName: String
    var title: String
    var metrics: Metrics
    var action: () -> Void

    @State private var hover = false
    private let blue = Rgb(0x3b82f6).color

    var body: some View {
        Button(action: action) {
            HStack(alignment: .top, spacing: metrics.len(6)) {
                Image(systemName: systemName)
                    .font(.system(size: metrics.type(Metrics.Font.nav)))
                Text(title)
                    .font(.system(size: metrics.type(Metrics.Font.nav), weight: .medium))
                    .fixedSize(horizontal: false, vertical: true)
                    .multilineTextAlignment(.leading)
                Spacer(minLength: 0)
            }
            .foregroundStyle(blue)
            .padding(.horizontal, metrics.len(10))
            .padding(.vertical, metrics.len(7))
            .background(hover ? blue.opacity(0x22 / 255.0) : .clear, in: RoundedRectangle(cornerRadius: metrics.len(7)))
            .overlay(RoundedRectangle(cornerRadius: metrics.len(7)).strokeBorder(blue.opacity(0x55 / 255.0)))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .onHover { hover = $0 }
    }
}
