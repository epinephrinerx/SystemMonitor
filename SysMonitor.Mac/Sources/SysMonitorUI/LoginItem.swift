import ServiceManagement

/// Opening at login, through `SMAppService` (R-029). The system keeps the
/// state, not our config: the user can switch it off in System Settings, and
/// a copy here would then be wrong.
@MainActor
final class LoginItem: ObservableObject {
    @Published private(set) var status = SMAppService.mainApp.status
    @Published private(set) var error: String?

    /// Asked for counts as on: macOS may want the user to approve it first.
    var isOn: Bool { status == .enabled || status == .requiresApproval }

    func set(_ on: Bool) {
        do {
            if on {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
            error = nil
        } catch {
            self.error = error.localizedDescription
            Diag.write("Login item \(on ? "register" : "unregister") failed: \(error.localizedDescription)")
        }
        refresh()
    }

    func refresh() {
        status = SMAppService.mainApp.status
    }

    func openSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
