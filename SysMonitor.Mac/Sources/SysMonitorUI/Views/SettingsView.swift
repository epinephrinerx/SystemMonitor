import SwiftUI

/// The Settings window: the sidebar's four window settings plus the ones that
/// live only here -- snapping, the close button, refresh rate, language
/// (R-015). Follows the theme, language and font size like everything else.
public struct SettingsView: View {
    @ObservedObject var model: AppModel

    public init(model: AppModel) {
        self.model = model
    }

    public var body: some View {
        let t = model.strings
        let theme = model.theme
        let m = Metrics(window: 1, font: model.config.fontScale)

        ScrollView {
            VStack(alignment: .leading, spacing: m.len(6)) {
                Heading(text: t("window_settings"), theme: theme, metrics: m)
                WindowSettings(model: model, metrics: m, includeSnap: true)

                Heading(text: t("close_action"), theme: theme, metrics: m)
                choice($model.config.closeAction, [("exit", t("close_to_exit")), ("tray", t("close_to_tray"))], m)

                Heading(text: t("speed"), theme: theme, metrics: m)
                choice($model.config.speed, [("eco", t("eco")), ("balanced", t("balanced")), ("fast", t("fast"))], m)

                Heading(text: t("language"), theme: theme, metrics: m)
                choice($model.config.lang, [("th", "ไทย"), ("en", "English")], m)
            }
            .padding(m.len(18))
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .foregroundStyle(theme.text.color)
        .background(theme.panel.color)
        .environment(\.colorScheme, model.config.isDark ? .dark : .light)
    }

    private func choice(_ selection: Binding<String>, _ options: [(String, String)], _ m: Metrics) -> some View {
        Picker("", selection: selection) {
            ForEach(options, id: \.0) { value, title in
                Text(title)
                    .font(.system(size: m.type(Metrics.Font.body)))
                    .fixedSize(horizontal: false, vertical: true)
                    .tag(value)
            }
        }
        .pickerStyle(.radioGroup)
        .labelsHidden()
    }
}
