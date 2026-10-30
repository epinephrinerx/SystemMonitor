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

/// Owns the widget and Overall panels, the menu bar item and the Settings
/// window, and does what the views ask of them.
@MainActor
public final class WindowManager: NSObject, WindowActions, NSMenuDelegate, NSWindowDelegate {
    private let model: AppModel
    private var widget: SysPanel!
    private var overall: SysPanel!
    private var settings: NSWindow?
    private var statusItem: NSStatusItem?
    private var observers = Set<AnyCancellable>()

    private var dragStart: (origin: CGPoint, mouse: CGPoint)?
    private var resizeStart: (frame: CGRect, mouse: CGPoint)?

    public init(model: AppModel) {
        self.model = model
    }

    public func start() {
        widget = makePanel(WidgetView(model: model, actions: self))
        overall = makePanel(OverallView(model: model, actions: self))
        widget.setFrame(restoredFrame(.widget), display: false)
        overall.setFrame(restoredFrame(.overall), display: false)
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
        panel.onCancel = { [weak self] in self?.show(.widget) }

        let host = NSHostingView(rootView: root)
        // Without this the hosting view sizes the window to the content's
        // ideal size and fights every resize.
        host.sizingOptions = []
        panel.contentView = host
        return panel
    }

    private var current: SysPanel { model.mode == .widget ? widget : overall }

    private func apply(_ config: AppConfig) {
        let appearance = NSAppearance(named: config.isDark ? .darkAqua : .aqua)
        for window in [widget, overall, settings].compactMap({ $0 }) {
            window.appearance = appearance
        }
        let level: NSWindow.Level = config.alwaysOnTop ? .floating : .normal
        widget.level = level
        overall.level = level
        widget.invalidateShadow()
        overall.invalidateShadow()
    }

    public func show(_ mode: ViewMode) {
        model.mode = mode
        let (front, back) = mode == .widget ? (widget!, overall!) : (overall!, widget!)
        back.orderOut(nil)
        front.orderFrontRegardless()
        front.makeKey()
    }

    /// Hide everything; the menu bar item brings it back.
    public func hideAll() {
        widget.orderOut(nil)
        overall.orderOut(nil)
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
        if model.mode == .widget {
            let visible = current.screen?.visibleFrame ?? start.frame
            frame = WindowGeometry.resizeProportional(origin: start.frame, edges: edges, delta: delta,
                                                      min: AppConfig.widgetMin, max: AppConfig.widgetMax,
                                                      visible: visible)
        } else {
            frame = WindowGeometry.resizeFree(origin: start.frame, edges: edges, delta: delta,
                                              min: AppConfig.overallMin, max: AppConfig.overallMax)
        }
        current.setFrame(frame, display: true)
    }

    public func resizeEnded() {
        resizeStart = nil
        current.invalidateShadow()
        remember()
    }

    public func resetSize() {
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
        let w = widget.frame, o = overall.frame
        c.widgetX = w.minX; c.widgetY = w.minY; c.widgetW = w.width; c.widgetH = w.height
        c.overallX = o.minX; c.overallY = o.minY; c.overallW = o.width; c.overallH = o.height
        model.config = c
    }

    private func restoredFrame(_ mode: ViewMode) -> CGRect {
        let c = model.config
        let main = NSScreen.main?.visibleFrame ?? CGRect(x: 0, y: 0, width: 1440, height: 900)
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
    @objc private func menuSettings() { openSettings() }
    @objc private func menuQuit() { NSApp.terminate(nil) }
}
