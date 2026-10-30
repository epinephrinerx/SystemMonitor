import AppKit
import Combine
import SwiftUI

/// A borderless floating panel that can still take the keyboard, so Escape
/// reaches it.
final class SysPanel: NSPanel {
    var onCancel: (() -> Void)?

    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }

    override func cancelOperation(_ sender: Any?) {
        onCancel?()
    }
}

/// Owns the widget, Overall and Full panels, the menu bar item and the Settings
/// window, and does what the views ask of them.
@MainActor
public final class WindowManager: NSObject, WindowActions, NSMenuDelegate, NSWindowDelegate {
    private let model: AppModel
    private var widget: SysPanel!
    private var overall: SysPanel!
    private var full: SysPanel!
    private var settings: NSWindow?
    private var statusItem: NSStatusItem?
    private var observers = Set<AnyCancellable>()

    private var dragStart: (origin: CGPoint, mouse: CGPoint)?
    private var resizeStart: (frame: CGRect, mouse: CGPoint)?
    /// Views with no saved place yet. They are placed when first shown, on
    /// the screen the user is looking at, not the main one at launch.
    private var unplaced: Set<ViewMode> = []

    public init(model: AppModel) {
        self.model = model
    }

    public func start() {
        widget = makePanel(WidgetView(model: model, actions: self))
        overall = makePanel(OverallView(model: model, actions: self))
        full = makePanel(FullView(model: model, actions: self))
        widget.setFrame(restoredFrame(.widget), display: false)
        overall.setFrame(restoredFrame(.overall), display: false)
        full.setFrame(restoredFrame(.full), display: false)
        if model.config.overallX == nil || model.config.overallY == nil { unplaced.insert(.overall) }
        if model.config.fullW <= 0 { unplaced.insert(.full) }
        makeStatusItem()

        model.$config
            .sink { [weak self] config in self?.apply(config) }
            .store(in: &observers)

        // The app always starts in the widget (R-010).
        show(.widget)
    }

    // MARK: Panels

    private func makePanel<Content: View>(_ root: Content) -> SysPanel {
        let panel = SysPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel],
                             backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.onCancel = { [weak self] in
            guard let self else { return }
            show(model.mode.back)
        }

        let host = NSHostingView(rootView: root)
        // Without this the hosting view sizes the window to the content's
        // ideal size and fights every resize.
        host.sizingOptions = []
        panel.contentView = host
        return panel
    }

    private func panel(_ mode: ViewMode) -> SysPanel {
        switch mode {
        case .widget: return widget
        case .overall: return overall
        case .full: return full
        }
    }

    private var current: SysPanel { panel(model.mode) }
    private var panels: [SysPanel] { [widget, overall, full] }

    private func apply(_ config: AppConfig) {
        let appearance = NSAppearance(named: config.isDark ? .darkAqua : .aqua)
        for window in panels + [settings].compactMap({ $0 }) {
            window.appearance = appearance
        }
        let level: NSWindow.Level = config.alwaysOnTop ? .floating : .normal
        for panel in panels {
            panel.level = level
            panel.invalidateShadow()
        }
    }

    public func show(_ mode: ViewMode) {
        // A gesture cut short by its panel going away never reports its end;
        // its start must not carry over to the next panel's first drag.
        dragStart = nil
        resizeStart = nil
        if unplaced.remove(mode) != nil, let screen = current.screen?.visibleFrame {
            panel(mode).setFrame(firstFrame(mode, on: screen), display: false)
        }
        model.mode = mode
        let front = panel(mode)
        for other in panels where other !== front {
            other.orderOut(nil)
        }
        front.orderFrontRegardless()
        front.makeKey()
    }

    /// Hide everything; the menu bar item brings it back.
    public func hideAll() {
        dragStart = nil
        resizeStart = nil
        panels.forEach { $0.orderOut(nil) }
    }

    public func closePressed() {
        if model.config.closeAction == "exit" {
            NSApp.terminate(nil)
        } else {
            hideAll()
        }
    }

    // MARK: Moving and sizing

    public func dragChanged() {
        let mouse = NSEvent.mouseLocation
        if dragStart == nil {
            dragStart = (current.frame.origin, mouse)
        }
        guard let start = dragStart else { return }
        current.setFrameOrigin(CGPoint(x: start.origin.x + mouse.x - start.mouse.x,
                                       y: start.origin.y + mouse.y - start.mouse.y))
    }

    public func dragEnded() {
        dragStart = nil
        if model.config.snap, let visible = current.screen?.visibleFrame {
            current.setFrame(WindowGeometry.snap(current.frame, to: visible), display: true)
        }
        remember()
    }

    public func resizeChanged(_ edges: Edges) {
        let mouse = NSEvent.mouseLocation
        if resizeStart == nil {
            resizeStart = (current.frame, mouse)
        }
        guard let start = resizeStart else { return }
        let delta = CGVector(dx: mouse.x - start.mouse.x, dy: mouse.y - start.mouse.y)
        let frame: CGRect
        switch model.mode {
        case .widget:
            let visible = current.screen?.visibleFrame ?? start.frame
            frame = WindowGeometry.resizeProportional(origin: start.frame, edges: edges, delta: delta,
                                                      min: AppConfig.widgetMin, max: AppConfig.widgetMax,
                                                      visible: visible)
        case .overall:
            frame = WindowGeometry.resizeFree(origin: start.frame, edges: edges, delta: delta,
                                              min: AppConfig.overallMin, max: AppConfig.overallMax)
        case .full:
            frame = WindowGeometry.resizeFree(origin: start.frame, edges: edges, delta: delta,
                                              min: AppConfig.fullMin, max: AppConfig.fullMax)
        }
        current.setFrame(frame, display: true)
    }

    public func resizeEnded() {
        resizeStart = nil
        current.invalidateShadow()
        remember()
    }

    public func resetSize() {
        if model.mode == .full {
            current.setFrame(fullScreenFrame(current.screen?.visibleFrame), display: true)
            current.invalidateShadow()
            remember()
            return
        }
        let size = model.mode == .widget ? AppConfig.widgetSize : AppConfig.overallSize
        let frame = current.frame
        // Keep the top-left corner where it is.
        current.setFrame(CGRect(x: frame.minX, y: frame.maxY - size.height,
                                width: size.width, height: size.height), display: true)
        current.invalidateShadow()
        remember()
    }

    /// Write both panels' frames into the config. Position and size go
    /// together: half of one is a window that jumps on the next start.
    public func remember() {
        var c = model.config
        let w = widget.frame, o = overall.frame, f = full.frame
        c.widgetX = w.minX; c.widgetY = w.minY; c.widgetW = w.width; c.widgetH = w.height
        c.overallX = o.minX; c.overallY = o.minY; c.overallW = o.width; c.overallH = o.height
        // The Full view keeps "never opened" until it has been, so its first
        // opening fills the screen it opens on.
        if model.mode == .full || c.fullW > 0 {
            c.fullX = f.minX; c.fullY = f.minY; c.fullW = f.width; c.fullH = f.height
        }
        model.config = c
    }

    private func restoredFrame(_ mode: ViewMode) -> CGRect {
        let c = model.config
        let main = NSScreen.main?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1440, height: 900)
        if mode == .full {
            guard c.fullW > 0, c.fullH > 0, let x = c.fullX, let y = c.fullY else {
                return fullScreenFrame(main)
            }
            let saved = CGRect(x: x, y: y, width: c.fullW, height: c.fullH)
            let screen = NSScreen.screens.first { $0.visibleFrame.intersects(saved) }?.visibleFrame ?? main
            return WindowGeometry.keepOnScreen(saved, screen)
        }
        let size = mode == .widget ? CGSize(width: c.widgetW, height: c.widgetH)
                                   : CGSize(width: c.overallW, height: c.overallH)
        let x = mode == .widget ? c.widgetX : c.overallX
        let y = mode == .widget ? c.widgetY : c.overallY

        guard let x, let y else {
            return mode == .widget
                ? WindowGeometry.firstPlacement(size: size, visible: main)
                : CGRect(x: main.midX - size.width / 2, y: main.midY - size.height / 2,
                         width: size.width, height: size.height)
        }
        // A monitor that has gone away takes its windows with it; bring them
        // back onto a screen that is still here.
        let saved = CGRect(origin: CGPoint(x: x, y: y), size: size)
        let screen = NSScreen.screens.first { $0.visibleFrame.intersects(saved) }?.visibleFrame ?? main
        return WindowGeometry.keepOnScreen(saved, screen)
    }

    private func firstFrame(_ mode: ViewMode, on visible: CGRect) -> CGRect {
        if mode == .full { return fullScreenFrame(visible) }
        let size = panel(mode).frame.size
        return CGRect(x: visible.midX - size.width / 2, y: visible.midY - size.height / 2,
                      width: size.width, height: size.height)
    }

    /// The Full view's first size: the screen's working area, less a margin
    /// so it reads as a window rather than a takeover.
    private func fullScreenFrame(_ visible: CGRect?) -> CGRect {
        let area = visible ?? NSScreen.main?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1440, height: 900)
        return area.insetBy(dx: 12, dy: 12)
    }

    // MARK: Settings and Storage

    public func openSettings() {
        if settings == nil {
            let window = NSWindow(contentRect: CGRect(x: 0, y: 0, width: 360, height: 520),
                                  styleMask: [.titled, .closable, .resizable],
                                  backing: .buffered, defer: false)
            window.title = model.strings("settings")
            window.isReleasedWhenClosed = false
            window.contentMinSize = CGSize(width: 280, height: 240)
            window.contentView = NSHostingView(rootView: SettingsView(model: model))
            window.appearance = NSAppearance(named: model.config.isDark ? .darkAqua : .aqua)
            window.level = .floating
            window.delegate = self
            window.center()
            settings = window
        }
        settings?.title = model.strings("settings")
        NSApp.activate(ignoringOtherApps: true)
        settings?.makeKeyAndOrderFront(nil)
    }

    public func windowWillClose(_ notification: Notification) {
        if (notification.object as? NSWindow) === settings {
            model.saveNow()
        }
    }

    public func openStorageSettings() {
        let storage = URL(string: "x-apple.systempreferences:com.apple.settings.Storage")!
        if !NSWorkspace.shared.open(storage) {
            NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/Utilities/Disk Utility.app"))
        }
    }

    // MARK: Asking

    public func confirm(_ message: String, detail: String, action: String) -> Bool {
        let alert = NSAlert()
        alert.messageText = message
        alert.informativeText = detail
        alert.alertStyle = .warning
        alert.addButton(withTitle: action)
        alert.addButton(withTitle: model.strings("cancel"))
        // The question goes in front of the panel that asked it.
        NSApp.activate(ignoringOtherApps: true)
        return alert.runModal() == .alertFirstButtonReturn
    }

    public func inform(_ message: String) {
        let alert = NSAlert()
        alert.messageText = message
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
    }

    // MARK: Menu bar

    /// The app has no Dock icon, so this is how a hidden widget comes back and
    /// how the app is quit. It is always there.
    private func makeStatusItem() {
        let item = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        item.button?.image = NSImage(systemSymbolName: "gauge.medium", accessibilityDescription: "System Monitor")
            ?? NSImage(systemSymbolName: "speedometer", accessibilityDescription: "System Monitor")
        let menu = NSMenu()
        menu.delegate = self
        item.menu = menu
        statusItem = item
    }

    public func menuNeedsUpdate(_ menu: NSMenu) {
        let t = model.strings
        menu.removeAllItems()
        menu.addItem(withTitle: t("show_widget"), action: #selector(menuShowWidget), keyEquivalent: "").target = self
        menu.addItem(withTitle: t("show_overall"), action: #selector(menuShowOverall), keyEquivalent: "").target = self
        menu.addItem(withTitle: t("full_data"), action: #selector(menuShowFull), keyEquivalent: "").target = self
        menu.addItem(withTitle: t("settings") + "…", action: #selector(menuSettings), keyEquivalent: ",").target = self
        menu.addItem(.separator())
        // Quit always quits, whatever the close button is set to do (R-005).
        menu.addItem(withTitle: t("quit"), action: #selector(menuQuit), keyEquivalent: "q").target = self
    }

    /// A one-line reading for the menu bar item's tooltip.
    public func updateTooltip() {
        let s = model.snapshot
        guard s.ready else { return }
        statusItem?.button?.toolTip = "CPU \(s.cpuTotal)%  ·  RAM \(s.ram.usage)%"
    }

    @objc private func menuShowWidget() { show(.widget) }
    @objc private func menuShowOverall() { show(.overall) }
    @objc private func menuShowFull() { show(.full) }
    @objc private func menuSettings() { openSettings() }
    @objc private func menuQuit() { NSApp.terminate(nil) }
}
