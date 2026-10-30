import Foundation
import SysMonitorCore

public enum ViewMode: String, Sendable {
    case widget, overall, full

    /// Double-click goes round: widget, Overall, Full, widget.
    public var next: ViewMode {
        switch self {
        case .widget: return .overall
        case .overall: return .full
        case .full: return .widget
        }
    }

    /// Escape steps back one: Full to Overall, anything else to the widget.
    public var back: ViewMode {
        self == .full ? .overall : .widget
    }
}

/// What the windows do, as the views see it. The window manager implements
/// it; the views hold it optionally so they can also be drawn offscreen.
@MainActor
public protocol WindowActions: AnyObject {
    func dragChanged()
    func dragEnded()
    func resizeChanged(_ edges: Edges)
    func resizeEnded()
    func show(_ mode: ViewMode)
    func closePressed()
    func resetSize()
    func openSettings()
    func openStorageSettings()
    /// Ask before doing something that cannot be undone.
    func confirm(_ message: String, detail: String, action: String) -> Bool
    func inform(_ message: String)
}

/// The state every view draws from: settings, the latest snapshot, and where
/// the widget is in its rotation.
@MainActor
public final class AppModel: ObservableObject {
    /// How long a widget page stays up before the next one.
    public static let rotateEvery: TimeInterval = 5

    @Published public var config: AppConfig {
        didSet {
            guard config != oldValue else { return }
            scheduleSave()
        }
    }
    @Published public private(set) var snapshot = Snapshot()
    @Published public var mode = ViewMode.widget
    @Published public private(set) var pageIndex = 0
    @Published public private(set) var alerts: [String: DiskLevel] = [:]

    /// Pausing stops the pages changing, not the values on them, and is not
    /// saved: a widget that starts frozen looks broken (R-006).
    @Published public var paused = false

    /// Every graph's history, kept whatever view is showing.
    public let histories = HistoryStore()
    @Published public private(set) var historyRevision = 0

    /// The Memory tab's process list: what is typed in its search box, which
    /// groups are open, which one is graphed.
    @Published public var processFilter = ""
    @Published public var expandedProcesses = Set<String>()
    @Published public var pickedProcess: String?

    private let store: ConfigStore?
    private let tracker = DiskAlertTracker()
    private var saveWork: DispatchWorkItem?
    private var lastStep = Date()

    public init(config: AppConfig = AppConfig(), store: ConfigStore? = nil) {
        self.config = config
        self.store = store
    }

    public var strings: Strings { Strings(config.lang) }
    public var theme: Theme { Theme.named(dark: config.isDark) }

    public func receive(_ snapshot: Snapshot) {
        self.snapshot = snapshot
        histories.push(snapshot, alsoProcess: pickedProcess)
        historyRevision &+= 1
        tracker.update(snapshot.disks.map { ($0.id, $0.usage, !$0.isNetwork) })
        if tracker.pending != alerts {
            alerts = tracker.pending
        }
    }

    public var pages: [WidgetPage] {
        WidgetPages.build(snapshot, config, strings, alerts: alerts)
    }

    public var currentPage: WidgetPage {
        let all = pages
        return all[pageIndex % all.count]
    }

    /// Step through the pages, wrapping. Stepping restarts the five seconds,
    /// and leaves a paused widget paused (R-004, R-006).
    public func step(_ by: Int) {
        let count = pages.count
        pageIndex = ((pageIndex % count + by) % count + count) % count
        lastStep = Date()
    }

    public func rotateIfDue(now: Date = Date()) {
        // The cheap checks first: building the pages walks every process.
        guard mode == .widget, !paused, now.timeIntervalSince(lastStep) >= Self.rotateEvery,
              pages.count > 1 else { return }
        step(1)
        lastStep = now
    }

    /// End processes, returning what could not be ended and why.
    public func end(_ processes: [ProcessEntry]) -> [(ProcessEntry, EndResult)] {
        let failures = processes.map { ($0, ProcessControl.end($0)) }.filter { $0.1 != .sent }
        Diag.write("End \(processes.map { "\($0.name)[\($0.pid)]" }.joined(separator: ", ")): "
            + (failures.isEmpty ? "sent" : "\(failures.count) not ended"))
        return failures
    }

    public func reason(_ result: EndResult) -> String {
        let t = strings
        switch result {
        case .sent: return ""
        case .protected: return t("reason_protected")
        case .notPermitted: return t("reason_not_permitted")
        case .gone: return t("reason_gone")
        case .failed(let code): return t("reason_failed", String(code))
        }
    }

    public func dismissAlert(_ id: String) {
        tracker.dismiss(id)
        alerts = tracker.pending
    }

    public func saveNow() {
        saveWork?.cancel()
        saveWork = nil
        store?.save(config)
    }

    /// Sliders change the config many times a second; write once they stop.
    private func scheduleSave() {
        guard store != nil else { return }
        saveWork?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.saveNow() }
        saveWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.5, execute: work)
    }
}
