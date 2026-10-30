import AppKit
import SysMonitorUI

// A menu bar utility: no Dock icon, no main menu. The widget and the menu bar
// item are the whole interface.
MainActor.assumeIsolated {
    let app = NSApplication.shared
    let controller = AppController()
    app.delegate = controller
    app.setActivationPolicy(.accessory)
    app.run()
}
